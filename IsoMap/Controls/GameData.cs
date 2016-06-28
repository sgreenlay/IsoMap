using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IsoMap.Controls
{
    public struct Dimensions
    {
        public Dimensions(IntVector2 v)
        {
            Size = v;
        }

        public int XYToIndex(IntVector2 v)
        {
            return XYToIndex(v.X, v.Y);
        }
        public int XYToIndex(int x, int y)
        {
            Debug.Assert(ValidXY(x, y));
            return x + y * Size.X;
        }

        public IntVector2 IndexToXY(int idx)
        {
            return new IntVector2(idx % Size.X, idx / Size.X);
        }
        public bool ValidXY(IntVector2 v) { return ValidXY(v.X, v.Y); }
        public bool ValidXY(int x, int y) { return (x >= 0 && x < Size.X) && (y >= 0 && y < Size.Y); }

        public int Area()
        {
            return Size.X * Size.Y;
        }

        public IntVector2 Size;
    }

    class PathFinder
    {
        public PathFinder(Dimensions sz)
        {
            dims = sz;
            dist = new int[sz.Area()];
        }

        private Dimensions dims;
        private int[] dist;

        public void Clear()
        {
            Array.Clear(dist, 0, dist.Length);
        }

        public const int WALK_TRAIT_BLOCKED = 0;
        public const int WALK_TRAIT_ONTO = 1;
        public const int WALK_TRAIT_THROUGH = 2;
        public const int WALK_TRAIT_EMPTY = WALK_TRAIT_ONTO | WALK_TRAIT_THROUGH;

        public void FindAllPaths(IntVector2 src, int[] Walkable, int stepsToRecurse)
        {
            Debug.Assert(stepsToRecurse > 0);
            if (!dims.ValidXY(src))
                return;

            var idx = dims.XYToIndex(src);

            if (dist[idx] >= stepsToRecurse)
                return;

            if (0 == (Walkable[idx] & WALK_TRAIT_ONTO))
                return;

            dist[idx] = stepsToRecurse;

            if (stepsToRecurse == 1)
                return;

            if (0 == (Walkable[idx] & WALK_TRAIT_THROUGH))
                return;

            FindAllPaths(src + new IntVector2(1, 0), Walkable, stepsToRecurse - 1);
            FindAllPaths(src + new IntVector2(-1, 0), Walkable, stepsToRecurse - 1);
            FindAllPaths(src + new IntVector2(0, 1), Walkable, stepsToRecurse - 1);
            FindAllPaths(src + new IntVector2(0, -1), Walkable, stepsToRecurse - 1);
        }

        public void CopyPathDataOut(BitArray Movable)
        {
            for (int i = 0; i < dims.Area(); ++i)
            {
                Movable[i] = dist[i] > 0;
            }
        }

    }
    public class GameData
    {
        private static Random Rand = new Random();

        public GameData()
        {
            Randomize();

            var maxX = TerrainSize.Size.X;
            var maxY = TerrainSize.Size.Y;

            while (TeamA.Count < 4)
            {
                var tpos = new IntVector2(Rand.Next(maxX), Rand.Next(maxY));
                if (Terrain[TerrainSize.XYToIndex(tpos)] != TerrainType.Empty)
                    continue;

                if (TeamA.Contains(tpos))
                    continue;

                TeamA.Add(tpos);
            }

            while (TeamB.Count < 4)
            {
                var tpos = new IntVector2(Rand.Next(maxX), Rand.Next(maxY));
                if (Terrain[TerrainSize.XYToIndex(tpos)] != TerrainType.Empty)
                    continue;

                if (TeamA.Contains(tpos))
                    continue;
                if (TeamB.Contains(tpos))
                    continue;
                TeamB.Add(tpos);
            }

            ActiveTeam = Team.TeamA;
            ActivePhase = Phase.Move;
        }

        public void Randomize()
        {
            for (var y = 0; y < TerrainSize.Size.Y; ++y)
            {
                for (var x = 0; x < TerrainSize.Size.X; ++x)
                {
                    var r = Rand.NextDouble();
                    if (r < 0.85)
                        Terrain.Add(TerrainType.Empty);
                    else if (r < 0.90)
                        Terrain.Add(TerrainType.Soft);
                    else if (r < 0.95)
                        Terrain.Add(TerrainType.Solid);
                    else
                        Terrain.Add(TerrainType.Transparent);
                }
            }
        }

        void AITurn()
        {
            Debug.Assert(ActivePhase == Phase.Move);

            if (CurrentTeam().Count == 0)
                return;

            var idx = Rand.Next(CurrentTeam().Count);
            var pathfinder = new PathFinder(TerrainSize);
            var walkable = FindWalkable(CurrentTeam(), EnemyTeam());
            var tpos = CurrentTeam().Positions[idx];
            pathfinder.FindAllPaths(tpos + new IntVector2(1, 0), walkable, 4);
            pathfinder.FindAllPaths(tpos + new IntVector2(-1, 0), walkable, 4);
            pathfinder.FindAllPaths(tpos + new IntVector2(0, 1), walkable, 4);
            pathfinder.FindAllPaths(tpos + new IntVector2(0, -1), walkable, 4);
            BitArray Movable = new BitArray(TerrainSize.Area());
            pathfinder.CopyPathDataOut(Movable);

            for (var i = 0; i < EnemyTeam().Count; ++i)
            {
                var epos = EnemyTeam().Positions[i];
                var eidx = TerrainSize.XYToIndex(epos);
                if (Movable.Get(eidx))
                {
                    EnemyTeam().Remove(epos);
                    CurrentTeam().Move(tpos, epos);
                    return;
                }
            }

            // No enemies in range. Move randomly. Accumulate a list of all indexes.
            List<int> indexes = new List<int>();
            for (var i = 0; i < Movable.Length; ++i)
            {
                if (Movable[i])
                    indexes.Add(i);
            }
            if (indexes.Count == 0)
            {
                // no valid squares for movement
                return;
            }
            // Select randomly from the list
            var tgt_idx = indexes[Rand.Next(indexes.Count)];
            var tgt_xy = TerrainSize.IndexToXY(tgt_idx);
            CurrentTeam().Move(CurrentTeam().Positions[idx], tgt_xy);
        }

        public int[] FindWalkable(Units FriendTeam, Units FoeTeam)
        {
            var dims = TerrainSize;
            int[] ret = new int[dims.Area()];
            for (int x = 0; x < ret.Length; ++x)
            {
                var ttype = Terrain[x];

                if (ttype == TerrainType.Solid || ttype == TerrainType.Transparent)
                {
                    ret[x] = PathFinder.WALK_TRAIT_BLOCKED;
                }
                else
                {
                    ret[x] = PathFinder.WALK_TRAIT_EMPTY;
                }
            }

            for (int x = 0; x < FriendTeam.Count; ++x)
            {
                var idx = dims.XYToIndex(FriendTeam.Positions[x]);
                ret[idx] = PathFinder.WALK_TRAIT_BLOCKED;
            }
            for (int x = 0; x < FoeTeam.Count; ++x)
            {
                var idx = dims.XYToIndex(FoeTeam.Positions[x]);
                ret[idx] = PathFinder.WALK_TRAIT_ONTO;
            }

            return ret;
        }

        public void PlayerShoot(IntVector2 tgt)
        {
            var idx = EnemyTeam().IndexOf(tgt);
            if (idx != -1)
            {
                EnemyTeam().Damage(idx, 1);
            }
            EndPlayerTurn();
        }

        public void EndPlayerTurn()
        {
            ActivePhase = Phase.Move;
            ActiveTeam = Team.TeamB;
            AITurn();
            ActiveTeam = Team.TeamA;
        }

        public enum TerrainType
        {
            Empty,
            Solid,
            Transparent,
            Soft
        };

        public enum Team
        {
            TeamA,
            TeamB
        };

        public enum Phase
        {
            Move,
            Shoot
        };

        public Units CurrentTeam()
        {
            if (ActiveTeam == Team.TeamA) return TeamA;
            else return TeamB;
        }
        public Units EnemyTeam()
        {
            if (ActiveTeam == Team.TeamA) return TeamB;
            else return TeamA;
        }

        public Phase ActivePhase;

        public Dimensions TerrainSize = new Dimensions(new IntVector2(9, 7));
        public List<TerrainType> Terrain = new List<TerrainType>();

        public Units TeamA = new Units();
        public Units TeamB = new Units();

        public Team ActiveTeam = Team.TeamA;
    }
}
