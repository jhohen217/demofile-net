using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DemoFile;
using DemoFile.Game.Cs;
using SharedTypes;

public class POVWriter
{
    private const int BUFFER_SIZE = 32768;
    private const int ESTIMATED_PLAYERS = 10;
    private const int ESTIMATED_LINES = 10000;

    private class PlayerInfo : PlayerState
    {
        public int? DeathTick { get; set; }
    }

    private class KillCollectionTracker
    {
        public KillCollection Collection { get; set; }
        public string OutputPath { get; set; }
        public Dictionary<string, List<string>> PlayerData { get; set; }
        public Dictionary<string, PlayerInfo> Players { get; set; }
        public Dictionary<string, string> LastKnownWeapons { get; set; }
        public int RoundFreezeEnd { get; set; }
        public int RoundEndTick { get; set; }
        public int ExpectedKills { get; set; }
        public int CurrentKills { get; set; }

        public KillCollectionTracker()
        {
            PlayerData = new Dictionary<string, List<string>>(ESTIMATED_PLAYERS);
            Players = new Dictionary<string, PlayerInfo>(ESTIMATED_PLAYERS);
            LastKnownWeapons = new Dictionary<string, string>(ESTIMATED_PLAYERS);
        }
    }

    private class CsvKillCollection
    {
        public string Type { get; set; }
        public string Player { get; set; }
        public string SteamID { get; set; }
        public int Tick { get; set; }
        public int KillCount { get; set; }
        public int TickDuration { get; set; }
        public int Round { get; set; }
        public string Map { get; set; }
    }

    private static readonly List<KillCollectionTracker> activeCollections = new(10);
    private static string outputDirectory;
    private static string demoPath;
    private static readonly List<CsvKillCollection> killCollectionsToTrack = new(10);
    private static readonly Dictionary<string, List<KillEvent>> collectionKills = new(10);
    private static readonly Dictionary<int, (int startTick, int endTick, int freezeEnd)> csvRoundTicks = new(30);

    private static void LoadRoundTicksFromCsv(string[] lines)
    {
        bool inRoundsSection = false;
        Console.WriteLine("Starting to load round ticks...");
        foreach (var line in lines)
        {
            if (line.StartsWith("[ROUNDS]"))
            {
                Console.WriteLine("Found [ROUNDS] section");
                inRoundsSection = true;
                continue;
            }
            
            if (inRoundsSection)
            {
                if (line.StartsWith("["))
                {
                    Console.WriteLine("End of [ROUNDS] section");
                    break;
                }
                
                if (string.IsNullOrWhiteSpace(line))
                {
                    Console.WriteLine("Skipping empty line");
                    continue;
                }
                
                if (line.StartsWith("Round,"))
                {
                    Console.WriteLine("Skipping header line");
                    continue;
                }

                Console.WriteLine($"Processing line: {line}");
                var parts = line.Split(',');
                if (parts.Length >= 4 && int.TryParse(parts[0], out var round))
                {
                    csvRoundTicks[round] = (
                        int.Parse(parts[1]),
                        int.Parse(parts[2]),
                        int.Parse(parts[3])
                    );
                    Console.WriteLine($"Added round {round}: Start={parts[1]}, End={parts[2]}, Freeze={parts[3]}");
                }
            }
            else if (line.StartsWith("["))
            {
                Console.WriteLine($"Skipping section: {line}");
            }
        }
        Console.WriteLine($"Finished loading round ticks. Total rounds: {csvRoundTicks.Count}");
    }

    private static PlayerInfo GetPlayerInfo(CCSPlayerController player)
    {
        if (player?.PlayerPawn == null) return null;

        var pawn = player.PlayerPawn;
        return new PlayerInfo
        {
            Name = player.PlayerName,
            SteamID = player.SteamID.ToString(),
            Team = player.CSTeamNum == CSTeamNumber.Terrorist ? "TERRORIST" : "CT",
            Position = new Position(pawn.Origin.X, pawn.Origin.Y, pawn.Origin.Z),
            ViewAngles = new ViewAngles(pawn.EyeAngles.Pitch, pawn.EyeAngles.Yaw)
        };
    }

