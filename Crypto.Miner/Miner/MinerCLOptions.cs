namespace Crypto.IO.Miner
{
    /// <summary>
    /// Holds settings for OpenCL Miner 
    /// </summary>
    public class MinerCLOptions : MinerOptions
    {
        public bool NoBinary = false;
        public bool NoExit = false;
        public int GlobalWorkSize = 0;
        public int GlobalWorkSizeMultiplier = 65536;
        public int LocalWorkSize = 128;
    }
}
