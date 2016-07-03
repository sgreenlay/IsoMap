using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using System.Collections;
using System.Diagnostics;

namespace IsoMap
{
    public class Units
    {
        public List<IntVector2> Positions = new List<IntVector2>();
        public List<string> Names = new List<string>();
        public List<int> Healths = new List<int>();
        public List<int> MoveSpeed = new List<int>();
        public List<int> MaxHealths = new List<int>();
        // Allegiance is used to indicate validity; null means "invalid unit"
        public List<Team> Allegiance = new List<Team>();

        private Stack<Index<Units>> FreeList = new Stack<Index<Units>>();

        public void Release(Index<Units> idx)
        {
            Allegiance[idx] = null;
            FreeList.Push(idx);
        }

        public Index<Units> Allocate()
        {
            if (FreeList.Count > 0)
            {
                var idx = FreeList.Pop();
                return idx;
            }
            else
            {
                Positions.Add(new IntVector2(-1, -1));
                Names.Add("");
                Healths.Add(0);
                MaxHealths.Add(0);
                MoveSpeed.Add(0);
                Allegiance.Add(null);
                return new Index<Units>(Positions.Count - 1);
            }
        }

        public void ReleaseAll()
        {
            FreeList.Clear();
            for (var x = new Index<Units>(0); x < Count; ++x)
            {
                Allegiance[x] = null;
                FreeList.Push(x);
            }
        }

        public bool IsValid(Index<Units> idx)
        {
            return Allegiance[idx] != null;
        }

        public Index<Units> IndexOfPosition(IntVector2 pos, Index<Units> begin_idx = new Index<Units>())
        {
            for (var idx = begin_idx; idx < Positions.Count; ++idx)
            {
                if (IsValid(idx) && Positions[idx] == pos)
                    return idx;
            }
            return new Index<Units>(-1);
        }

        public int Count { get { return Positions.Count; } }
    }
}
