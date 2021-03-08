using System;

namespace Crypto.IO
{
    public class WorkPackage
    {
        public string Job;  // Job identifier can be anything. Not necessarily a hash

        public H256 Boundary;
        public H256 Header;  //< When H256() means "pause until notified a new work package is available".
        public H256 Seed;

        public int Epoch = -1;
        public int Block = -1;

        public ulong StartNonce = 0;
        public int ExSizeBytes = 0;

        public string Algo = "ethash";
    }
}
