using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DemoFile;
using DemoFile.Game;
using DemoFile.Game.Cs;
using DemoFile.Sdk;
using SharedTypes;

public static class Constants
{
    public const int PADDING_TICKS = 256;
    public const int BUFFER_SIZE = 32768;
    public const int ESTIMATED_PLAYERS = 10;
    public const int ESTIMATED_ROUNDS = 30;
    public const int ESTIMATED_KILLS = 100;
    public const int ESTIMATED_COLLECTIONS = 10;
}

public class CsvWriter : IDisposable
{
    internal readonly StreamWriter writer;
    private readonly string mapName;
    private readonly StringBuilder stringBuilder;
    public readonly List<KillCollection> KillCollections;
    private readonly HashSet<string> writtenLines;
    
    public CsvWriter(string outputPath, string mapName)
    {
        writer = new StreamWriter(outputPath, false, Encoding.UTF8, Constants.BUFFER_SIZE) { AutoFlush = false };
        this.mapName = EscapeCsv(mapName);
        stringBuilder = new StringBuilder(4096);
        KillCollections = new List<KillCollection>(Constants.ESTIMATED_COLLECTIONS);
        writtenLines = new HashSet<string>(Constants.ESTIMATED_KILLS);
    }

    public string EscapeCsv(string field)
    {
        if (string.IsNullOrEmpty(field)) return string.Empty;
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }

    public void WriteMetadata(Dictionary<int, (int startTick, int? endTick)> roundTicks, int totalTicks, float tickRate, string demoPath, int collectionIndex)
    {
        var collection = KillCollections[collectionIndex];
        var roundInfo = roundTicks[collection.Round];
        var roundFreezeEnd = roundInfo.startTick + 1224;
        var endTick = roundInfo.endTick ?? totalTicks;

        stringBuilder.Clear()
            .AppendLine("[DEMO_INFO]")
            .AppendLine($"DemoPath,{EscapeCsv(demoPath)}")
            .AppendLine($"DemoName,{EscapeCsv(Path.GetFileName(demoPath))}")
            .AppendLine($"MapName,{mapName}")
            .AppendLine($"TotalTicks,{totalTicks}")
            .AppendLine($"AceCount,{KillCollections.Count(c => c.KillCount == 5 && !c.IsMultiKill)}")
            .AppendLine($"QuadCount,{KillCollections.Count(c => c.KillCount == 4 && !c.IsMultiKill)}")
            .AppendLine($"MultiCount,{KillCollections.Count(c => c.IsMultiKill)}")
            .AppendLine($"TripleCount,{KillCollections.Count(c => c.KillCount == 3 && !c.IsMultiKill)}")
            .AppendLine()
            .AppendLine("[ROUNDS]")
            .AppendLine("Round,StartTick,EndTick,RoundFreezeEnd");

        foreach (var (round, (startTick, roundEndTick)) in roundTicks.OrderBy(x => x.Key))
        {
            var freezeEnd = startTick + 1224;
            stringBuilder.AppendLine($"{round},{startTick},{roundEndTick ?? -1},{freezeEnd}");
        }
        stringBuilder.AppendLine();

        writer.Write(stringBuilder);
    }

    public void AddKillCollection(KillCollection collection)
    {
        KillCollections.Add(collection);
    }

    public void WritePlayerState(PlayerState state)
    {
        var key = $"{state.Tick}_{state.Name}";
        if (!writtenLines.Contains(key))
        {
            stringBuilder.Clear()
                .Append(state.Tick).Append(",Position,")
                .Append(EscapeCsv(state.Name)).Append(',')
                .Append(state.SteamID).Append(',')
                .Append(state.Team).Append(',')
                .Append(EscapeCsv(state.ActiveWeapon)).Append(',')
                .Append(state.Position.X).Append(',')
                .Append(state.Position.Y).Append(',')
                .Append(state.Position.Z).Append(',')
                .Append(state.ViewAngles.Pitch).Append(',')
                .Append(state.ViewAngles.Yaw).AppendLine(",,,,,,");

            writer.Write(stringBuilder);
            writtenLines.Add(key);
        }
    }

