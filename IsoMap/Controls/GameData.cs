using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IsoMap
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

        private IntVector2 randUnitSpawn()
        {
            while (true)
            {
                var tpos = new IntVector2(Rand.Next(TerrainSize.Size.X), Rand.Next(TerrainSize.Size.Y));

                if (Terrain[TerrainSize.XYToIndex(tpos)] != TerrainType.Empty)
                    continue;

                if (units.IndexOfPosition(tpos) != -1)
                    continue;

                return tpos;
            }
        }

        private void InitPlayerCharacter(Index<Units> idx)
        {
            units.Positions[idx] = randUnitSpawn();
            units.Names[idx] = NameGenerator.randName();
            units.MoveSpeed[idx] = 3;
            units.Healths[idx] = 3;
            units.MaxHealths[idx] = 3;
            units.Allegiance[idx] = TeamA;
        }
        private void InitEnemyCharacter(Index<Units> idx)
        {
            units.Positions[idx] = randUnitSpawn();
            units.Names[idx] = NameGenerator.randName();
            units.MoveSpeed[idx] = 4;
            units.Healths[idx] = 3;
            units.MaxHealths[idx] = 3;
            units.Allegiance[idx] = TeamB;
        }
        private void RandomizeTerrain()
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

        public GameData()
        {
            RandomizeTerrain();

            var maxX = TerrainSize.Size.X;
            var maxY = TerrainSize.Size.Y;

            for (var i = 0; i < 4; ++i)
            {
                var idx = units.Allocate();
                InitPlayerCharacter(idx);
                TeamA.units.Add(idx);

                var idx2 = units.Allocate();
                InitEnemyCharacter(idx2);
                TeamB.units.Add(idx2);
            }

            Movable = new BitArray(TerrainSize.Area());
            ActiveTeam = Team.TeamID.TeamA;
            ActivePhase = Phase.Move;
        }

        private BitArray Movable;

        void AITurn()
        {
            Debug.Assert(ActivePhase == Phase.Move);

            if (CurrentTeam().Count == 0)
                return;

            var team_idx = new Index<Team>(Rand.Next(CurrentTeam().Count));
            var idx = CurrentTeam()[team_idx];
            var pathfinder = new PathFinder(TerrainSize);


            pathfindMove(idx, pathfinder);

            pathfinder.CopyPathDataOut(Movable);

            for (var i = new Index<Team>(0); i < EnemyTeam().Count; ++i)
            {
                var enemy_unit_idx = EnemyTeam()[i];
                var epos = units.Positions[enemy_unit_idx];
                var eidx = TerrainSize.XYToIndex(epos);
                if (Movable.Get(eidx))
                {
                    units.Release(enemy_unit_idx);
                    EnemyTeam().RemoveAt(i);
                    units.Positions[idx] = epos;
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
            units.Positions[idx] = tgt_xy;
        }

        private int[] FindWalkable(Index<Units> unit_idx)
        {
            int[] ret = new int[TerrainSize.Area()];
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

            var team = units.Allegiance[unit_idx];

            for (var x = new Index<Units>(); x < units.Count; ++x)
            {
                if (!units.IsValid(x))
                    continue;
                var idx = TerrainSize.XYToIndex(units.Positions[x]);
                if (units.Allegiance[x] == team)
                    ret[idx] = PathFinder.WALK_TRAIT_BLOCKED;
                else
                    ret[idx] = PathFinder.WALK_TRAIT_ONTO;
            }

            return ret;
        }

        internal void pathfindMove(Index<Units> unit_idx, PathFinder pathfinder)
        {
            var Walkable = FindWalkable(unit_idx);

            var tpos = units.Positions[unit_idx];
            var dist = units.MoveSpeed[unit_idx];

            pathfinder.FindAllPaths(tpos + new IntVector2(1, 0), Walkable, dist);
            pathfinder.FindAllPaths(tpos + new IntVector2(-1, 0), Walkable, dist);
            pathfinder.FindAllPaths(tpos + new IntVector2(0, 1), Walkable, dist);
            pathfinder.FindAllPaths(tpos + new IntVector2(0, -1), Walkable, dist);
        }

        private bool canShootThroughTile(Team friendly, int x, int y, BitArray bitarray)
        {
            var idx = TerrainSize.XYToIndex(x, y);
            var ttype = Terrain[idx];
            if (ttype == GameData.TerrainType.Solid)
                return false;
            var other = units.IndexOfPosition(new IntVector2(x, y));
            if (other != -1 && units.Allegiance[other] == friendly)
                return false;
            bitarray.Set(idx, true);
            if (other != -1 && units.Allegiance[other] != friendly)
                return false;
            if (ttype == GameData.TerrainType.Soft)
                return false;
            return true;
        }
        internal void pathfindShoot(Index<Units> unit_idx, BitArray bitarray)
        {
            var tpos = units.Positions[unit_idx];
            var friendly = CurrentTeam();
            for (var x = tpos.X + 1; x < TerrainSize.Size.X; ++x)
            {
                if (!canShootThroughTile(friendly, x, tpos.Y, bitarray))
                    break;
            }
            for (var x = tpos.X - 1; x >= 0; --x)
            {
                if (!canShootThroughTile(friendly, x, tpos.Y, bitarray))
                    break;
            }
            for (var y = tpos.Y + 1; y < TerrainSize.Size.Y; ++y)
            {
                if (!canShootThroughTile(friendly, tpos.X, y, bitarray))
                    break;
            }
            for (var y = tpos.Y - 1; y >= 0; --y)
            {
                if (!canShootThroughTile(friendly, tpos.X, y, bitarray))
                    break;
            }
        }

        public void PlayerShoot(IntVector2 tgt)
        {
            var idx = units.IndexOfPosition(tgt);
            if (idx != -1 && units.Allegiance[idx] == EnemyTeam())
            {
                Damage(idx, 1);
            }
            EndPlayerTurn();
        }

        public void EndPlayerTurn()
        {
            ActivePhase = Phase.Move;
            ActiveTeam = Team.TeamID.TeamB;
            AITurn();
            ActiveTeam = Team.TeamID.TeamA;
        }

        public enum TerrainType
        {
            Empty,
            Solid,
            Transparent,
            Soft
        };

        public enum Phase
        {
            Move,
            Shoot
        };

        private void Damage(Index<Units> idx, int v)
        {
            units.Healths[idx] -= 1;
            if (units.Healths[idx] == 0)
            {
                Kill(idx);
            }
        }
        public void Move(Index<Units> idx, IntVector2 tgt)
        {
            units.Positions[idx] = tgt;
        }

        public void Kill(Index<Units> idx)
        {
            units.Allegiance[idx].Remove(idx);
            units.Release(idx);
        }

        public Team CurrentTeam()
        {
            if (ActiveTeam == Team.TeamID.TeamA) return TeamA;
            else return TeamB;
        }
        public Team EnemyTeam()
        {
            if (ActiveTeam == Team.TeamID.TeamA) return TeamB;
            else return TeamA;
        }

        public Phase ActivePhase;

        public Dimensions TerrainSize = new Dimensions(new IntVector2(9, 7));
        public List<TerrainType> Terrain = new List<TerrainType>();

        public Team TeamA = new Team(Team.TeamID.TeamA);
        public Team TeamB = new Team(Team.TeamID.TeamB);

        private Team.TeamID ActiveTeam = Team.TeamID.TeamA;
        public Units units = new Units();
    }
}
