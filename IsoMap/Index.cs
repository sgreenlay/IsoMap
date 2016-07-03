using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IsoMap
{
    public struct Index<T>
    {
        public static readonly Index<T> Invalid = new Index<T>(-1);

        public Index(int v = 0) { value = v; }
        public int value;

        public static Index<T> operator ++(Index<T> i) { return new Index<T>(i.value + 1); }

        public override bool Equals(object obj)
        {
            if (!(obj is Index<T>))
                return false;
            return ((Index<T>)obj).value == value;
        }
        public override int GetHashCode()
        {
            return value.GetHashCode();
        }
        public static bool operator ==(Index<T> i, Index<T> j) { return i.value == j.value; }
        public static bool operator !=(Index<T> i, Index<T> j) { return i.value != j.value; }
        public static implicit operator int(Index<T> i)
        {
            return i.value;
        }
    }
}