    public void WriteKillEvent(PlayerState killer, PlayerState victim, string weapon)
    {
        stringBuilder.Clear()
            .Append(killer.Tick).Append(",Kill,")
            .Append(EscapeCsv(killer.Name)).Append(',')
            .Append(killer.SteamID).Append(',')
            .Append(killer.Team).Append(',')
            .Append(EscapeCsv(weapon)).Append(',')
            .Append(killer.Position.X).Append(',')
            .Append(killer.Position.Y).Append(',')
            .Append(killer.Position.Z).Append(',')
            .Append(killer.ViewAngles.Pitch).Append(',')
            .Append(killer.ViewAngles.Yaw).Append(',')
            .Append(EscapeCsv(victim.Name)).Append(',')
            .Append(victim.SteamID).Append(',')
            .Append(victim.Team).Append(',')
            .Append(victim.Position.X).Append(',')
            .Append(victim.Position.Y).Append(',')
            .Append(victim.Position.Z).AppendLine();

        writer.Write(stringBuilder);
    }

    public void WriteCollections()
    {
        stringBuilder.Clear()
            .AppendLine("[KILL_COLLECTIONS]")
            .AppendLine("Type,KillerName,SteamID,KillTick,KillCount,TickDuration,Round,StartTick,EndTick");

        foreach (var collection in KillCollections)
        {
            var lastKillTick = collection.Kills.Max(k => k.Tick);
            stringBuilder.Append(collection.Type).Append(',')
                .Append(collection.Player).Append(',')
                .Append(collection.Kills[0].Attacker.SteamID).Append(',')
                .Append(collection.Tick).Append(',')
                .Append(collection.KillCount).Append(',')
                .Append(collection.TickDuration).Append(',')
                .Append(collection.Round).Append(',')
                .Append(collection.Tick).Append(',')
                .Append(lastKillTick).AppendLine();
        }
        stringBuilder.AppendLine();

        writer.Write(stringBuilder);

        // Write individual collections
        for (int i = 0; i < KillCollections.Count; i++)
        {
            var collection = KillCollections[i];
            
            stringBuilder.Clear()
                .AppendLine($"[COLLECTION {i + 1}]")
                .AppendLine("Type,KillTick,KillerName,KillerSteamid,PlayerTeam,PlayerWeapon,PlayerPosX,PlayerPosY,PlayerPosZ,PlayerViewPitch,PlayerViewYaw,VictimName,VictimSteamid,VictimTeam,VictimPosX,VictimPosY,VictimPosZ,DistanceToEnemy,TicksSinceLastKill,DistanceMovedSinceLastKill");

            foreach (var kill in collection.Kills)
            {
                stringBuilder.Append(collection.Type).Append(',')
                    .Append(kill.Tick).Append(',')
                    .Append(EscapeCsv(kill.Attacker.Name)).Append(',')
                    .Append(kill.Attacker.SteamID).Append(',')
                    .Append(kill.Attacker.Team).Append(',')
                    .Append(EscapeCsv(kill.Weapon)).Append(',')
                    .Append(kill.Attacker.Position.X).Append(',')
                    .Append(kill.Attacker.Position.Y).Append(',')
                    .Append(kill.Attacker.Position.Z).Append(',')
                    .Append(kill.Attacker.ViewAngles.Pitch).Append(',')
                    .Append(kill.Attacker.ViewAngles.Yaw).Append(',')
                    .Append(EscapeCsv(kill.Victim.Name)).Append(',')
                    .Append(kill.Victim.SteamID).Append(',')
                    .Append(kill.Victim.Team).Append(',')
                    .Append(kill.Victim.Position.X).Append(',')
                    .Append(kill.Victim.Position.Y).Append(',')
                    .Append(kill.Victim.Position.Z).Append(',')
                    .AppendFormat("{0:F2}", kill.DistanceToEnemy).Append(',')
                    .Append(kill.TicksSinceLastKill).Append(',')
                    .AppendFormat("{0:F2}", kill.DistanceMovedSinceLastKill).AppendLine();
            }
            stringBuilder.AppendLine();
            writer.Write(stringBuilder);
        }
    }

    public void Dispose()
    {
        writer.Flush();
        writer.Dispose();
    }
}

