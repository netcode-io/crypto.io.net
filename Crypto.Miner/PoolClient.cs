using System;
using System.Net.Sockets;

namespace Crypto.IO
{
    public abstract class PoolClient
    {
        protected Session _session = null;
        protected bool _connected = false; //: atomic // This is related to socket ! Not session
        protected TcpClient _endpoint;
        protected Uri _conn = null;
        protected Action<Farm, PoolClient, int, int, bool> _onSolutionAccepted;
        protected Action<Farm, PoolClient, int, int> _onSolutionRejected;
        protected Action<Farm, PoolClient> _onDisconnected;
        protected Action<Farm, PoolClient> _onConnected;
        protected Action<Farm, PoolClient, WorkPackage> _onWorkReceived;

        /// <summary>
        /// Session
        /// </summary>
        public class Session
        {
            /// <summary>
            /// Tstamp of session start.
            /// </summary>
            public DateTime Start = DateTime.Now;
            /// <summary>
            /// Whether or not worker is subscribed.
            /// </summary>
            public bool Subscribed = false; //: atomic
            /// <summary>
            /// Whether or not worker is authorized.
            /// </summary>
            public bool Authorized = false; //: atomic
            /// <summary>
            /// Total duration of session in minutes.
            /// </summary>
            /// <value>
            /// The duration.
            /// </value>
            double Duration => (DateTime.Now - Start).TotalMinutes;

            #region EthereumStratum (1 and 2)

            // Extranonce currently active
            public ulong ExtraNonce = 0;
            // Length of extranonce in bytes
            public int ExtraNonceSizeBytes = 0;
            // Next work target
            public H256 NextWorkBoundary = new H256("0x00000000ffff0000000000000000000000000000000000000000000000000000");

            // EthereumStratum (2 only)
            #region EthereumStratum (2 only)
            public bool FirstMiningSet = false;
            public int Timeout = 30;  // Default to 30 seconds
            public string SessionId = string.Empty;
            public string WorkerId = string.Empty;
            public string Algo = "ethash";
            public int Epoch = 0;
            public DateTime LastTxStamp = DateTime.Now;
            #endregion

            #endregion
        }

        /// <summary>
        /// Sets the connection definition to be used by the client.
        /// </summary>
        /// <param name="conn">The connection.</param>
        public void SetConnection(Uri conn) => _conn = conn;

        // Gets a pointer to the currently active connection definition
        public Uri Connection => _conn;

        /// <summary>
        /// Releases the pointer to the connection definition.
        /// </summary>
        public void UnsetConnection() => _conn = null;

        public abstract void Connect();
        public abstract void Disconnect();
        public abstract void SubmitHashrate(ulong rate, string id);
        public abstract void SubmitSolution(Solution solution);
        public virtual bool IsConnected => _connected; //: atomic
        public virtual bool IsPendingState => false;
        public virtual bool IsSubscribed => _session != null && _session.Subscribed; //: atomic
        public virtual bool IsAuthorized => _session != null && _session.Authorized; //: atomic

        public virtual string ActiveEndPoint => _connected ? $" [{_endpoint}]" : string.Empty; //: atomic

        public void OnSolutionAccepted(Action<Farm, PoolClient, int, int, bool> handler) => _onSolutionAccepted = handler;
        public void OnSolutionRejected(Action<Farm, PoolClient, int, int> handler) => _onSolutionRejected = handler;
        public void OnDisconnected(Action<Farm, PoolClient> handler) => _onDisconnected = handler;
        public void OnConnected(Action<Farm, PoolClient> handler) => _onConnected = handler;
        public void OnWorkReceived(Action<Farm, PoolClient, WorkPackage> handler) => _onWorkReceived = handler;
    }
}
