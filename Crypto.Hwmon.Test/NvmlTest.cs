using Xunit;

namespace CryptoPool.IO.Hwmon
{
    public class NvmlTest
    {
        [Fact]
        public void NewAndDispose()
        {
            using var nvml = new Nvml();
        }

        [Fact]
        public void GpuName()
        {
            using var nvml = new Nvml();
            Assert.NotEmpty(nvml.GpuName(0));
        }

        [Fact]
        public void GetTempC()
        {
            using var nvml = new Nvml();
            Assert.NotNull(nvml.GetTempC(0));
        }

        [Fact]
        public void GetFanpcnt()
        {
            using var nvml = new Nvml();
            Assert.NotNull(nvml.GetFanpcnt(0));
        }

        [Fact]
        public void GetPowerUsage()
        {
            using var nvml = new Nvml();
            Assert.NotNull(nvml.GetPowerUsage(0));
        }
    }
}
