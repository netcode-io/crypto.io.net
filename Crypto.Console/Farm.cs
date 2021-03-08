using System;

namespace Crypto.IO
{
    internal class Farm : IFarm
    {
        public int Ergodicity => throw new NotImplementedException();

        public bool IsMining => throw new NotImplementedException();

        public bool Paused => throw new NotImplementedException();

        public float HashRate => throw new NotImplementedException();

        public void AccountSolution(int minerIdx, SolutionAccounting accepted)
        {
            throw new NotImplementedException();
        }

        public void Pause()
        {
            throw new NotImplementedException();
        }

        public void Resume()
        {
            throw new NotImplementedException();
        }

        public void SetWork(WorkPackage currentWp)
        {
            throw new NotImplementedException();
        }

        public void Shuffle()
        {
            throw new NotImplementedException();
        }

        public bool Start()
        {
            throw new NotImplementedException();
        }

        public void Stop()
        {
            throw new NotImplementedException();
        }
    }
}
