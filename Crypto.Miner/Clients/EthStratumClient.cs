//using System;
//using System.Threading;

//namespace Crypto.IO
//{
//    public class EthStratumClient : PoolClient
//    {
//        int _block;
//        DateTime _startTime;

//        float _hrAlpha = 0.45f;
//        float _hrMax = 0.0f;
//        float _hrMean = 0.0f;

//        public EthStratumClient(int block) : base("sim") => _block = block;

//        public override void Connect()
//        {
//            // Initialize new session
//            _connected = true; //: atomic
//            _session = new Session
//            {
//                Subscribed = true, //: atomic
//                Authorized = true,  //: atomic
//            };
//            _onConnected?.Invoke(Farm.F, this);
//            // No need to worry about starting again.
//            // Worker class prevents that
//            StartWorking();
//        }

//        public override void Disconnect()
//        {
//            Console.WriteLine($"Simulation results : {Ansi.WhiteBold}Max {_hrMax.ToFormattedHashes(6)} Mean {_hrMean.ToFormattedHashes(6)}{Ansi.Reset}");
//            _conn.AddDuration(_session.Duration);
//            _session = null;
//            _connected = false; //: atomic
//            _onDisconnected?.Invoke(Farm.F, this);
//        }

//        public override bool IsPendingState => false;

//        public override string ActiveEndPoint => string.Empty;

//        public override void SubmitHashrate(ulong rate, string id) { }

//        public override void SubmitSolution(Solution solution)
//        {
//            // This is a fake submission only evaluated locally
//            var submitStart = DateTime.Now;
//            var accepted = EthashAux.Eval(solution.Work.Epoch, solution.Work.Header, solution.Nonce).value <= solution.Work.Boundary;
//            var responseDelayMs = (DateTime.Now - submitStart).TotalMilliseconds;
//            if (accepted)
//                _onSolutionAccepted?.Invoke(Farm.F, this, responseDelayMs, solution.MIdx, false);
//            else
//                _onSolutionRejected?.Invoke(Farm.F, this, responseDelayMs, solution.MIdx);
//        }

//        // Handles all logic here
//        protected override void WorkLoop()
//        {
//            _startTime = DateTime.Now;

//            // apply exponential sliding average
//            _onWorkReceived(Farm.F, this, new WorkPackage
//            {
//                Seed = H256.Random(),  // We don't actually need a real seed as the epoch is calculated upon block number (see poolmanager)
//                Header = H256.Random(),
//                Block = _block,
//                Boundary = new H256(1.GetTargetFromDiff())
//            });  // submit new fake job

//            while (_session != null)
//            {
//                var hr = Farm.F.HashRate;
//                _hrMax = Math.Max(_hrMax, hr);
//                _hrMean = _hrAlpha * _hrMean + (1.0f - _hrAlpha) * hr;
//                Thread.Sleep(200);
//            }
//        }
//    }
//}