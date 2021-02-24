using CryptoPool.IO.Ethash;

namespace CryptoPool.IO.Miner
{
    /// <summary>
    /// Hosting one or more Miners.
    /// Must be implemented in a threadsafe manner since it will be called from multiple miner threads.
    /// </summary>
    public abstract class FarmFace
    {
        static FarmFace F;

        public FarmFace() => F = this;

        public virtual int TStart { get; } = 0;
        public virtual int TStop { get; } = 0;
        public virtual int Ergodicity { get; } = 0;

        /// <summary>
        /// Called from a Miner to note a WorkPackage has a solution.
        /// </summary>
        /// <param name="p">The solution.</param>
        public abstract void SubmitProof(Solution p);
        public abstract void AccountSolution(int minerIdx, SolutionAccounting accounting);
        public virtual ulong NonceScrambler { get; } = 0L;
        public virtual int SegmentWidth { get; } = 0;
    }
}
