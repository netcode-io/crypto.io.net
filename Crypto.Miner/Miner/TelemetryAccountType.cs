namespace CryptoPool.IO.Miner
{
    /// <summary>
    /// TelemetryAccountType
    /// </summary>
    public class TelemetryAccountType
    {
        public string Prefix = string.Empty;
        public float Hashrate = 0.0f;
        public bool Paused = false;
        public HwSensorsType Sensors;
        public SolutionAccountType Solutions;
    }
}
