using System;
using System.Collections.Generic;
using System.Text;

namespace Crypto.IO.Miner
{
    /// <summary>
    /// Keeps track of progress for farm and miners
    /// </summary>
    public class TelemetryType
    {
        public bool Hwmon = false;
        public DateTime Start = DateTime.Now;

        public TelemetryAccountType Farm;
        public List<TelemetryAccountType> Miners;

        public override string ToString()
        {
            var b = new StringBuilder();
            /*
            Output is formatted as

            Run <h:mm> <Solutions> <Speed> [<miner> ...]
            where
            - Run h:mm    Duration of the batch
            - Solutions   Detailed solutions (A+R+F) per farm
            - Speed       Actual hashing rate

            each <miner> reports
            - speed       Actual speed at the same level of
                          magnitude for farm speed
            - sensors     Values of sensors (temp, fan, power)
            - solutions   Optional (LOG_PER_GPU) Solutions detail per GPU
            */

            var duration = DateTime.Now - Start;
            var hours = duration.Hours;
            var hoursString = hours > 9 ? hours > 99 ? $"{hours:3}" : $"{hours:2}" : $"{hours:1}";
            b.Append($"{Ansi.Green}{hoursString}:{duration.Minutes,2}{Ansi.Reset} {Ansi.WhiteBold}{Farm.Solutions}{Ansi.Reset} {Ansi.TealBold}{Farm.Hashrate.ToFormattedHashes()}{Ansi.Reset} -");

            var i = -1;                // Current miner index
            var m = Miners.Count - 1;  // Max miner index
            foreach (var miner in Miners)
            {
                i++;
                b.Append($"{(miner.Paused ? Ansi.Red : null)}{miner.Prefix}{i} {Ansi.TealBold}{miner.Hashrate.ToFormattedHashes()}{Ansi.Reset}");

                if (Hwmon)
                    b.Append($" {Ansi.Teal}{miner.Sensors}{Ansi.Reset}");

                // Eventually push also solutions per single GPU
                //    if (g_logOptions & LOG_PER_GPU)
                b.Append($" {Ansi.Teal}{miner.Solutions}{Ansi.Reset}");

                // Separator if not the last miner index
                if (i < m)
                    b.Append(", ");
            }
            return b.ToString();
        }
    }
}
