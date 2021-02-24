using System;

namespace CryptoPool.IO.Hwmon
{
    public interface IHwmon : IDisposable
    {
        int GpuCount { get; }
        string GpuName(int index);
        //string GetGpuPciId(int index);
        uint? GetTempC(int index);
        uint? GetFanpcnt(int index);
        uint? GetPowerUsage(int index);
    }
}
