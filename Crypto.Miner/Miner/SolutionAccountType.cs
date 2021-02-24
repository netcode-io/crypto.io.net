using System;

namespace CryptoPool.IO.Miner
{
    /// <summary>
    /// SolutionAccountType
    /// </summary>
    public class SolutionAccountType
    {
        public int Accepted = 0;
        public int Rejected = 0;
        public int Wasted = 0;
        public int Failed = 0;
        public DateTime TStamp = DateTime.Now;

        public override string ToString() =>
            $"A{Accepted}{(Wasted != 0 ? $":W{Wasted}" : null)}{(Rejected != 0 ? $":R{Rejected}" : null)}{(Failed != 0 ? $":F{Failed}" : null)}";
    }
}
