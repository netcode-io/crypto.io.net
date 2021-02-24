//#define ETH_ETHASHCUDA
//#define ETH_ETHASHCL
//#define ETH_ETHASHCPU
using Crypto.IO.Hwmon;
using Crypto.IO.Miner;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Crypto.IO
{
    /// <summary>
    /// Farm
    /// </summary>
    public class Farm : FarmFace
    {
        bool _paused = false; //: atomic

        object _minerWorkLock = new object();
        List<Miner.Miner> _miners;  // Collection of miners

        WorkPackage _currentWp;
        EpochContext _currentEc;

        bool _isMining = false; //: atomic

        TelemetryType _telemetry;  // Holds progress and status info for farm and miners

        Action<Farm, Solution> _onSolutionFound;
        Action<Farm> _onMinerRestart;

        FarmOptions _options;  // Own Farm Settings
        MinerCUOptions _cuOptions;  // Cuda settings passed to CUDA Miner instantiator
        MinerCLOptions _clOptions;  // OpenCL settings passed to CL Miner instantiator
        MinerCPOptions _cpOptions;  // CPU settings passed to CPU Miner instantiator

        //boost::asio::io_service::strand m_io_strand;
        Timer _collectTimer;
        const int _collectInterval = 5000;

        string _poolAddresses;

        // StartNonce (non-NiceHash Mode) and segment width assigned to each GPU as exponent of 2
        // considering an average block time of 15 seconds a single device GPU should need a speed of 286 Mh/s
        // before it consumes the whole 2^32 segment
        ulong _nonceScrambler;
        int _nonceSegmentWidth = 32;

        // Wrappers for hardware monitoring libraries and their mappers
        Nvml _nvml = null;
        Dictionary<string, int> _mapNvmlHandle = new Dictionary<string, int>();

        Adl _adl = null;
        Dictionary<string, int> _mapAdlHandle = new Dictionary<string, int>();

        readonly Dictionary<string, DeviceDescriptor> _devices;

        public static Farm F;
        public Farm(Dictionary<string, DeviceDescriptor> devices,
            FarmOptions options,
            MinerCUOptions cuOptions,
            MinerCLOptions clOptions,
            MinerCPOptions cpOptions)
        {
            F = this;
            _devices = devices;
            _options = options;
            _cuOptions = cuOptions;
            _clOptions = clOptions;
            _cpOptions = cpOptions;
            Console.WriteLine("Farm::Farm() begin");

            // Init HWMON if needed
            if (_options.HwMon != 0)
            {
                _telemetry.Hwmon = true;

                // Scan devices to identify which hw monitors to initialize
                var needAdl = false;
                var needNvml = false;
                foreach (var it in _devices.Values)
                {
                    if (it.SubscriptionType == DeviceSubscriptionType.Cuda)
                    {
                        needNvml = true;
                        continue;
                    }
                    if (it.SubscriptionType == DeviceSubscriptionType.OpenCL)
                    {
                        if (it.CLPlatformType == PlatformCLType.Nvidia)
                        {
                            needNvml = true;
                            continue;
                        }
                        if (it.CLPlatformType == PlatformCLType.Amd)
                        {
                            needAdl = true;
                            continue;
                        }
                    }
                }

                // Adl
                if (needAdl)
                    _adl = new Adl();
                if (_adl != null)
                    // Build Pci identification as done in miners.
                    for (var i = 0; i < _adl.GpuCount; i++)
                        _mapAdlHandle[_adl.GetPciId(i)] = i;

                // Nvml
                if (needNvml)
                    _nvml = new Nvml();
                if (_nvml != null)
                    // Build Pci identification as done in miners.
                    for (var i = 0; i < _nvml.GpuCount; i++)
                        _mapNvmlHandle[_nvml.GetPciId(i)] = i;
            }

            // Initialize nonce_scrambler
            Shuffle();

            // Start data collector timer
            // It should work for the whole lifetime of Farm regardless it's mining state
            _collectTimer = new Timer(CollectData, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(_collectInterval));

            Console.WriteLine("Farm::Farm() end");
        }

        public void Dispose()
        {
            Console.WriteLine("Farm::~Farm() begin");

            // Stop data collector (before monitors !!!)
            _collectTimer?.Dispose();

            //// Deinit HWMON
            //if (_adlh != null)
            //    wrap_adl_destroy(_adlh);
            //if (_nvmlh != null)
            //    wrap_nvml_destroy(_nvmlh);

            // Stop mining (if needed)
            if (_isMining) //: atomic
                Stop();

            Console.WriteLine("Farm::~Farm() end");
        }

        /// <summary>
        /// Randomizes the nonce scrambler.
        /// </summary>
        public void Shuffle()
        {
            var buf = new byte[8];
            new Random().NextBytes(buf);
            _nonceScrambler = BitConverter.ToUInt64(buf, 0);
        }

        /// <summary>
        /// Sets the current mining mission.
        /// </summary>
        /// <param name="wp">The work package we wish to be mining.</param>
        public void SetWork(WorkPackage wp)
        {
            if (_currentWp.Epoch != wp.Epoch)
            {
                //var ec = Ethash.GetGlobalEpochContext(wp.Epoch);
                //_currentEc.EpochNumber = wp.Epoch;
                //_currentEc.LightNumItems = ec.LightCacheNumItems;
                //_currentEc.LightSize = Ethash.GetLightCacheSize(ec.LightCacheNumItems);
                //_currentEc.DagNumItems = ec.FullDatasetNumItems;
                //_currentEc.DagSize = Ethash.GetFullDatasetSize(ec.FullDatasetNumItems);
                //_currentEc.LightCache = ec.LightCcache;
                foreach (var miner in _miners)
                    miner.SetEpoch(_currentEc);
            }

            _currentWp = wp;

            // Check if we need to shuffle per work (ergodicity == 2)
            if (_options.Ergodicity == 2 && _currentWp.ExSizeBytes == 0)
                Shuffle();

            ulong startNonce;
            if (_currentWp.ExSizeBytes > 0)
            {
                // Equally divide the residual segment among miners
                startNonce = _currentWp.StartNonce;
                _nonceSegmentWidth = (int)Math.Log(Math.Pow(2, 64 - (_currentWp.ExSizeBytes * 4)) / _miners.Count, 2);
            }
            // Get the randomly selected nonce
            else startNonce = _nonceScrambler;

            for (var i = 0; i < _miners.Count; i++)
            {
                _currentWp.StartNonce = startNonce + ((ulong)i << _nonceSegmentWidth);
                _miners[i].SetWork(_currentWp);
            }
        }

        /// <summary>
        /// Start a number of miners.
        /// </summary>
        /// <returns></returns>
        public bool Start()
        {
            // Prevent recursion
            if (_isMining) //: atomic
                return true;

            Console.WriteLine("Farm::start() begin");
            lock (_minerWorkLock)
            {
                // Start all subscribed miners if none yet
                if (_miners.Count == 0)
                {
                    foreach (var it in _devices.Values)
                    {
                        var minerTelemetry = new TelemetryAccountType();
#if ETH_ETHASHCUDA
                        if (it.SubscriptionType == DeviceSubscriptionType.Cuda)
                        {
                            minerTelemetry.Prefix = "cu";
                            _miners.Add(
                                new CUDAMiner(_miners.Count, _cuOptions, it));
                        }
#endif
#if ETH_ETHASHCL
                        if (it.SubscriptionType == DeviceSubscriptionType.OpenCL)
                        {
                            minerTelemetry.Prefix = "cl";
                            _miners.Add(
                                new CLMiner(_miners.Count, _clOptions, it));
                        }
#endif
#if ETH_ETHASHCPU
                        if (it.SubscriptionType == DeviceSubscriptionType.Cpu)
                        {
                            minerTelemetry.Prefix = "cp";
                            _miners.Add(
                                new CPUMiner(_miners.Count, _cpOptions, it));
                        }
#endif
                        if (string.IsNullOrEmpty(minerTelemetry.Prefix))
                            continue;
                        _telemetry.Miners.Add(minerTelemetry);
                        _miners.Last().StartWorking();
                    }

                    // Initialize DAG Load mode
                    Miner.Miner.SetDagLoadInfo(_options.DagLoadMode, _miners.Count);

                    _isMining = true; //: atomic
                }
                else
                {
                    foreach (var miner in _miners)
                        miner.StartWorking();
                    _isMining = true; //: atomic
                }
                Console.WriteLine("Farm::start() end");
                return _isMining; //: atomic
            }
        }

        /// <summary>
        /// All mining activities to a full stop.
        /// Implies all mining threads are stopped.
        /// </summary>
        public void Stop()
        {
            Console.WriteLine("Farm::stop() begin");
            // Avoid re-entering if not actually mining.
            // This, in fact, is also called by destructor
            if (IsMining)
                lock (_minerWorkLock)
                {
                    foreach (var miner in _miners)
                    {
                        miner.TriggerStopWorking();
                        miner.KickMiner();
                    }
                    _miners.Clear();
                    _isMining = false; //: atomic
                }
            Console.WriteLine("Farm::stop() end");
        }

        /// <summary>
        /// Signals all miners to suspend mining
        /// </summary>
        public void Pause()
        {
            // Signal each miner to suspend mining
            lock (_minerWorkLock)
            {
                _paused = true; //: atomic;
                foreach (var miner in _miners)
                    miner.Pause(MinerPauseTo.Farm_Paused);
            }
        }

        /// <summary>
        /// Whether or not the whole farm has been paused.
        /// </summary>
        /// <returns></returns>
        public bool Paused => _paused; //: atomic

        /// <summary>
        /// Signals all miners to resume mining.
        /// </summary>
        public void Resume()
        {
            // Signal each miner to resume mining
            // Note ! Miners may stay suspended if other reasons
            lock (_minerWorkLock)
            {
                _paused = false; //: atomic
                foreach (var miner in _miners)
                    miner.Resume(MinerPauseTo.Farm_Paused);
            }
        }

        /// <summary>
        /// Stop all mining activities and Starts them again.
        /// </summary>
        public void Restart() => _onMinerRestart?.Invoke(this);

        /// <summary>
        /// Stop all mining activities and Starts them again (async post).
        /// </summary>
        public Task RestartAsync() => _onMinerRestart != null
            ? Task.Run(() => _onMinerRestart(this))
            : Task.CompletedTask;

        /// <summary>
        /// Returns whether or not the farm has been started
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is mining; otherwise, <c>false</c>.
        /// </value>
        public bool IsMining => _isMining; //: atomic

        /// <summary>
        /// Spawn a reboot script.
        /// </summary>
        /// <param name="args">The arguments.</param>
        /// <returns>false if no matching file was found</returns>
        public bool Reboot(string[] args) =>
            SpawnFileInBinDir(Environment.OSVersion.Platform == PlatformID.Win32NT ? "reboot.cmd" : "reboot.sh", args);

        /// <summary>
        /// Get information on the progress of mining this work package.
        /// </summary>
        /// <value>
        /// The progress with mining so far.
        /// </value>
        public TelemetryType Telemetry => _telemetry;

        /// <summary>
        /// Gets current hashrate
        /// </summary>
        /// <returns></returns>
        public float HashRate => _telemetry.Farm.Hashrate;

        /// <summary>
        /// Gets the collection of pointers to miner instances
        /// </summary>
        /// <value>
        /// The miners.
        /// </value>
        public List<Miner.Miner> Miners => _miners;

        /// <summary>
        /// Gets the miner.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <returns></returns>
        public Miner.Miner GetMiner(int index) => index < _miners.Count ? _miners[index] : null;

        /// <summary>
        /// Accounts a solution to a miner and, as a consequence, to the whole farm
        /// </summary>
        /// <param name="minerIdx">Index of the miner.</param>
        /// <param name="accounting">The accounting.</param>
        public override void AccountSolution(int minerIdx, SolutionAccounting accounting)
        {
            if (accounting == SolutionAccounting.Accepted)
            {
                _telemetry.Farm.Solutions.Accepted++;
                _telemetry.Farm.Solutions.TStamp = DateTime.Now;
                _telemetry.Miners[minerIdx].Solutions.Accepted++;
                _telemetry.Miners[minerIdx].Solutions.TStamp = DateTime.Now;
                return;
            }
            if (accounting == SolutionAccounting.Wasted)
            {
                _telemetry.Farm.Solutions.Wasted++;
                _telemetry.Farm.Solutions.TStamp = DateTime.Now;
                _telemetry.Miners[minerIdx].Solutions.Wasted++;
                _telemetry.Miners[minerIdx].Solutions.TStamp = DateTime.Now;
                return;
            }
            if (accounting == SolutionAccounting.Rejected)
            {
                _telemetry.Farm.Solutions.Rejected++;
                _telemetry.Farm.Solutions.TStamp = DateTime.Now;
                _telemetry.Miners[minerIdx].Solutions.Rejected++;
                _telemetry.Miners[minerIdx].Solutions.TStamp = DateTime.Now;
                return;
            }
            if (accounting == SolutionAccounting.Failed)
            {
                _telemetry.Farm.Solutions.Failed++;
                _telemetry.Farm.Solutions.TStamp = DateTime.Now;
                _telemetry.Miners[minerIdx].Solutions.Failed++;
                _telemetry.Miners[minerIdx].Solutions.TStamp = DateTime.Now;
                return;
            }
        }

        /// <summary>
        /// Gets the solutions account for the whole farm
        /// </summary>
        public SolutionAccountType GetSolutions() => _telemetry.Farm.Solutions;

        /// <summary>
        /// Gets the solutions account for single miner
        /// </summary>
        /// <param name="minerIdx">Index of the miner.</param>
        /// <returns></returns>
        public SolutionAccountType GetSolutions(int minerIdx) => minerIdx < _telemetry.Miners.Count ? _telemetry.Miners[minerIdx].Solutions : null;

        /// <summary>
        /// Provides a valid header based upon that received previously with SetWork().
        /// </summary>
        /// <param name="bi">The now-valid header.</param>
        /// <returns>true if the header was good and that the Farm should pause until more work is submitted.</returns>
        public void OnSolutionFound(Action<Farm, Solution> handler) => _onSolutionFound = handler;

        public void OnMinerRestart(Action<Farm> handler) => _onMinerRestart = handler;

        /// <summary>
        /// Gets the actual start nonce of the segment picked by the farm.
        /// </summary>
        /// <returns></returns>
        public ulong GetNonceScrambler() => _nonceScrambler;

        /// <summary>
        /// Gets the actual width of each subsegment assigned to miners.
        /// </summary>
        /// <returns></returns>
        public int GetSegmentWidth() => _nonceSegmentWidth;

        /// <summary>
        /// Sets the actual start nonce of the segment picked by the farm.
        /// </summary>
        /// <param name="n">The n.</param>
        public void SetNonceScrambler(ulong n) => _nonceScrambler = n;

        /// <summary>
        /// Sets the actual width of each subsegment assigned to miners.
        /// </summary>
        /// <param name="n">The n.</param>
        public void SetNonceSegmentWidth(int n)
        {
            if (_currentWp.ExSizeBytes == 0)
                _nonceSegmentWidth = n;
        }

        /// <summary>
        /// Provides the description of segments each miner is working on.
        /// </summary>
        /// <returns>A JsonObject</returns>
        JsonElement GetNonceScramblerJson()
        {
            throw new NotImplementedException();
            //var r = new JsonElement();
            //r["start_nonce"] = $"{_nonceScrambler:x}";
            //r["device_width"] = _nonceSegmentWidth;
            //r["device_count"] = (ulong)_miners.Count;
            //return r;
        }

        public void SetTStartTStop(int tstart, int tstop)
        {
            _options.TempStart = tstart;
            _options.TempStop = tstop;
        }

        public override int TStart => _options.TempStart;

        public override int TStop => _options.TempStop;

        public override int Ergodicity => _options.Ergodicity;

        /// <summary>
        /// Called from a Miner to note a WorkPackage has a solution.
        /// </summary>
        /// <param name="s">The solution.</param>
        public override void SubmitProof(Solution s) => Task.Run(() => SubmitProofAsync(s));

        /// <summary>
        /// Async submits solution serializing execution in Farm's strand
        /// </summary>
        /// <param name="s">The s.</param>
        public void SubmitProofAsync(Solution s)
        {
            if (!_options.NoEval)
            {
                var r = EthashAux.Eval(s.Work.Epoch, s.Work.Header, s.Nonce);
                if (r.value > s.Work.Boundary)
                {
                    AccountSolution(s.MIdx, SolutionAccounting.Failed);
                    //WARNING
                    Console.WriteLine($"GPU {s.MIdx} gave incorrect result. Lower overclocking values if it happens frequently.");
                    return;
                }
                _onSolutionFound(this, new Solution
                {
                    Nonce = s.Nonce,
                    MixHash = r.mixHash,
                    Work = s.Work,
                    TStamp = s.TStamp,
                    MIdx = s.MIdx
                });
            }
            else _onSolutionFound(this, s);

#if DEBUG
            //if (_logOptions & LOG_SUBMIT)
            //NOTE
            Console.WriteLine($"Submit time: {(DateTime.Now - s.TStamp).Milliseconds} us.");
#endif
        }

        /// <summary>
        /// Collects data about hashing and hardware status.
        /// </summary>
        /// <param name="ec">The ec.</param>
        void CollectData(object ec)
        {
            if (ec != null)
                return;

            // Reset hashrate (it will accumulate from miners)
            var farmHr = 0.0f;

            // Process miners
            foreach (var miner in _miners)
            {
                var minerIdx = miner.Index;
                var hr = miner.Paused ? 0.0f : miner.RetrieveHashRate();
                farmHr += hr;
                _telemetry.Miners[minerIdx].Hashrate = hr;
                _telemetry.Miners[minerIdx].Paused = miner.Paused;

                if (_options.HwMon != 0)
                {
                    var hwInfo = miner.HwmonInfo;
                    int tempC = 0, fanpcnt = 0, powerW = 0;
                    if (hwInfo.DeviceType == HwMonitorInfoType.NVidia && _nvml != null)
                    {
                        var devIdx = hwInfo.DeviceIndex;
                        if (devIdx == -1 && !string.IsNullOrEmpty(hwInfo.DevicePciId))
                            miner.SetHwmonDeviceIndex(_mapNvmlHandle.TryGetValue(hwInfo.DevicePciId, out devIdx) ? devIdx : -2); // -2 will prevent further tries to map
                        if (devIdx >= 0)
                        {
                            //wrap_nvml_get_tempC(_nvmlh, devIdx, out tempC);
                            //wrap_nvml_get_fanpcnt(_nvmlh, devIdx, out fanpcnt);
                            //if (_options.HwMon == 2)
                            //    wrap_nvml_get_power_usage(_nvmlh, devIdx, out powerW);
                        }
                    }
                    else if (hwInfo.DeviceType == HwMonitorInfoType.Amd)
                    {
                        if (_adl != null)  // Windows only for AMD
                        {
                            var devIdx = hwInfo.DeviceIndex;
                            if (devIdx == -1 && !string.IsNullOrEmpty(hwInfo.DevicePciId))
                                miner.SetHwmonDeviceIndex(_mapAdlHandle.TryGetValue(hwInfo.DevicePciId, out devIdx) ? devIdx : -2); // -2 will prevent further tries to map
                            if (devIdx >= 0)
                            {
                                //wrap_adl_get_tempC(_adlh, devIdx, out tempC);
                                //wrap_adl_get_fanpcnt(_adlh, devIdx, out fanpcnt);
                                //if (_options.HwMon == 2)
                                //    wrap_adl_get_power_usage(_adlh, devIdx, out powerW);
                            }
                        }
                    }

                    // If temperature control has been enabled call check threshold
                    if (_options.TempStop != 0)
                    {
                        var paused = miner.PauseTest(MinerPauseTo.Overheating);
                        if (!paused && (tempC >= _options.TempStop))
                            miner.Pause(MinerPauseTo.Overheating);
                        if (paused && (tempC <= _options.TempStop))
                            miner.Resume(MinerPauseTo.Overheating);
                    }

                    _telemetry.Miners[minerIdx].Sensors.TempC = tempC;
                    _telemetry.Miners[minerIdx].Sensors.FanP = fanpcnt;
                    _telemetry.Miners[minerIdx].Sensors.PowerW = powerW / ((double)1000.0);
                }
                _telemetry.Farm.Hashrate = farmHr;
                miner.TriggerHashRateUpdate();
            }

            // Resubmit timer for another loop
            //m_collectTimer.expires_from_now(boost::posix_time::milliseconds(m_collectInterval));
            //m_collectTimer.async_wait(
            //    m_io_strand.wrap(boost::bind(&Farm::collectData, this, boost::asio::placeholders::error)));
        }

        /// <summary>
        /// Spawn a file - must be located in the directory of ethminer binary.
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <param name="args">The arguments.</param>
        /// <returns>false if file was not found or it is not executeable</returns>
        bool SpawnFileInBinDir(string filename, string[] args)
        {
            var fn = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename);
            try
            {
                if (!File.Exists(fn))
                    return false;
                if (new FileInfo(fn).Length == 0)
                    return false;
                new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        WindowStyle = ProcessWindowStyle.Hidden,
                        FileName = fn,
                        Arguments = string.Join("", args),
                    }
                }.Start();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
