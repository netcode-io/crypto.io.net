using System;
using System.Threading;

namespace Crypto.IO.Miner
{
    /// <summary>
    /// A miner - a member and adoptee of the Farm.
    /// Not threadsafe. It is assumed Farm will synchronise calls to/from this class.
    /// </summary>
    /// <seealso cref="Worker" />
    public abstract class Miner : Worker
    {
        const int DAG_LOAD_MODE_PARALLEL = 0;
        const int DAG_LOAD_MODE_SEQUENTIAL = 1;

        static int _minersCount = 0;   // Total Number of Miners
        static int _dagLoadMode = 0;   // Way dag should be loaded
        static int _dagLoadIndex = 0;  // In case of serialized load of dag this is the index of miner which should load next

        int _index = 0;           // Ordinal index of the Instance (not the device)
        EpochContext _epochContext;
#if DEBUG
        DateTime _workSwitchStart;
#endif
        object _workLock = new object();
        object _pauseLock = new object();
        //object _new_work_signal = new object();
        object _dag_loaded_signal = new object();
        MinerPauseTo _pauseFlags;
        WorkPackage _work = new WorkPackage();
        DateTime _hashTime = DateTime.Now;
        float _hashRate = 0.0f; //: atomic
        ulong _groupCount = 0UL;
        int _hashRateUpdate = 0; //: atomic

        public Miner(string name, int index) : base($"{name}{index}") => Index = index;

        // Sets basic info for eventual serialization of DAG load
        public static void SetDagLoadInfo(int mode, int deviceCount)
        {
            _dagLoadMode = mode;
            _dagLoadIndex = 0;
            _minersCount = deviceCount;
        }

        /// <summary>
        /// Gets the device descriptor assigned to this instance.
        /// </summary>
        /// <returns></returns>
        public DeviceDescriptor Descriptor { get; private set; }

        /// <summary>
        /// Assigns hashing work to this instance.
        /// </summary>
        /// <param name="work">The work.</param>
        public void SetWork(WorkPackage work)
        {
            lock (_workLock)
            {
                // void work if this miner is paused
                if (Paused) _work.Header = H256.Empty;
                else _work = work;
#if DEBUG
                _workSwitchStart = DateTime.Now;
#endif
            }
            KickMiner();
        }

        /// <summary>
        /// Assigns Epoch context to this instance.
        /// </summary>
        /// <param name="ec">The ec.</param>
        public void SetEpoch(EpochContext ec) => _epochContext = ec;

        public int Index { get; private set; }

        public HwMonitorInfo HwmonInfo { get; private set; } = new HwMonitorInfo();

        public void SetHwmonDeviceIndex(int i) => HwmonInfo.DeviceIndex = i;

        /// <summary>
        /// Kick an asleep miner..
        /// </summary>
        /// <returns></returns>
        public abstract void KickMiner();

        /// <summary>
        /// Pauses mining setting a reason flag.
        /// </summary>
        /// <param name="what">The what.</param>
        /// <returns></returns>
        public void Pause(MinerPauseTo what)
        {
            lock (_pauseLock)
            {
                _pauseFlags |= what;
                _work.Header = H256.Empty;
                KickMiner();
            }
        }

        /// <summary>
        /// Whether or not this miner is paused for any reason.
        /// </summary>
        /// <value>
        ///   <c>true</c> if paused; otherwise, <c>false</c>.
        /// </value>
        public bool Paused
        {
            get
            {
                lock (_pauseLock)
                    return _pauseFlags != 0;
            }
        }

        /// <summary>
        /// Checks if the given reason for pausing is currently active.
        /// </summary>
        /// <param name="what">The what.</param>
        /// <returns></returns>
        public bool PauseTest(MinerPauseTo what)
        {
            lock (_pauseLock)
                return (_pauseFlags & what) == what;
        }

        /// <summary>
        /// Returns the human readable reason for this miner being paused.
        /// </summary>
        /// <value>
        /// The paused string.
        /// </value>
        public string PausedString
        {
            get
            {
                lock (_pauseLock)
                    return _pauseFlags == 0
                        ? string.Empty
                        : _pauseFlags.ToString().Replace("_", " ");
            }
        }

        /// <summary>
        /// Cancels a pause flag.
        /// Miner can be paused for multiple reasons at a time.
        /// </summary>
        /// <param name="fromwhat">The fromwhat.</param>
        public void Resume(MinerPauseTo fromwhat)
        {
            lock (_pauseLock)
                _pauseFlags &= ~fromwhat;
        }

        /// <summary>
        /// Retrieves currrently collected hashrate
        /// </summary>
        /// <returns></returns>
        public float RetrieveHashRate() => _hashRate; //: atomic

        public void TriggerHashRateUpdate()
        {
            if (Interlocked.CompareExchange(ref _hashRateUpdate, 0, 1) == 1)
                return;
            _hashRate = 0.0f;
        }

        /// <summary>
        /// Initializes miner's device.
        /// </summary>
        /// <returns></returns>
        protected abstract bool InitDevice();

        /// <summary>
        /// Initializes miner to current (or changed) epoch..
        /// </summary>
        /// <returns></returns>
        protected bool InitEpoch()
        {
            // When loading of DAG is sequential wait for this instance to become current
            if (_dagLoadMode == DAG_LOAD_MODE_SEQUENTIAL)
            {
                while (_dagLoadIndex < _index)
                    Monitor.Wait(_dag_loaded_signal, 3000);
                if (ShouldStop)
                    return false;
            }

            // Run the internal initialization specific for miner
            var result = InitEpochInternal();

            // Advance to next miner or reset to zero for  next run if all have processed
            if (_dagLoadMode == DAG_LOAD_MODE_SEQUENTIAL)
            {
                _dagLoadIndex = _index + 1;
                if (_minersCount == _dagLoadIndex) _dagLoadIndex = 0;
                else Monitor.PulseAll(_dag_loaded_signal);
            }
            return result;
        }

        /// <summary>
        /// Miner's specific initialization to current (or changed) epoch..
        /// </summary>
        /// <returns></returns>
        protected abstract bool InitEpochInternal();

        /// <summary>
        /// Returns current workpackage this miner is working on.
        /// </summary>
        /// <returns></returns>
        public WorkPackage Work()
        {
            lock (_workLock)
                return _work;
        }

        public void UpdateHashRate(uint groupSize, uint increment)
        {
            _groupCount += increment;
            if (Interlocked.CompareExchange(ref _hashRateUpdate, 1, 0) == 0)
                return;
            var t = DateTime.Now;
            var us = (t - _hashTime).Milliseconds;
            _hashTime = t;
            _hashRate = us != 0 ? _groupCount * groupSize * 1.0e6f / us : 0.0f; //: atomic
            _groupCount = 0;
        }
    }
}
