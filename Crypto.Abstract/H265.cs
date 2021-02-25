using System;

namespace Crypto.IO
{
    /// <summary>
    /// H256
    /// </summary>
    public struct H256
    {
        public static readonly H256 Empty = new H256();

        public H256(string id) { }
        public H256(byte[] id) { }

        public byte[] Data => null;
        public string Abridged => "";

        public override bool Equals(object obj)
        {
            return base.Equals(obj);
        }

        public static bool operator ==(H256 lhs, H256 rhs) => Equals(lhs, rhs);

        public static bool operator !=(H256 lhs, H256 rhs) => !Equals(lhs, rhs);

        public static bool operator <(H256 b, H256 c)
        {
            return false;
        }
        public static bool operator <=(H256 b, H256 c)
        {
            return false;
        }
        public static bool operator >(H256 b, H256 c)
        {
            return false;
        }
        public static bool operator >=(H256 b, H256 c)
        {
            return false;
        }

        public static H256 Random()
        {
            throw new NotImplementedException();
        }

        //public string Hex(bool add = false)
        //{
        //    throw new NotImplementedException();
        //}
    }
}
