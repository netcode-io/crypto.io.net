using System;

namespace Crypto.IO
{
    /// <summary>
    /// Ethash
    /// </summary>
    public static class Ethash
    {
        public static H256 Hash256FromBytes(byte[] data)
        {
            throw new NotImplementedException();
        }

        public static EpochContext GetGlobalEpochContext(int epoch)
        {
            throw new NotImplementedException();
        }

        internal static object Hash(EpochContext context, H256 hash, ulong nonce)
        {
            throw new NotImplementedException();
        }

        public static int FindEpochNumber(H256 h256)
        {
            throw new NotImplementedException();
        }
    }
}
