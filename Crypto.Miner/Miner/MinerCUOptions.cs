namespace CryptoPool.IO.Miner
{
    /// <summary>
    /// Holds settings for CUDA Miner
    /// </summary>
    public class MinerCUOptions : MinerOptions
    {
        public int Streams = 2;
        public int Schedule = 4;
        public int GridSize = 8192;
        public int BlockSize = 128;
    }
}
