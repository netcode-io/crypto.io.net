using System;
using System.Threading;
using System.Threading.Tasks;

namespace Crypto.IO
{
    public class SimulateClient : PoolClient
    {
        int _block;
        DateTime _startTime;

        float _hrAlpha = 0.45f;
        float _hrMax = 0.0f;
        float _hrMean = 0.0f;
        IFarm _f;

        public SimulateClient(IFarm f, int block) : base("sim")
        {
            _f = f;
            _block = block;
        }

        public override Task ConnectAsync()
        {
            // Initialize new session
            _connected = true; //: atomic
            _session = new Session
            {
                Subscribed = true, //: atomic
                Authorized = true,  //: atomic
            };
            _onConnected?.Invoke(_f, this);
            // No need to worry about starting again.
            // Worker class prevents that
            StartWorking();
            return Task.CompletedTask;
        }

        public override Task DisconnectAsync()
        {
            Console.WriteLine($"Simulation results : {Ansi.WhiteBold}Max {_hrMax.ToFormattedHashes(6)} Mean {_hrMean.ToFormattedHashes(6)}{Ansi.Reset}");
            _conn.AddDuration(_session.Duration);
            _session = null;
            _connected = false; //: atomic
            _onDisconnected?.Invoke(_f, this);
            return Task.CompletedTask;
        }

        public override bool IsPendingState => false;

        public override string ActiveEndPoint => string.Empty;

        public override void SubmitHashrate(ulong rate, string id) { }

        public override void SubmitSolution(Solution solution)
        {
            // This is a fake submission only evaluated locally
            var submitStart = DateTime.Now;
            var accepted = EthashAux.Eval(solution.Work.Epoch, solution.Work.Header, solution.Nonce).value <= solution.Work.Boundary;
            var responseDelayMs = (DateTime.Now - submitStart).TotalMilliseconds;
            if (accepted)
                _onSolutionAccepted?.Invoke(_f, this, responseDelayMs, solution.MIdx, false);
            else
                _onSolutionRejected?.Invoke(_f, this, responseDelayMs, solution.MIdx);
        }

        // Handles all logic here
        static readonly byte[] WorkLoopBoundry = 1.0.ToTargetFromDiff();
        protected override void WorkLoop()
        {
            _startTime = DateTime.Now;

            // apply exponential sliding average
            _onWorkReceived(_f, this, new WorkPackage
            {
                Seed = H256.Random(),  // We don't actually need a real seed as the epoch is calculated upon block number (see poolmanager)
                Header = H256.Random(),
                Block = _block,
                Boundary = new H256(WorkLoopBoundry)
            });  // submit new fake job

            while (_session != null)
            {
                var hr = _f.HashRate;
                _hrMax = Math.Max(_hrMax, hr);
                _hrMean = _hrAlpha * _hrMean + (1.0f - _hrAlpha) * hr;
                Thread.Sleep(200);
            }
        }
    }
}