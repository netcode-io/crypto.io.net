namespace Crypto.IO
{
    public class WorkPackage
    {
        //explicit operator bool() const { return header != h256();

        public string Job;  // Job identifier can be anything. Not necessarily a hash

        public H256 Boundary;
        public H256 Header;  ///< When h256() means "pause until notified a new work package is available".
        public H256 Seed;

        public int Epoch = -1;
        public int Block = -1;

        public ulong StartNonce = 0;
        public ulong ExSizeBytes = 0;

        public string Algo = "ethash";
    }
}
