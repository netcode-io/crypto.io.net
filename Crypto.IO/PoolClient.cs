using System;

namespace CryptoPool.IO
{
    public class PoolClient
    {
        public class Session
        {
            // Tstamp of sessio start
            chrono::steady_clock::time_point start = chrono::steady_clock::now();
            // Whether or not worker is subscribed
            atomic<bool> subscribed = { false };
            // Whether or not worker is authorized
            atomic<bool> authorized = { false };
            // Total duration of session in minutes
            unsigned long duration()
            {
                return (chrono::duration_cast<chrono::minutes>(chrono::steady_clock::now() - start))
                    .count();
            }

            // EthereumStratum (1 and 2)

            // Extranonce currently active
            uint64_t extraNonce = 0;
            // Length of extranonce in bytes
            unsigned int extraNonceSizeBytes = 0;
            // Next work target
            h256 nextWorkBoundary =
                h256("0x00000000ffff0000000000000000000000000000000000000000000000000000");

            // EthereumStratum (2 only)
            bool firstMiningSet = false;
            unsigned int timeout = 30;  // Default to 30 seconds
            string sessionId = "";
            string workerId = "";
            string algo = "ethash";
            unsigned int epoch = 0;
            chrono::steady_clock::time_point lastTxStamp = chrono::steady_clock::now();
        }

        public virtual ~PoolClient() noexcept = default;

    // Sets the connection definition to be used by the client
    public void setConnection(std::shared_ptr<URI> _conn)
        {
            m_conn = _conn;
            m_conn->Responds(false);
        }

        // Gets a pointer to the currently active connection definition
        public std::shared_ptr<URI> getConnection() { return m_conn; }

        // Releases the pointer to the connection definition
        public void unsetConnection() { m_conn = nullptr; }

        public virtual void connect() = 0;
    public virtual void disconnect() = 0;
    public public virtual void submitHashrate(uint64_t const& rate, string const& id) = 0;
    public virtual void submitSolution(const Solution& solution) = 0;
    public virtual bool isConnected() { return m_connected.load(memory_order_relaxed); }
        public virtual bool isPendingState() { return false; }

        public virtual bool isSubscribed()
        {
            return (m_session ? m_session->subscribed.load(memory_order_relaxed) : false);
        }
        public virtual bool isAuthorized()
        {
            return (m_session ? m_session->authorized.load(memory_order_relaxed) : false);
        }

        public virtual string ActiveEndPoint()
        {
            return (m_connected.load(memory_order_relaxed) ? " [" + toString(m_endpoint) + "]" : "");
        }

        public using SolutionAccepted = function<void(chrono::milliseconds const&, unsigned const&, bool)>;
    public using SolutionRejected = function<void(chrono::milliseconds const&, unsigned const&)>;
    public using Disconnected = function<void()>;
    public using Connected = function<void()>;
    public using WorkReceived = function<void(WorkPackage const&)>;

    public void onSolutionAccepted(SolutionAccepted const& _handler) { m_onSolutionAccepted = _handler; }
    public void onSolutionRejected(SolutionRejected const& _handler) { m_onSolutionRejected = _handler; }
    public void onDisconnected(Disconnected const& _handler) { m_onDisconnected = _handler; }
    public void onConnected(Connected const& _handler) { m_onConnected = _handler; }
    public void onWorkReceived(WorkReceived const& _handler) { m_onWorkReceived = _handler; }



    protected unique_ptr<Session> m_session = nullptr;

    protected std::atomic<bool> m_connected = { false };  // This is related to socket ! Not session

    protected boost::asio::ip::basic_endpoint<boost::asio::ip::tcp> m_endpoint;

    protected std::shared_ptr<URI> m_conn = nullptr;

    protected SolutionAccepted m_onSolutionAccepted;
    protected SolutionRejected m_onSolutionRejected;
    protected Disconnected m_onDisconnected;
    protected Connected m_onConnected;
    protected WorkReceived m_onWorkReceived;
}
}
