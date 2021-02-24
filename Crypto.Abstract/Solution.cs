using System;

namespace Crypto.IO
{
    /// <summary>
    /// Solution
    /// </summary>
    public class Solution
    {
        public ulong Nonce;             // Solution found nonce
        public H256 MixHash;            // Mix hash
        public WorkPackage Work;        // WorkPackage this solution refers to
        public DateTime TStamp;         // Timestamp of found solution
        public int MIdx;                // Originating miner Id
    }
}
