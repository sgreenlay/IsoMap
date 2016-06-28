using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IsoMap
{
    public struct IntVector2
    {
        public IntVector2(int x, int y) { X = x; Y = y; }
        public int X, Y;

        public static IntVector2 operator +(IntVector2 a, IntVector2 b)
        {
            return new IntVector2(a.X + b.X, a.Y + b.Y);
        }

        public static bool operator ==(IntVector2 a, IntVector2 b)
        {
            return a.X == b.X && a.Y == b.Y;
        }
        public static bool operator !=(IntVector2 a, IntVector2 b)
        {
            return !(a == b);
        }
        public override bool Equals(object obj)
        {
            return (IntVector2)obj == this;
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        //public static bool operator <(IntVector2 a, IntVector2 b)
        //{
        //    return a.X < b.X && a.Y < b.Y;
        //}
        //public static bool operator >(IntVector2 a, IntVector2 b)
        //{
        //    return b < a;
        //}

        //public static bool operator <=(IntVector2 a, IntVector2 b)
        //{
        //    return a < b || a == b;
        //}
        //public static bool operator >=(IntVector2 a, IntVector2 b)
        //{
        //    return b <= a;
        //}
    }
}
