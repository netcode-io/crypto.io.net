using Crypto.IO.Miner;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Crypto.IO
{
    public class PoolManager
    {
        static PoolManager P;
        readonly PoolOptions _options;
        int _running = 0; //: atomic<bool>
        int _stopping = 0; //: atomic<bool>
        int _asyncPending = 0; //: atomic<bool>
        int _connectionAttempt = 0;
        string _selectedHost = string.Empty;  // Holds host name (and endpoint) of selected connection
        int _connectionSwitches = 0; //: atomic
        int _activeConnectionIdx = 0;
        WorkPackage _currentWp;
        Timer _failoverTimer;
        Timer _submithrTimer;
        Timer _reconnectTimer;
        PoolClient _client = null;
        int _epochChanges = 0; //: atomic

        public PoolManager(PoolOptions options)
        {
            P = this;
            _options = options;
            _currentWp.Header = new H256();

            Farm.F.OnMinerRestart(f =>
            {
                Console.WriteLine("Restart miners...");
                if (f.IsMining)
                {
                    Console.WriteLine("Shutting down miners...");
                    f.Stop();
                }
                Console.WriteLine("Spinning up miners...");
                f.Start();
            });

            Farm.F.OnSolutionFound((f, sol) =>
            {
                // Solution should passthrough only if client is properly connected. Otherwise we'll have the bad behavior
                // to log nonce submission but receive no response
                if (_client != null && _client.IsConnected)
                    _client.SubmitSolution(sol);
                else
                    Console.WriteLine($"{Ansi.Orange}Solution 0x{sol.Nonce:x} wasted. Waiting for connection...");
            });
        }

        void SetClientHandlers()
        {
            _client.OnConnected((f, c) =>
            {
                var connection = c.Connection;
                // If HostName is already an IP address no need to append the effective ip address.
                if ((connection.HostNameType == UriHostNameType.Dns || connection.HostNameType == UriHostNameType.Basic) && !string.IsNullOrEmpty(c.ActiveEndPoint))
                    _selectedHost = connection.Host + c.ActiveEndPoint;

                Console.WriteLine($"Established connection to {_selectedHost}");
                _connectionAttempt = 0;

                // Reset current WorkPackage
                _currentWp.Job = null;
                _currentWp.Header = H256.Empty;

                // Shuffle if needed
                if (f.Ergodicity == 1U)
                    f.Shuffle();

                // Rough implementation to return to primary pool after specified amount of time
                _failoverTimer = _activeConnectionIdx != 0 && _options.PoolFailoverTimeout != 0
                    ? new Timer(FailoverTimer_Elapsed, null, new TimeSpan(0, _options.PoolFailoverTimeout, 0), new TimeSpan(0, 0, -1))
                    : null;

                if (!f.IsMining)
                {
                    Console.WriteLine("Spinning up miners...");
                    f.Start();
                }
                else if (f.Paused)
                {
                    Console.WriteLine("Resume mining ...");
                    f.Resume();
                }

                // Activate timing for HR submission
                _submithrTimer = _options.ReportHashrate
                    ? new Timer(SubmithrTimer_Elapsed, null, new TimeSpan(0, 0, _options.HashRateInterval), new TimeSpan(0, 0, -1))
                    : null;

                // Signal async operations have completed
                _asyncPending = 0; //: atomic
            });

            _client.OnDisconnected((f, c) =>
            {
                Console.WriteLine($"Disconnected from {_selectedHost}");

                // Clear current connection
                c.UnsetConnection();
                _currentWp.Header = H256.Empty;

                // Stop timing actors
                _failoverTimer?.Dispose(); _failoverTimer = null;
                _submithrTimer?.Dispose(); _submithrTimer = null;

                if (_stopping != 0) //: atomic
                {
                    if (f.IsMining)
                    {
                        Console.WriteLine("Shutting down miners...");
                        f.Stop();
                    }
                    _running = 0; //: atomic
                }
                else
                {
                    // Signal we will reconnect async
                    _asyncPending = 1; //: atomic

                    // Suspend mining and submit new connection request
                    Console.WriteLine("No connection. Suspend mining ...");
                    f.Pause();
                    Task.Run(() => RotateConnect());
                }
            });

            _client.OnWorkReceived((f, c, wp) =>
            {
                // Should not happen !
                if (wp == null)
                    return;

                var _currentEpoch = _currentWp.Epoch;
                var newEpoch = _currentEpoch == -1;
                // In EthereumStratum/2.0.0 epoch number is set in session
                if (!newEpoch)
                    newEpoch = c.Connection.StratumMode() == 3
                        ? wp.Epoch != _currentWp.Epoch
                        : wp.Seed != _currentWp.Seed;
                var newDiff = wp.Boundary != _currentWp.Boundary;
                _currentWp = wp;

                if (newEpoch)
                {
                    Interlocked.Add(ref _epochChanges, 1);
                    // If epoch is valued in workpackage take it
                    if (wp.Epoch == -1)
                        _currentWp.Epoch = _currentWp.Block >= 0
                            ? _currentWp.Block / 30000
                            : Ethash.FindEpochNumber(Ethash.Hash256FromBytes(_currentWp.Seed.Data));
                }
                else _currentWp.Epoch = _currentEpoch;

                if (newDiff || newEpoch)
                    ShowMiningAt();

                Console.WriteLine($"Job: {Ansi.White}{_currentWp.Header.Abridged}{(_currentWp.Block != -1 ? $" block {_currentWp.Block}" : null)}{Ansi.Reset} {_selectedHost}");
                f.SetWork(_currentWp);
            });

            _client.OnSolutionAccepted((f, c, responseDelay, minerIdx, asStale) =>
            {
                Console.WriteLine($"{Ansi.Lime}**Accepted{(asStale ? " stale" : null)}{Ansi.Reset}{responseDelay,4} ms. {_selectedHost}");
                f.AccountSolution(minerIdx, SolutionAccounting.Accepted);
            });

            _client.OnSolutionRejected((f, c, responseDelay, minerIdx) =>
            {
                Console.WriteLine($"{Ansi.Red}**Rejected{Ansi.Reset}{responseDelay,4} ms. {_selectedHost}");
                f.AccountSolution(minerIdx, SolutionAccounting.Rejected);
            });
        }

        public void Stop()
        {
            Console.WriteLine("PoolManager::stop() begin");
            if (_running != 0) //: atomic
            {
                _asyncPending = 1; //: atomic
                _stopping = 1; //: atomic
                if (_client != null && _client.IsConnected)
                {
                    _client.Disconnect();
                    // Wait for async operations to complete
                    while (_running != 0) //: atomic
                        Thread.Sleep(500);
                    _client = null;
                }
                else
                {
                    // Stop timing actors
                    _failoverTimer?.Dispose(); _failoverTimer = null;
                    _submithrTimer?.Dispose(); _submithrTimer = null;
                    _reconnectTimer?.Dispose(); _reconnectTimer = null;
                    if (Farm.F.IsMining)
                    {
                        Console.WriteLine("Shutting down miners...");
                        Farm.F.Stop();
                    }
                }
            }
            Console.WriteLine("PoolManager::stop() end");
        }

        public void AddConnection(string connstring) => _options.Connections.Add(new Uri(connstring));

        public void AddConnection(Uri uri) => _options.Connections.Add(uri);

        /// <summary>
        /// Removes the connection.
        /// </summary>
        /// <param name="index">The index.</param>
        public void RemoveConnection(int index)
        {
            // Are there any outstanding operations ?
            if (_asyncPending != 0) //: atomic
                throw new InvalidOperationException("Outstanding operations. Retry ...");

            // Check bounds
            if (index < 0 || index >= _options.Connections.Count)
                throw new IndexOutOfRangeException("Index out-of bounds.");

            // Can't delete active connection
            if (index == _activeConnectionIdx)
                throw new InvalidOperationException("Can't remove active connection");

            // Remove the selected connection
            _options.Connections.RemoveAt(index);
            if (_activeConnectionIdx > index)
                _activeConnectionIdx--;
        }

        private void SetActiveConnectionCommon(int index)
        {
            // Are there any outstanding operations ?
            if (Interlocked.CompareExchange(ref _asyncPending, 0, 1) != 1)
                throw new InvalidOperationException("Outstanding operations. Retry ...");

            if (index != _activeConnectionIdx)
            {
                Interlocked.Add(ref _connectionSwitches, 1);
                _activeConnectionIdx = index;
                _connectionAttempt = 0;
                _client.Disconnect();
            }
            // Release the flag immediately
            else _asyncPending = 0; //: atomic
        }

        /// <summary>
        /// Sets the active connection.
        /// </summary>
        /// <param name="index">The index.</param>
        public void SetActiveConnection(int index)
        {
            // Sets the active connection to the requested index
            if (index >= _options.Connections.Count)
                throw new IndexOutOfRangeException("Index out-of bounds.");
            SetActiveConnectionCommon(index);
        }

        public void SetActiveConnection(string connstring)
        {
            for (var idx = 0; idx < _options.Connections.Count; idx++)
                if (_options.Connections[idx].ToString() == connstring)
                {
                    SetActiveConnectionCommon(idx);
                    return;
                }
            throw new InvalidOperationException("Not found.");
        }

        public Uri ActiveConnection => _activeConnectionIdx < _options.Connections.Count ? _options.Connections[_activeConnectionIdx] : null;

        public object GetConnectionsJson()
        {
            // Returns the list of configured connections
            //Json::Value jRes;
            //for (size_t i = 0; i < m_Settings.connections.size(); i++)
            //{
            //    Json::Value JConn;
            //    JConn["index"] = (unsigned)i;
            //    JConn["active"] = (i == m_activeConnectionIdx ? true : false);
            //    JConn["uri"] = m_Settings.connections[i]->str();
            //    jRes.append(JConn);
            //}
            //return jRes;
            return null;
        }

        public void Start()
        {
            _running = 1; //: atomic
            _asyncPending = 1; //: atomic
            Interlocked.Add(ref _connectionSwitches, 1);
            Task.Run(() => RotateConnect());
        }

        private void RotateConnect()
        {
            if (_client != null && _client.IsConnected)
                return;

            var connections = _options.Connections;

            // Check we're within bounds
            if (_activeConnectionIdx >= connections.Count)
                _activeConnectionIdx = 0;

            // If this connection is marked Unrecoverable then discard it
            if (connections[_activeConnectionIdx].IsUnrecoverable())
            {
                connections.RemoveAt(_activeConnectionIdx);
                _connectionAttempt = 0;
                if (_activeConnectionIdx >= connections.Count)
                    _activeConnectionIdx = 0;
                Interlocked.Add(ref _connectionSwitches, 1);
            }
            else if (_connectionAttempt >= _options.ConnectionMaxRetries)
            {
                // If this is the only connection we can't rotate forever
                if (connections.Count == 1)
                    connections.RemoveAt(_activeConnectionIdx);
                // Rotate connections if above max attempts threshold
                else
                {
                    _connectionAttempt = 0;
                    _activeConnectionIdx++;
                    if (_activeConnectionIdx >= connections.Count)
                        _activeConnectionIdx = 0;
                    Interlocked.Add(ref _connectionSwitches, 1);
                }
            }

            if (connections.Count != 0 && connections[_activeConnectionIdx].Host != "exit")
            {
                if (_client != null)
                    _client = null;
                var connection = connections[_activeConnectionIdx];
                switch (connection.Family())
                {
                    //case UriFamily.GETWORK: _client = new EthGetworkClient(_options.NoWorkTimeout, _options.GetWorkPollInterval); break;
                    //case UriFamily.STRATUM: _client = new EthStratumClient(_options.NoWorkTimeout, _options.NoResponseTimeout); break;
                    //case UriFamily.SIMULATION: _client = new SimulateClient(_options.BenchmarkBlock); break;
                }
                if (_client != null)
                    SetClientHandlers();

                // Count connectionAttempts
                _connectionAttempt++;

                // Invoke connections
                _selectedHost = $"{connection.Host}:{connection.Port}";
                _client.SetConnection(connection);
                Console.WriteLine($"Selected pool {_selectedHost}");

                if (_connectionAttempt > 1 && _options.DelayBeforeRetry > 0)
                {
                    Console.WriteLine($"Next connection attempt in {_options.DelayBeforeRetry} seconds");
                    _reconnectTimer = new Timer(ReconnectTimer_Elapsed, null, new TimeSpan(0, 0, _options.DelayBeforeRetry), new TimeSpan(0, 0, -1));
                }
                else _client.Connect();
            }
            else
            {
                Console.WriteLine(connections.Count == 0 ? "No more connections to try. Exiting..." : "'exit' failover just got hit. Exiting...");

                // Stop mining if applicable
                if (Farm.F.IsMining)
                {
                    Console.WriteLine("Shutting down miners...");
                    Farm.F.Stop();
                }

                _running = 0; //: atomic;
                Environment.Exit(0);
            }
        }

        private void ShowMiningAt()
        {
            // Should not happen
            if (_currentWp == null)
                return;
            //double d = dev::getHashesToTarget(_currentWp.Boundary.hex(HexPrefix::Add));
            Console.WriteLine($"Epoch : {Ansi.White} {_currentWp.Epoch}{Ansi.Reset} Difficulty : {Ansi.White}dev::getFormattedHashes(d){Ansi.Reset}");
        }

        void FailoverTimer_Elapsed(object ec)
        {
            if (ec != null)
                return;
            if (_running != 0) //: atomic
                if (_activeConnectionIdx != 0)
                {
                    _activeConnectionIdx = 0;
                    _connectionAttempt = 0;
                    Interlocked.Add(ref _connectionSwitches, 1);
                    Console.WriteLine("Failover timeout reached, retrying connection to primary pool");
                    _client.Disconnect();
                }
        }

        void SubmithrTimer_Elapsed(object ec)
        {
            if (ec != null)
                return;
            if (_running != 0) //: atomic
            {
                if (_client != null && _client.IsConnected)
                    _client.SubmitHashrate((uint)Farm.F.HashRate, _options.HashRateId);
                // Resubmit actor
                _submithrTimer.Change(new TimeSpan(0, 0, _options.HashRateInterval), new TimeSpan(0, 0, -1));
            }
        }

        void ReconnectTimer_Elapsed(object ec)
        {
            if (ec != null)
                return;
            if (_running != 0) //: atomic
                if (_client != null && !_client.IsConnected)
                    _client.Connect();
        }

        public bool IsConnected => _client.IsConnected;

        public bool IsRunning => _running != 0;

        public int CurrentEpoch => _currentWp.Epoch;

        public double CurrentDifficulty => _currentWp == null
            ? 0.0
            : _currentWp.Boundary.Data.ToTargetFromHash();

        public int ConnectionSwitches => _connectionSwitches; //: atomic

        public int EpochChanges() => _epochChanges; //: atomic
    }
}
