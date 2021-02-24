namespace CryptoPool.IO.Miner
{
    /// <summary>
    /// HwMonitorInfo
    /// </summary>
    public class HwMonitorInfo
    {
        public HwMonitorInfoType DeviceType = HwMonitorInfoType.Unknown;
        public string DevicePciId;
        public int DeviceIndex = -1;
    }
}
