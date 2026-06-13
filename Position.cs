
namespace LouveSystems.K2.Lib
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    public struct Position : IBinarySerializableWithVersion
    {
        public int x;
        public int y;

        public Position(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public override bool Equals(object obj)
        {
            return obj is Position position &&
                   x == position.x &&
                   y == position.y;
        }

        public int GetHash()
        {
            return Extensions.Hash(x, y);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(x, y);
        }

        public int DistanceWith(in Position other)
        {
            return SquaredDistanceWith(in other).IntegerSqrt();
        }

        public int SquaredDistanceWith(in Position other)
        {
            return (int)Math.Pow(x - other.x, 2) + (int)Math.Pow(y - other.y, 2);
        }

        public override string ToString()
        {
            return $"({x}; {y})";
        }

        public void Write(BinaryWriter into)
        {
            into.Write(x);
            into.Write(y);
        }

        public void Read(byte version, BinaryReader from)
        {
            x = from.ReadInt32();
            y = from.ReadInt32();
        }

        public static Position operator -(Position a, Position b)
        {
            return new Position(a.x - b.x, a.y - b.y);
        }

        public static Position operator+ (Position a, Position b)
        {
            return new Position(a.x + b.x, a.y + b.y);
        }

        public static Position operator *(Position a, int integer)
        {
            return new Position(a.x * integer, a.y * integer);
        }

        public static Position operator /(Position a, int integer)
        {
            return new Position(a.x / integer, a.y / integer);
        }

        public static Position operator /(Position a, Position b)
        {
            return new Position(a.x / b.x, a.y / b.y);
        }
        public static bool operator ==(Position a, Position b)
        {
            return a.x == b.x && a.y == b.y;
        }

        public static bool operator !=(Position a, Position b)
        {
            return a.x != b.x || a.y != b.y;
        }
    }
}