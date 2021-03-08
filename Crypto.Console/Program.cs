using System;
using System.Threading;
using System.Threading.Tasks;

namespace Crypto.IO
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var client = new EthStratumClient(new Farm { }, 0, 0);
            //Test(client, "stratum+tcp://user@ss.antpool.com:3333").Wait();
            //Test(client, "stratum+tcp://eth-eu1.nanopool.org:9999").Wait();
            //Test(client, "stratum+tcp://zec-eu1.nanopool.org:6666").Wait();
            Test(client, "stratum2+tcp://us.ubiqpool.io:8008").Wait();
        }

        static async Task Test(PoolClient client, string url)
        {
            client.SetConnection(new Uri(url));
            await client.ConnectAsync();
            while (client.IsConnected)
                Thread.Sleep(1000);

        }
    }
}
