using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Crypto.IO
{
    public class EthStratumClientTest
    {
        TestOutputConverter _output;

        public EthStratumClientTest(ITestOutputHelper output) => Console.SetOut(_output = new TestOutputConverter(output));

        //[Fact]
        //public void Create()
        //{
        //    using var _ = _output.Session();
        //    var client = new EthStratumClient(new Farm { }, 0, 0);
        //}

        [Theory]
        //[InlineData("stratum+tcp://ss.xantpool.com:3333", "bad_resolve")]
        //[InlineData("stratum+tcp://ss.antpool.com:1234", "bad_connect")]
        [InlineData("stratum+tcp://user@ss.antpool.com:3333", "good")] //: 3333,443,25
        [InlineData("stratum+tcp://eth-eu1.nanopool.org:9999", "good")] //: 3333,443,25
        [InlineData("stratum+tcp://zec-eu1.nanopool.org:6666", "good")] //: 3333,443,25
        [InlineData("stratum2+tcp://us.ubiqpool.io:8008", "good")] //: 3333,443,25
        public async Task ConnectAsync(string url, string expect)
        {
            using var _ = _output.Session();
            var client = new EthStratumClient(new Farm { }, 0, 0);
            client.SetConnection(new Uri(url));
            await client.ConnectAsync();
            switch (expect)
            {
                case "bad_resolve":
                case "bad_connect":
                    Assert.False(client.IsConnected);
                    break;
                case "good":
                    Assert.True(client.IsConnected);
                    while (client.IsConnected)
                        Thread.Sleep(100);
                    break;
            }
        }
    }
}