public class KillCollectionWriter
{
    private static readonly Dictionary<int, Dictionary<string, List<KillEvent>>> TickKills = new(Constants.ESTIMATED_ROUNDS);
    private static readonly Dictionary<string, Dictionary<int, List<KillEvent>>> PlayerRoundKills = new(Constants.ESTIMATED_PLAYERS);
    private static int currentRound = 0;
    private static readonly Dictionary<int, (int startTick, int? endTick)> RoundTicks = new(Constants.ESTIMATED_ROUNDS);
    private static readonly Dictionary<string, Dictionary<int, PlayerState>> PlayerStates = new(Constants.ESTIMATED_PLAYERS);

    private static PlayerState GetPlayerState(CCSPlayerController player, CsDemoParser demo)
    {
        if (player?.PlayerPawn == null) return null;

        var steamId = player.SteamID;
        var steamIdString = steamId.ToString();
        if (steamIdString == "0" || steamIdString == demo.CurrentDemoTick.Value.ToString())
            return null;

        var pawn = player.PlayerPawn;
        return new PlayerState
        {
            Name = player.PlayerName,
            SteamID = steamIdString,
            Team = player.CSTeamNum == CSTeamNumber.Terrorist ? "TERRORIST" : "CT",
            Position = new Position(pawn.Origin.X, pawn.Origin.Y, pawn.Origin.Z),
            ViewAngles = new ViewAngles(pawn.EyeAngles.Pitch, pawn.EyeAngles.Yaw),
            ActiveWeapon = pawn.ActiveWeapon?.ServerClass.Name ?? "none",
            Tick = demo.CurrentDemoTick.Value
        };
    }

    private static void ProcessKillEvent(int tick, string attackerName, PlayerState attackerState, PlayerState victimState, string weapon, CsDemoParser demo)
    {
        // Track kills by tick for multi-kills
        if (!TickKills.TryGetValue(tick, out var tickKillDict))
        {
            tickKillDict = new Dictionary<string, List<KillEvent>>(Constants.ESTIMATED_PLAYERS);
            TickKills[tick] = tickKillDict;
        }
        if (!tickKillDict.TryGetValue(attackerName, out var tickKills))
        {
            tickKills = new List<KillEvent>();
            tickKillDict[attackerName] = tickKills;
        }

        // Track kills by player and round for aces
        if (!PlayerRoundKills.TryGetValue(attackerName, out var playerRoundDict))
        {
            playerRoundDict = new Dictionary<int, List<KillEvent>>(Constants.ESTIMATED_ROUNDS);
            PlayerRoundKills[attackerName] = playerRoundDict;
        }
        if (!playerRoundDict.TryGetValue(currentRound, out var roundKills))
        {
            roundKills = new List<KillEvent>();
            playerRoundDict[currentRound] = roundKills;
        }

        var killEvent = new KillEvent
        {
            Attacker = attackerState,
            Victim = victimState,
            Tick = tick,
            Weapon = weapon
        };

        killEvent.CalculateDistances(roundKills.Count > 0 ? roundKills[roundKills.Count - 1] : null);

        tickKills.Add(killEvent);
        roundKills.Add(killEvent);

        // Store states for all players at this tick
        foreach (var player in demo.Players)
        {
            if (player == null) continue;

            var state = GetPlayerState(player, demo);
            if (state == null) continue;

            if (!PlayerStates.TryGetValue(state.SteamID, out var playerTickDict))
            {
                playerTickDict = new Dictionary<int, PlayerState>();
                PlayerStates[state.SteamID] = playerTickDict;
            }
            playerTickDict[tick] = state;
        }
    }

