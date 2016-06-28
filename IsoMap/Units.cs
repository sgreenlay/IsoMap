using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;

namespace IsoMap
{
    public class Units
    {
        public List<IntVector2> Positions = new List<IntVector2>();
        public List<string> Names = new List<string>();
        public List<int> Healths = new List<int>();
        public List<int> MaxHealths = new List<int>();

        internal bool Contains(IntVector2 pos)
        {
            return Positions.Contains(pos);
        }
        internal int IndexOf(IntVector2 pos)
        {
            return Positions.IndexOf(pos);
        }

        internal void Add(IntVector2 pos)
        {
            Positions.Add(pos);
            Names.Add(NameGenerator.randName());
            Healths.Add(3);
            MaxHealths.Add(3);
        }

        internal void Remove(IntVector2 selectedTile)
        {
            var idx = Positions.IndexOf(selectedTile);
            Remove(idx);
        }
        internal CanvasBitmap Bitmap { get; set; }
        internal int offset;

        internal int Count { get { return Positions.Count; } }

        internal void Move(IntVector2 source, IntVector2 destination)
        {
            var idx = Positions.IndexOf(source);
            if (idx == -1)
                throw new ArgumentException();
            Positions[idx] = destination;
        }

        internal void Damage(int idx, int v)
        {
            Healths[idx] -= 1;
            if (Healths[idx] == 0)
                Remove(idx);
        }
        private void Remove(int idx)
        {
            Positions.RemoveAt(idx);
            Names.RemoveAt(idx);
            Healths.RemoveAt(idx);
            MaxHealths.RemoveAt(idx);
        }
    }
}
