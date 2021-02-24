using System.Diagnostics;
using System.Threading;

namespace System
{
    public abstract class Worker
    {
        string _name;
        //public object _workLock = new object();    // Lock for the network existence.
        Thread _work;       // The network thread.
        int _state = (int)WorkerState.Starting; //: atomic

        public Worker(string name) => _name = name;

        /// Starts worker thread; causes startedWorking() to be called.
        public void StartWorking()
        {
            Debug.WriteLine("Worker::startWorking() begin");
            if (_work != null)
            {
                if (Interlocked.CompareExchange(ref _state, (int)WorkerState.Started, (int)WorkerState.Stopped) != (int)WorkerState.Stopped)
                {
                    throw new NotImplementedException();
                }
            }
            while (_state == (int)WorkerState.Starting)
                Thread.Sleep(20);
            Debug.WriteLine("Worker::startWorking() begin");
        }

        /// Triggers worker thread it should stop
        public void TriggerStopWorking()
        {
            throw new NotImplementedException();
        }

        /// Stop worker thread; causes call to stopWorking() and waits till thread has stopped.
        public void StopWorking()
        {
            throw new NotImplementedException();
        }

        /// Whether or not this worker should stop
        public bool ShouldStop => _state != (int)WorkerState.Started;

        public abstract void WorkLoop();
    }
}