using System;

namespace Crypto.IO.Hwmon
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
