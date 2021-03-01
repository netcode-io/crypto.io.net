namespace Crypto.IO
{
    /// <summary>
    /// IFarm
    /// </summary>
    public interface IFarm
    {
        int Ergodicity { get; }
        bool IsMining { get; }
        bool Paused { get; }
        float HashRate { get; }

        void Shuffle();
        bool Start();
        void Resume();
        void Stop();
        void Pause();
        void SetWork(WorkPackage currentWp);
        void AccountSolution(int minerIdx, SolutionAccounting accepted);
    }
}
