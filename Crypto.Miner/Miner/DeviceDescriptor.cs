namespace Crypto.IO.Miner
{
    /// <summary>
    /// DeviceDescriptor
    /// </summary>
    public class DeviceDescriptor
    {
        public DeviceType Type = DeviceType.Unknown;
        public DeviceSubscriptionType SubscriptionType = DeviceSubscriptionType.None;

        public string UniqueId;     // For GPUs this is the PCI ID
        public int TotalMemory;     // Total memory available by device
        public string Name;         // Device Name

        public bool CLDetected;     // For OpenCL detected devices
        public string CLName;
        public int CLPlatformId;
        public string CLPlatformName;
        public PlatformCLType CLPlatformType = PlatformCLType.Unknown;
        public string CLPlatformVersion;
        public int CLPlatformVersionMajor;
        public int CLPlatformVersionMinor;
        public int CLDeviceOrdinal;
        public int CLDeviceIndex;
        public string CLDeviceVersion;
        public int CLDeviceVersionMajor;
        public int CLDeviceVersionMinor;
        public string CLBoardName;
        public int CLMaxMemAlloc;
        public int CLMaxWorkGroup;
        public int CLMaxComputeUnits;
        public string CLNvCompute;
        public int CLNvComputeMajor;
        public int CLNvComputeMinor;

        public bool CUDetected;     // For CUDA detected devices
        public string CUName;
        public int CUDeviceOrdinal;
        public int CUDeviceIndex;
        public string CUCompute;
        public int CUComputeMajor;
        public int CUComputeMinor;

        public int CPCpuNumer;      // For CPU
    }
}
