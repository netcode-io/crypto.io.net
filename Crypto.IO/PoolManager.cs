using System;

namespace CryptoPool.IO
{
    public class PoolManager
    {
        readonly PoolOptions _options;

        bool _running = false; //: atomic
        bool _stopping = false; //: atomic
        bool _async_pending = false; //: atomic

        int _connectionAttempt = 0;

        string _selectedHost = string.Empty;  // Holds host name (and endpoint) of selected connection
        int _connectionSwitches = 0; //: atomic

        int _activeConnectionIdx = 0;

        WorkPackage _currentWp;

        //strand _io_strand;
        //deadline_timer _failovertimer;
        //deadline_timer _submithrtimer;
        //deadline_timer _reconnecttimer;

        PoolClient _client = null;

        int _epochChanges = 0; //: atomic

        static PoolManager _this;

        public PoolManager(PoolOptions options)
        {
            _options = options;
            //_io_strand = g_io_service,
            //_failovertimer = g_io_service,
            //_submithrtimer = g_io_service,
            //_reconnecttimer = g_io_service

            _this = this;

            _currentWp.header = h256();

            //Farm::f().onMinerRestart([&]() {
            //    cnote << "Restart miners...";

            //    if (Farm::f().isMining())
            //    {
            //        cnote << "Shutting down miners...";
            //        Farm::f().stop();
            //    }

            //    cnote << "Spinning up miners...";
            //    Farm::f().start();
            //});

            //Farm::f().onSolutionFound([&](const Solution&sol) {
            //    // Solution should passthrough only if client is
            //    // properly connected. Otherwise we'll have the bad behavior
            //    // to log nonce submission but receive no response

            //    if (_client && _client->isConnected())
            //    {
            //        _client->submitSolution(sol);
            //    }
            //    else
            //    {
            //        cnote << string(EthOrange "Solution 0x") + toHex(sol.nonce)
            //              << " wasted. Waiting for connection...";
            //    }

            //    return false;
            //});
        }


        private void setClientHandlers()
        {
            _client.onConnected([&]() {
                {

                    // If HostName is already an IP address no need to append the
                    // effective ip address.
                    if (p_client->getConnection()->HostNameType() == dev::UriHostNameType::Dns ||
                        p_client->getConnection()->HostNameType() == dev::UriHostNameType::Basic)
                    {
                        string ep = _client->ActiveEndPoint();
                        if (!ep.empty())
                            _selectedHost = _client->getConnection()->Host() + ep;
                    }

                    cnote << "Established connection to " << m_selectedHost;
                    m_connectionAttempt = 0;

                    // Reset current WorkPackage
                    m_currentWp.job.clear();
                    m_currentWp.header = h256();

                    // Shuffle if needed
                    if (Farm::f().get_ergodicity() == 1U)
                        Farm::f().shuffle();

                    // Rough implementation to return to primary pool
                    // after specified amount of time
                    if (m_activeConnectionIdx != 0 && m_Settings.poolFailoverTimeout)
                    {
                        m_failovertimer.expires_from_now(
                            boost::posix_time::minutes(m_Settings.poolFailoverTimeout));
                        m_failovertimer.async_wait(m_io_strand.wrap(boost::bind(
                            &PoolManager::failovertimer_elapsed, this, boost::asio::placeholders::error)));
                    }
                    else
                    {
                        m_failovertimer.cancel();
                    }
                }

                if (!Farm::f().isMining())
                {
                    Console.WriteLine("Spinning up miners...");
                    Farm::f().start();
                }
                else if (Farm::f().paused())
                {
                    Console.WriteLine("Resume mining ...");
                    Farm::f().resume();
                }

                // Activate timing for HR submission
                if (m_Settings.reportHashrate)
                {
                    m_submithrtimer.expires_from_now(boost::posix_time::seconds(m_Settings.hashRateInterval));
                    m_submithrtimer.async_wait(m_io_strand.wrap(boost::bind(
                        &PoolManager::submithrtimer_elapsed, this, boost::asio::placeholders::error)));
                }

                // Signal async operations have completed
                m_async_pending.store(false, std::memory_order_relaxed);
            }



            //public static PoolManager& p() { return *m_this; }
            public void AddConnection(string connstring)
        {

        }
        public void AddConnection(Uri uri)
        {

        }
        public Json::Value getConnectionsJson();
        public void setActiveConnection(unsigned int idx);
        public void setActiveConnection(std::string& _connstring);
        public std::shared_ptr<URI> getActiveConnection();
        public void removeConnection(unsigned int idx);
        public void start();
        public void stop();
        public bool isConnected() { return p_client->isConnected(); };
        public bool isRunning() { return m_running; };
        public int getCurrentEpoch();
        public double getCurrentDifficulty();
        public unsigned getConnectionSwitches();
        public unsigned getEpochChanges();


        private void rotateConnect();

        
        private void showMiningAt();

        private void setActiveConnectionCommon(unsigned int idx);

        private PoolSettings m_Settings;

        private void failovertimer_elapsed(const boost::system::error_code& ec);
    private void submithrtimer_elapsed(const boost::system::error_code& ec);
    private void reconnecttimer_elapsed(const boost::system::error_code& ec);


    }
}