    private static void ProcessTickKills(CsvWriter csvWriter, CsDemoParser demo)
    {
        // Process multi-kills (3+ kills in same tick)
        foreach (var (tick, attackerKills) in TickKills)
        {
            foreach (var (attackerName, kills) in attackerKills)
            {
                if (kills.Count < 3) continue;

                var collection = new KillCollection(kills.Count)
                {
                    Player = attackerName,
                    KillCount = kills.Count,
                    Tick = tick,
                    TickDuration = 0,
                    Round = currentRound,
                    Kills = kills,
                    Map = demo.ServerInfo?.MapName ?? "unknown",
                    IsMultiKill = true,
                    Type = KillCollection.TYPE_MULTI
                };

                collection.CalculateMetrics();
                csvWriter.AddKillCollection(collection);
            }
        }

        // Process aces and quads in rounds
        foreach (var (attackerName, roundKills) in PlayerRoundKills)
        {
            foreach (var (round, kills) in roundKills)
            {
                if (kills.Count < 3) continue;

                // Skip if this is a multi-kill (already handled above)
                var killGroups = new Dictionary<int, int>();
                foreach (var kill in kills)
                {
                    if (!killGroups.TryGetValue(kill.Tick, out var count))
                        killGroups[kill.Tick] = 1;
                    else if (++killGroups[kill.Tick] >= 3)
                        goto nextRound;
                }

                var orderedKills = kills.OrderBy(k => k.Tick).ToList();
                var startTick = orderedKills[0].Tick;
                var endTick = orderedKills[orderedKills.Count - 1].Tick;

                var collection = new KillCollection(orderedKills.Count)
                {
                    Player = attackerName,
                    KillCount = orderedKills.Count,
                    Tick = startTick,
                    TickDuration = endTick - startTick,
                    Round = round,
                    Kills = orderedKills,
                    Map = demo.ServerInfo?.MapName ?? "unknown",
                    IsMultiKill = false
                };

                collection.CalculateMetrics();
                csvWriter.AddKillCollection(collection);

                nextRound: continue;
            }
        }
    }

    public static async Task ParseDemo(string demoPath, string outputPath = null)
    {
        demoPath = Path.GetFullPath(demoPath);
        if (!File.Exists(demoPath))
        {
            throw new FileNotFoundException($"Demo file not found: {demoPath}");
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            var config = Config.LoadConfig();
            var demoName = Path.GetFileNameWithoutExtension(demoPath);
            var outputDir = config.project.KillCollectionParse;
            Directory.CreateDirectory(outputDir);
            outputPath = Path.Combine(outputDir, $"{demoName}.csv");
        }
        else
        {
            outputPath = Path.GetFullPath(outputPath);
        }

        var demo = new CsDemoParser();
        using var stream = File.OpenRead(demoPath);
        var reader = DemoFileReader.Create(demo, stream);

        // Track round changes
        demo.Source1GameEvents.RoundStart += e =>
        {
            currentRound++;
            RoundTicks[currentRound] = (demo.CurrentDemoTick.Value, null);
        };

        demo.Source1GameEvents.RoundEnd += e =>
        {
            if (RoundTicks.TryGetValue(currentRound, out var roundInfo))
            {
                RoundTicks[currentRound] = (roundInfo.startTick, demo.CurrentDemoTick.Value);
            }
        };

        // Track player deaths
        demo.Source1GameEvents.PlayerDeath += e =>
        {
            var killer = demo.Players.FirstOrDefault(p => p.PlayerName == e.Attacker?.PlayerName);
            var victim = demo.Players.FirstOrDefault(p => p.PlayerName == e.Player?.PlayerName);

            if (killer != null && victim != null)
            {
                var killerState = GetPlayerState(killer, demo);
                var victimState = GetPlayerState(victim, demo);

                if (killerState != null && victimState != null)
                {
                    ProcessKillEvent(demo.CurrentDemoTick.Value, killer.PlayerName, killerState, victimState, e.Weapon, demo);
                }
            }
        };

        await reader.StartReadingAsync(default);
        while (await reader.MoveNextAsync(default)) { }

        using var csvWriter = new CsvWriter(outputPath, demo.ServerInfo?.MapName ?? "unknown");
        ProcessTickKills(csvWriter, demo);

        var tickRate = demo.ServerInfo != null ? 64 : 0;
        // Write CSV content
        csvWriter.WriteMetadata(RoundTicks, demo.CurrentDemoTick.Value, tickRate, demoPath, 0);
        csvWriter.writer.WriteLine();
        csvWriter.WriteCollections();

        // Count multi-kills (3+ kills in same tick)
        var multiKillCount = TickKills.Sum(tickKills => 
            tickKills.Value.Count(attackerKills => attackerKills.Value.Count >= 3)
        );

        // Set Windows Shell metadata
        ShellMetadata.SetKillCollectionMetadata(
            outputPath,
            demo.ServerInfo?.MapName ?? "unknown",
            csvWriter.KillCollections.Count(c => c.KillCount == 5 && !c.IsMultiKill),
            csvWriter.KillCollections.Count(c => c.KillCount == 4 && !c.IsMultiKill),
            csvWriter.KillCollections.Count(c => c.KillCount == 3 && !c.IsMultiKill),
            multiKillCount,
            demo.CurrentDemoTick.Value
        );
    }
}
