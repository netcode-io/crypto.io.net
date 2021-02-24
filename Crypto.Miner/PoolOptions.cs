using System;
using System.Collections.Generic;

namespace Crypto.IO
{
    /// <summary>
    /// PoolOptions
    /// </summary>
    public class PoolOptions
    {
        /// <summary>
        /// List of connection definitions.
        /// </summary>
        public List<Uri> Connections;
        /// <summary>
        /// Interval (ms) between getwork requests
        /// </summary>
        public int GetWorkPollInterval = 500;
        /// <summary>
        /// If no new jobs in this number of seconds drop connection.
        /// </summary>
        public int NoWorkTimeout = 180;
        /// <summary>
        /// If no response in this number of seconds drop connection.
        /// </summary>
        public int NoResponseTimeout = 2;
        /// <summary>
        /// Return to primary pool after this number of minutes.
        /// </summary>
        public int PoolFailoverTimeout = 0;
        /// <summary>
        /// Whether or not to report hashrate to pool.
        /// </summary>
        public bool ReportHashrate = false;
        /// <summary>
        /// Interval in seconds among hashrate submissions.
        /// </summary>
        public int HashRateInterval = 60;
        /// <summary>
        /// Unique identifier for HashRate submission
        /// </summary>
        public string HashRateId = "TODO"; //h256::random().hex(HexPrefix::Add);
        /// <summary>
        /// Max number of connection retries
        /// </summary>
        public int ConnectionMaxRetries = 3;
        /// <summary>
        /// Delay seconds before connect retry.
        /// </summary>
        public int DelayBeforeRetry = 0;
        /// <summary>
        /// Block number used by SimulateClient to test performances.
        /// </summary>
        public int BenchmarkBlock = 0;
    }
}
