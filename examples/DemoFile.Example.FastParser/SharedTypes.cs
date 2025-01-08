using System;
using System.Collections.Generic;

namespace SharedTypes
{
    public class PlayerState
    {
        public string Name { get; set; }
        public string Team { get; set; }
        public string SteamID { get; set; }
        public Position Position { get; set; }
        public ViewAngles ViewAngles { get; set; }
        public string ActiveWeapon { get; set; }
        public int Tick { get; set; }
    }

    public readonly struct Position
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Position(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float DistanceTo(Position other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static Position Zero => new Position(0, 0, 0);
    }

    public readonly struct ViewAngles
    {
        public readonly float Pitch;
        public readonly float Yaw;

        public ViewAngles(float pitch, float yaw)
        {
            Pitch = pitch;
            Yaw = yaw;
        }

        public static ViewAngles Zero => new ViewAngles(0, 0);
    }

    public class KillEvent
    {
        public PlayerState Attacker { get; set; }
        public PlayerState Victim { get; set; }
        public int Tick { get; set; }
        public string Weapon { get; set; }
        public float DistanceToEnemy { get; set; }
        public int TicksSinceLastKill { get; set; }
        public float DistanceMovedSinceLastKill { get; set; }

        public void CalculateDistances(KillEvent previousKill = null)
        {
            DistanceToEnemy = Attacker.Position.DistanceTo(Victim.Position);
            
            if (previousKill != null)
            {
                TicksSinceLastKill = Tick - previousKill.Tick;
                DistanceMovedSinceLastKill = Attacker.Position.DistanceTo(previousKill.Attacker.Position);
            }
        }
    }

    public class KillCollection
    {
        public const string TYPE_ACE = "Ace";
        public const string TYPE_QUAD = "Quad";
        public const string TYPE_TRIPLE = "Triple";
        public const string TYPE_MULTI = "Multi";

        public string Player { get; set; }
        public int KillCount { get; set; }
        public int Tick { get; set; }
        public int TickDuration { get; set; }
        public int Round { get; set; }
        public int RoundStartTick { get; set; }
        public List<KillEvent> Kills { get; set; }
        public string Map { get; set; }
        public bool IsMultiKill { get; set; }
        public float TotalDistanceMoved { get; set; }
        public float KillerRadius { get; set; }
        public float VictimsRadius { get; set; }
        public string Type { get; set; }

        public KillCollection(int estimatedKills = 5)
        {
            Kills = new List<KillEvent>(estimatedKills);
        }

        public void CalculateMetrics()
        {
            if (Kills.Count == 0) return;

            // Calculate distances for each kill
            for (int i = 0; i < Kills.Count; i++)
            {
                Kills[i].CalculateDistances(i > 0 ? Kills[i - 1] : null);
            }

            // Calculate total distance moved
            TotalDistanceMoved = 0;
            for (int i = 1; i < Kills.Count; i++)
            {
                TotalDistanceMoved += Kills[i].Attacker.Position.DistanceTo(Kills[i - 1].Attacker.Position);
            }

            // Calculate radii
            var killerPositions = new List<Position>(Kills.Count);
            var victimPositions = new List<Position>(Kills.Count);

            foreach (var kill in Kills)
            {
                killerPositions.Add(kill.Attacker.Position);
                victimPositions.Add(kill.Victim.Position);
            }

            KillerRadius = CalculateRadius(killerPositions);
            VictimsRadius = CalculateRadius(victimPositions);

            // Set type based on kill count and timing
            if (IsMultiKill)
            {
                Type = TYPE_MULTI;
            }
            else
            {
                Type = KillCount switch
                {
                    5 => TYPE_ACE,
                    4 => TYPE_QUAD,
                    3 => TYPE_TRIPLE,
                    _ => TYPE_MULTI
                };
            }
        }

        private static float CalculateRadius(List<Position> positions)
        {
            if (positions.Count <= 1) return 0;

            // Calculate centroid
            float centerX = 0, centerY = 0, centerZ = 0;
            foreach (var pos in positions)
            {
                centerX += pos.X;
                centerY += pos.Y;
                centerZ += pos.Z;
            }
            var center = new Position(
                centerX / positions.Count,
                centerY / positions.Count,
                centerZ / positions.Count
            );

            // Find maximum distance from center
            float maxRadius = 0;
            foreach (var pos in positions)
            {
                var distance = pos.DistanceTo(center);
                if (distance > maxRadius)
                    maxRadius = distance;
            }
            return maxRadius;
        }
    }
}
