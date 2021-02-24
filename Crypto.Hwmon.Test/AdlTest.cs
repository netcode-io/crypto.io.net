using Xunit;

namespace Crypto.IO.Hwmon
{
    public class AdlTest
    {
        [Fact]
        public void NewAndDispose()
        {
            using var adl = new Adl();
        }

        [Fact]
        public void GpuName()
        {
            using var adl = new Adl();
            Assert.NotEmpty(adl.GpuName(0));
        }

        [Fact]
        public void GetPciId()
        {
            using var adl = new Adl();
            Assert.NotEmpty(adl.GetPciId(0));
        }

        [Fact]
        public void GetTempC()
        {
            using var adl = new Adl();
            Assert.NotNull(adl.GetTempC(0));
        }

        [Fact]
        public void GetFanpcnt()
        {
            using var adl = new Adl();
            Assert.NotNull(adl.GetFanpcnt(0));
        }

        [Fact]
        public void GetPowerUsage()
        {
            using var adl = new Adl();
            Assert.NotNull(adl.GetPowerUsage(0));
        }
    }
}