    private static string LoadDemoPathFromCsv(string[] lines)
    {
        foreach (var line in lines)
        {
            if (line.StartsWith("DemoPath,"))
            {
                var path = line.Substring(9).Trim('"');
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException("Demo file not found: " + path);
                }
                demoPath = path;
                return path;
            }
        }
        throw new Exception("Demo path not found in CSV file");
    }

    private static void LoadKillCollectionsFromCsv(string[] lines)
    {
        string mapName = null;
        bool inDemoInfo = false;
        bool inKillCollections = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("[DEMO_INFO]"))
            {
                inDemoInfo = true;
                inKillCollections = false;
            }
            else if (line.StartsWith("[KILL_COLLECTIONS]"))
            {
                inDemoInfo = false;
                inKillCollections = true;
            }
            else if (line.StartsWith("["))
            {
                inDemoInfo = false;
                inKillCollections = false;
            }
            else if (inDemoInfo && line.StartsWith("MapName,"))
            {
                mapName = line.Substring(8).Trim('"');
            }
            else if (inKillCollections && !string.IsNullOrWhiteSpace(line) && !line.StartsWith("Type,"))
            {
                var parts = line.Split(',');
                if (parts.Length >= 6 && (parts[0] == KillCollection.TYPE_MULTI || parts[0] == KillCollection.TYPE_QUAD || parts[0] == KillCollection.TYPE_ACE))
                {
                    if (string.IsNullOrEmpty(mapName))
                    {
                        throw new Exception("Map name not found in CSV file");
                    }

                    killCollectionsToTrack.Add(new CsvKillCollection
                    {
                        Type = parts[0],
                        Player = parts[1].Trim('"'),
                        SteamID = parts[2],
                        Tick = int.Parse(parts[3]),
                        KillCount = int.Parse(parts[4]),
                        TickDuration = int.Parse(parts[5]),
                        Round = int.Parse(parts[6]),
                        Map = mapName
                    });
                }
            }
        }
    }

    private static void ProcessKillEvent(CsDemoParser demo, int tick, string attackerName, PlayerInfo attackerInfo, PlayerInfo victimInfo, string weapon)
    {
        foreach (var activeCollection in activeCollections)
        {
            if (activeCollection.Players.TryGetValue(victimInfo.SteamID, out var victim))
            {
                victim.DeathTick = tick;
            }
            if (activeCollection.Players.ContainsKey(attackerInfo.SteamID))
            {
                activeCollection.LastKnownWeapons[attackerInfo.SteamID] = weapon;
            }
        }

        var killEvent = new KillEvent
        {
            Attacker = attackerInfo,
            Victim = victimInfo,
            Tick = tick,
            Weapon = weapon
        };

        foreach (var collection in killCollectionsToTrack)
        {
            if (collection.Player == attackerName && tick >= collection.Tick && tick <= collection.Tick + collection.TickDuration)
            {
                var key = $"{collection.Type}_{collection.Player}_round{collection.Round}_tick{collection.Tick}";
                
                if (!collectionKills.TryGetValue(key, out var kills))
                {
                    kills = new List<KillEvent>();
                    collectionKills[key] = kills;
                }
                
                kills.Add(killEvent);

                var tracker = activeCollections.FirstOrDefault(t => t.Collection.Player == collection.Player);
                if (tracker == null) continue;

                tracker.Collection.Kills = kills;
                tracker.CurrentKills = kills.Count;
            }
        }
    }

    private static void ProcessTick(CsDemoParser demo, int tick)
    {
        if (tick % 10000 == 0)
        {
            Console.WriteLine($"Processing tick {tick}");
        }

        // Process new collections at round start
        foreach (var collection in killCollectionsToTrack)
        {
            if (activeCollections.Any(t => t.Collection.Player == collection.Player))
                continue;

            if (!csvRoundTicks.TryGetValue(collection.Round, out var roundInfo))
                continue;

            if (tick != roundInfo.startTick)
                continue;

            var nextRoundTick = csvRoundTicks
                .Where(r => r.Key > collection.Round)
                .Select(r => r.Value.startTick)
                .DefaultIfEmpty(roundInfo.endTick)
                .Min();

            Console.WriteLine($"Starting collection for {collection.Type} by {collection.Player} in round {collection.Round}");
            var tracker = new KillCollectionTracker
            {
                Collection = new KillCollection(collection.KillCount)
                {
                    Player = collection.Player,
                    KillCount = collection.KillCount,
                    Tick = collection.Tick,
                    TickDuration = collection.TickDuration,
                    Round = collection.Round,
                    RoundStartTick = roundInfo.startTick,
                    Map = collection.Map,
                    Type = collection.Type
                },
                OutputPath = Path.Combine(outputDirectory, $"{collection.Type}_{collection.Player}_round{collection.Round}_tick{collection.Tick}.csv"),
                RoundFreezeEnd = roundInfo.freezeEnd,
                RoundEndTick = nextRoundTick,
                ExpectedKills = collection.KillCount,
                CurrentKills = 0
            };

            foreach (var player in demo.Players)
            {
                if (player != null)
                {
                    var playerInfo = GetPlayerInfo(player);
                    if (playerInfo != null)
                    {
                        tracker.Players[playerInfo.SteamID] = playerInfo;
                        tracker.PlayerData[playerInfo.SteamID] = new List<string>(ESTIMATED_LINES);
                    }
                }
            }

            activeCollections.Add(tracker);
        }

        // Process active collections
        foreach (var tracker in activeCollections.ToList())
        {
            if (tick >= tracker.RoundFreezeEnd && tick <= tracker.RoundEndTick)
            {
                var playerStates = new Dictionary<string, (PlayerInfo, string)>(ESTIMATED_PLAYERS);

                foreach (var player in demo.Players)
                {
                    if (player == null) continue;

                    var playerInfo = GetPlayerInfo(player);
                    if (playerInfo == null) continue;

                    if (!tracker.Players.TryGetValue(playerInfo.SteamID, out var existingPlayer))
                    {
                        existingPlayer = playerInfo;
                        tracker.Players[playerInfo.SteamID] = existingPlayer;
                        tracker.PlayerData[playerInfo.SteamID] = new List<string>(ESTIMATED_LINES);
                    }

                    if (existingPlayer.DeathTick.HasValue && tick > existingPlayer.DeathTick.Value)
                    {
                        playerInfo = new PlayerInfo
                        {
                            Name = playerInfo.Name,
                            SteamID = playerInfo.SteamID,
                            Team = playerInfo.Team,
                            Position = Position.Zero,
                            ViewAngles = ViewAngles.Zero,
                            DeathTick = existingPlayer.DeathTick
                        };
                    }
                    else
                    {
                        playerInfo.DeathTick = existingPlayer.DeathTick;
                    }

                    string weapon = "";
                    if (player.PlayerName == tracker.Collection.Player)
                    {
                        var currentKill = tracker.Collection.Kills.FirstOrDefault(k => k.Tick == tick);
                        var lastKill = tracker.Collection.Kills.Where(k => k.Tick < tick).OrderByDescending(k => k.Tick).FirstOrDefault();
                        var nextKill = tracker.Collection.Kills.Where(k => k.Tick > tick).OrderBy(k => k.Tick).FirstOrDefault();

                        if (currentKill != null)
                        {
                            weapon = currentKill.Weapon;
                            tracker.LastKnownWeapons[playerInfo.SteamID] = weapon;
                        }
                        else if (lastKill != null)
                        {
                            weapon = lastKill.Weapon;
                        }
                        else if (nextKill != null)
                        {
                            weapon = nextKill.Weapon;
                        }
                    }
                    else
                    {
                        weapon = tracker.LastKnownWeapons.GetValueOrDefault(playerInfo.SteamID, "");
                    }

                    tracker.Players[playerInfo.SteamID] = playerInfo;
                    playerStates[playerInfo.SteamID] = (playerInfo, weapon);
                }

                foreach (var (steamId, (player, weapon)) in playerStates)
                {
                    var data = tracker.PlayerData[steamId];
                    var line = $"{tick},{player.Name},{weapon},{player.Team},{player.Position.X},{player.Position.Y},{player.Position.Z},{player.ViewAngles.Pitch},{player.ViewAngles.Yaw}";
                    
                    if (data.Count == 0 || !data[^1].StartsWith($"{tick},"))
                    {
                        data.Add(line);
                    }
                }
            }

            if (tick == tracker.RoundEndTick)
            {
                Console.WriteLine($"Round end tick {tick} reached for {tracker.Collection.Type} by {tracker.Collection.Player} in round {tracker.Collection.Round}");
                WriteCollectionToFile(tracker);
                activeCollections.Remove(tracker);
            }
        }
    }

    private static void WriteCollectionToFile(KillCollectionTracker tracker)
    {
        Console.WriteLine($"Writing POV file to: {tracker.OutputPath}");
        Console.WriteLine($"Collection details: {tracker.Collection.Type}_{tracker.Collection.Player}_round{tracker.Collection.Round}_tick{tracker.Collection.Tick}");
        using var writer = new StreamWriter(tracker.OutputPath, false, Encoding.UTF8, BUFFER_SIZE);
        var sb = new StringBuilder(4096);
        var weapons = new HashSet<string>();

        // Collect weapon information
        foreach (var kill in tracker.Collection.Kills)
        {
            if (!string.IsNullOrEmpty(kill.Weapon))
            {
                weapons.Add(kill.Weapon);
            }
        }
        var weaponsList = weapons.OrderBy(w => w).ToList();

        // Calculate metrics
        var victimPositions = tracker.Collection.Kills.Select(k => k.Victim.Position).ToList();
        var killerPositions = tracker.Collection.Kills.Select(k => k.Attacker.Position).ToList();
        
        // Calculate radii (max distance from centroid)
        var victimsRadius = CalculateRadius(victimPositions);
        var killerRadius = CalculateRadius(killerPositions);
        
        // Calculate total killer movement distance
        var killerMoveDistance = CalculateMovementDistance(killerPositions);

        // Write header information
        sb.AppendLine("[KILL_COLLECTION]")
          .AppendLine($"Player,{tracker.Collection.Player}")
          .AppendLine($"Round,{tracker.Collection.Round}")
          .AppendLine($"KillCount,{tracker.Collection.KillCount}")
          .AppendLine($"TickDuration,{tracker.Collection.TickDuration}")
          .AppendLine($"RoundStartTick,{tracker.Collection.RoundStartTick}")
          .AppendLine($"RoundFreezeEnd,{tracker.RoundFreezeEnd}")
          .AppendLine($"RoundEndTick,{tracker.RoundEndTick}")
          .AppendLine($"DemoPath,{demoPath}")
          .AppendLine($"DemoName,{Path.GetFileName(demoPath)}")
          .AppendLine($"MapName,{tracker.Collection.Map}")
          .AppendLine($"Weapons,{string.Join(",", weaponsList)}")
          .AppendLine($"VictimsRadius,{victimsRadius:F2}")
          .AppendLine($"KillerRadius,{killerRadius:F2}")
          .AppendLine($"KillerMoveDistance,{killerMoveDistance:F2}")
          .AppendLine();

        // Write collection info
        sb.AppendLine("[COLLECTION_INFO]")
          .AppendLine("Type,KillTick,KillerName,KillerSteamid,PlayerTeam,PlayerWeapon,PlayerPosX,PlayerPosY,PlayerPosZ,PlayerViewPitch,PlayerViewYaw,VictimName,VictimSteamid,VictimTeam,VictimPosX,VictimPosY,VictimPosZ,DistanceToEnemy,TicksSinceLastKill,DistanceMovedSinceLastKill");

        foreach (var kill in tracker.Collection.Kills)
        {
            sb.AppendLine($"{tracker.Collection.Type},{kill.Tick},{kill.Attacker.Name},{kill.Attacker.SteamID},{kill.Attacker.Team},{kill.Weapon},{kill.Attacker.Position.X},{kill.Attacker.Position.Y},{kill.Attacker.Position.Z},{kill.Attacker.ViewAngles.Pitch},{kill.Attacker.ViewAngles.Yaw},{kill.Victim.Name},{kill.Victim.SteamID},{kill.Victim.Team},{kill.Victim.Position.X},{kill.Victim.Position.Y},{kill.Victim.Position.Z},{kill.DistanceToEnemy:F2},{kill.TicksSinceLastKill},{kill.DistanceMovedSinceLastKill:F2}");
        }
        sb.AppendLine();

        // Write player data
        foreach (var (steamId, playerInfo) in tracker.Players)
        {
            sb.AppendLine($"\n[{steamId}]")
              .AppendLine("Tick,PlayerName,Weapon,Team,PosX,PosY,PosZ,ViewPitch,ViewYaw");

            foreach (var line in tracker.PlayerData[steamId].OrderBy(line => int.Parse(line.Split(',')[0])))
            {
                sb.AppendLine(line);
            }
        }

        writer.Write(sb);

        // Set Windows Shell metadata
        ShellMetadata.SetPOVMetadata(
            tracker.OutputPath,
            tracker.Collection.Player,
            tracker.Collection.Kills[0].Attacker.SteamID,  // Get SteamID from first kill
            tracker.Collection.Map,
            tracker.Collection.KillCount,
            tracker.Collection.Round,
            tracker.Collection.TickDuration,
            tracker.Collection.Type,
            string.Join(", ", weaponsList),
            victimsRadius,
            killerRadius,
            killerMoveDistance
        );
    }

    private static float CalculateRadius(List<Position> positions)
    {
        if (positions.Count == 0) return 0;
        if (positions.Count == 1) return 0;

        // Calculate centroid
        var centroid = new Position(
            positions.Average(p => p.X),
            positions.Average(p => p.Y),
            positions.Average(p => p.Z)
        );

        // Find maximum distance from centroid
        return positions.Max(p => (float)Math.Sqrt(
            Math.Pow(p.X - centroid.X, 2) +
            Math.Pow(p.Y - centroid.Y, 2) +
            Math.Pow(p.Z - centroid.Z, 2)
        ));
    }

    private static float CalculateMovementDistance(List<Position> positions)
    {
        if (positions.Count <= 1) return 0;

        float totalDistance = 0;
        for (int i = 1; i < positions.Count; i++)
        {
            var prev = positions[i - 1];
            var curr = positions[i];
            totalDistance += (float)Math.Sqrt(
                Math.Pow(curr.X - prev.X, 2) +
                Math.Pow(curr.Y - prev.Y, 2) +
                Math.Pow(curr.Z - prev.Z, 2)
            );
        }
        return totalDistance;
    }

    public static async Task RunParser(string csvPath, string outputDir = null)
    {
        Console.WriteLine("Starting POV parser...");
        if (string.IsNullOrEmpty(outputDir))
        {
            outputDir = Config.LoadConfig().project.TickByTickParse;
        }
        outputDirectory = outputDir;
        Directory.CreateDirectory(outputDir);
        Console.WriteLine($"Using output directory: {outputDir}");

        var lines = await File.ReadAllLinesAsync(csvPath);
        Console.WriteLine("Loading CSV data...");
        LoadRoundTicksFromCsv(lines);
        Console.WriteLine($"Loaded {csvRoundTicks.Count} round ticks");
        LoadDemoPathFromCsv(lines);
        Console.WriteLine($"Using demo file: {demoPath}");
        LoadKillCollectionsFromCsv(lines);
        Console.WriteLine($"Found {killCollectionsToTrack.Count} kill collections to track");

        var demo = new CsDemoParser();
        demo.Source1GameEvents.PlayerDeath += (e) =>
        {
            var killer = demo.Players.FirstOrDefault(p => p.PlayerName == e.Attacker?.PlayerName);
            var victim = demo.Players.FirstOrDefault(p => p.PlayerName == e.Player?.PlayerName);

            if (killer != null && victim != null)
            {
                var killerInfo = GetPlayerInfo(killer);
                var victimInfo = GetPlayerInfo(victim);

                if (killerInfo != null && victimInfo != null)
                {
                    ProcessKillEvent(demo, demo.CurrentDemoTick.Value, killer.PlayerName, killerInfo, victimInfo, e.Weapon);
                }
            }
        };

        using var stream = File.OpenRead(demoPath);
        var reader = DemoFileReader.Create(demo, stream);
        await reader.StartReadingAsync(default);

        int lastTick = -1;
        int sameTickCount = 0;

        while (await reader.MoveNextAsync(default))
        {
            var currentTick = demo.CurrentDemoTick.Value;
            if (currentTick == lastTick)
            {
                if (++sameTickCount > 1000)
                    break;
            }
            else
            {
                lastTick = currentTick;
                sameTickCount = 0;
            }

            ProcessTick(demo, currentTick);
        }
    }
}
