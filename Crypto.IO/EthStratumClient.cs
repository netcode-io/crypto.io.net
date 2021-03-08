using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// https://darchuk.net/2019/01/04/c-setting-socket-keep-alive/
// https://stackoverflow.com/questions/8375013/how-to-use-ssl-in-tcpclient-class
namespace Crypto.IO
{
    class StratumTcpClient : TcpClient
    {
        public StratumTcpClient()
        {
            NoDelay = true;
            ReceiveTimeout = 10000;
            SendTimeout = 10000;
        }

        public void SetOptions()
        {
            //Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            //Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true);
        }
    }

    public class EthStratumClient : PoolClient
    {
        const string ProjectNameWithVersion = "MyMiner 1.0";

        int _disconnecting = 0; //: atomic<bool>
        int _connecting = 0; //: atomic<bool>
        bool _authPending = false; //: atomic<bool>

        // seconds to trigger a work_timeout (overwritten in constructor)
        int _workTimeout;

        // seconds timeout for responses and connection (overwritten in constructor)
        int _responseTimeout;

        // default interval for workloop timer (milliseconds)
        int _workloopInterval = 1000;

        //WorkPackage _current;
        DateTime _current_Timestamp;

        StratumTcpClient _socket = null;

        Stream _stream;
        StreamReader _recvBuf;
        StreamWriter _sendBuf;
        Timer _workloopTimer;

        int _responsePleasCount = 0; //: atomic
        DateTime _responsePleaOlder; //: atomic
        Queue<DateTime> _responsePleaTimes = new Queue<DateTime>(64);

        int _txPending = 0; //: atomic
        Queue<string> _txQueue = new Queue<string>(64);

        Queue<IPEndPoint> _endpoints;

        int _solutionSubmittedMaxId;  // maximum json id we used to send a solution
        IFarm _f;

        public EthStratumClient(IFarm f, int workTimeout, int responseTimeout) : base()
        {
            _f = f;
            _workTimeout = workTimeout;
            _responseTimeout = responseTimeout;

            // Initialize workloop_timer to infinite wait
            _workloopTimer = new Timer(WorkloopTimer_Elapsed, null, Timeout.Infinite, Timeout.Infinite);
            ClearResponsePleas();
        }

        public override async Task ConnectAsync()
        {
            // Prevent unnecessary and potentially dangerous recursion
            if (_connecting != 0)
                return;
            Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(ConnectAsync)}() begin");

            // Start timing operations
            //_workloopTimer.Change(_workloopInterval, Timeout.Infinite);

            // Reset status flags
            _authPending = false; //: atomic

            // Initializes socket and eventually secure stream
            if (_socket == null)
                _socket = new StratumTcpClient();

            // Initialize a new queue of end points
            _endpoints = new Queue<IPEndPoint>();
            _endpoint = null;

            // Resolve all ips associated to hostname calling the resolver each time is useful as most load balancers will give Ips in different order
            if (_conn.HostNameType == UriHostNameType.Dns || _conn.HostNameType == UriHostNameType.Basic)
                try
                {
                    var result = await Dns.GetHostAddressesAsync(_conn.DnsSafeHost);
                    foreach (var dns in result)
                        _endpoints.Enqueue(new IPEndPoint(dns, _conn.Port));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not resolve host {_conn.Host}, {ex.Message}");

                    // Release locking flag and set connection status
                    _connecting = 0; //: atomic

                    // We "simulate" a disconnect, to ensure a fully shutdown state
                    DisconnectFinalize();
                    Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(ConnectAsync)}() end1");
                    return;
                }
            // No need to use the resolver if host is already an IP address
            else _endpoints.Enqueue(new IPEndPoint(IPAddress.Parse(_conn.Host), _conn.Port));

            await StartConnect();
            Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(ConnectAsync)}() end");
        }

        public override Task DisconnectAsync()
        {
            // Prevent unnecessary and potentially dangerous recursion
            if (Interlocked.CompareExchange(ref _disconnecting, 0, 1) != 1)
                return Task.CompletedTask;
            _connected = false; //: atomic

            Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(DisconnectAsync)}() begin");

            // Cancel any outstanding async operation
            //_socket?.Cancel();

            if (_socket != null && _socket.Connected)
                try
                {
                    //if (_conn.GetStratumSecLevel() != StratumSecLevel.None)
                    //{
                    //    // This will initiate the exchange of "close_notify" message among parties.
                    //    // If both client and server are connected then we expect the handler with success as there may be a connection issue we also endorse a timeout
                    //    OnSSLShutdownCompleted(null);
                    //    EnqueueResponsePlea();

                    //    // Rest of disconnection is performed asynchronously
                    //    Console.WriteLine("EthStratumClient::disconnect() end");
                    //    return;
                    //}
                    //else
                    _socket.Dispose();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error while disconnecting: {e.Message}");
                }

            DisconnectFinalize();
            Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(DisconnectAsync)}() end");
            return Task.CompletedTask;
        }

        void DisconnectFinalize()
        {
            _socket?.Dispose();

            // Release locking flag and set connection status
#if DEBUG
            //if (_logOptions & LOG_CONNECT)
            Console.WriteLine($"Socket disconnected from {ActiveEndPoint}");
#endif

            // Release session if exits
            if (_session != null)
                _conn.AddDuration(_session.Duration);
            _session = null;

            _authPending = false; //: atomic
            _disconnecting = 0; //: atomic
            _txPending = 0; //: atomic

            if (!_conn.IsUnrecoverable())
            {
                // If we got disconnected during autodetection phase reissue a connect lowering stratum mode checks
                // _canConnect flag is used to prevent never-ending loop when remote endpoint rejects connections attempts persistently since the first
                if (!_conn.StratumModeConfirmed() && _conn.Responds())
                {
                    // Repost a new connection attempt and advance to next stratum test
                    if (_conn.GetStratumMode() > 0)
                    {
                        _conn.SetStratumMode(_conn.GetStratumMode() - 1);
                        Task.Run(StartConnect);
                        return;
                    }
                    // There are no more stratum modes to test. Mark connection as unrecoverable and trash it
                    else _conn.MarkUnrecoverable();
                }
            }

            // Clear plea queue and stop timing
            ClearResponsePleas();
            _solutionSubmittedMaxId = 0;

            // Put the actor back to sleep
            _workloopTimer.Change(-1, -1);

            // Trigger handlers
            _onDisconnected?.Invoke(_f, this);
        }

        async Task StartConnect()
        {
            if (_connecting != 0) //: atomic
                return;
            _connecting = 1; //: atomic
            Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(StartConnect)}() begin");

            if (_endpoints.Count == 0)
            {
                _connecting = 0; //: atomic
                Console.WriteLine($"No more IP addresses to try for host: {_conn.Host}");

                // We "simulate" a disconnect, to ensure a fully shutdown state
                DisconnectFinalize();
                Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(StartConnect)}() end1");
                return;
            }

            // Pick the first endpoint in list. Eventually endpoints get discarded on connection errors.
            _endpoint = _endpoints.First();

            // Re-init socket if we need to
            if (_socket == null)
                _socket = new StratumTcpClient();

#if DEBUG
            //if (_logOptions & LOG_CONNECT)
            Console.WriteLine($"Trying {_endpoint} ...");
#endif

            ClearResponsePleas();
            _connecting = 1; //: atomic
            EnqueueResponsePlea();
            _solutionSubmittedMaxId = 0;

            // Start connecting async
            try
            {
                await _socket.ConnectAsync(_endpoint.Address, _endpoint.Port);

                // Set status completion
                _connecting = 0; //: atomic
            }
            catch (SocketException ec)
            {
                // Timeout has run before or we got error
                if (ec.SocketErrorCode == SocketError.TimedOut || !_socket.Connected)
                {
                    Console.WriteLine($"Error {_endpoint} [ {(ec.SocketErrorCode != SocketError.TimedOut ? ec.Message : "Timeout")} ]");

                    // We need to close the socket used in the previous connection attempt before starting a new one.
                    if (_socket.Connected)
                        _socket.Dispose();

                    // Discard this endpoint and try the next available. Eventually is start_connect which will check for an empty list.
                    _endpoints.Dequeue();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                    Task.Run(StartConnect);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

                    Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(StartConnect)}() end1");
                    return;
                }
            }

            // We got a socket connection established
            _conn.Responds(true);
            _connected = true; //: atomic

            // Clear txqueue
            _txQueue.Clear();

#if DEBUG
            //if (_logOptions & LOG_CONNECT)
            Console.WriteLine($"Socket connected to {ActiveEndPoint}");
#endif

            _socket.SetOptions();
            if (_conn.GetStratumSecLevel() != StratumSecLevel.None)
            {
                var sslStream = new SslStream(_socket.GetStream());
                try
                {
                    sslStream.AuthenticateAsClient(_conn.Host);
                    _stream = sslStream;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"SSL/TLS Handshake failed: {ex.Message}");
                    // This is a fatal error. No need to try other IPs as the certificate is based on host-name not ip address. Trying other IPs would end up with the very same error.
                    _conn.MarkUnrecoverable();
                    await DisconnectAsync();
                    Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(StartConnect)}() end2");
                    return;
                }
            }
            else
                _stream = _socket.GetStream();
            _recvBuf = new StreamReader(_stream);
            _sendBuf = new StreamWriter(_stream);

            // Clean buffer from any previous stale data
            ClearResponsePleas();

            /*
            If connection has been set-up with a specific scheme then set it's related stratum version as confirmed, otherwise let's go through an autodetection.

            Autodetection process passes all known stratum modes:
            - 1st pass EthStratumClient::ETHEREUMSTRATUM2 (3)
            - 2nd pass EthStratumClient::ETHEREUMSTRATUM  (2)
            - 3rd pass EthStratumClient::ETHPROXY         (1)
            - 4th pass EthStratumClient::STRATUM          (0)
            */
            if (_conn.GetStratumVersion() < StratumVersion.AutoDetect)
                _conn.SetStratumMode(_conn.GetStratumVersion(), true);
            else if (!_conn.StratumModeConfirmed() && _conn.GetStratumMode() == StratumVersion.AutoDetect)
                _conn.SetStratumMode(StratumVersion.EthereumStratum2, false);

            var (user, worker, pass) = _conn.StratumUserInfo();
            switch (_conn.GetStratumMode())
            {
                case StratumVersion.Stratum:
                    Send(new
                    {
                        id = 1U,
                        method = "mining.subscribe",
                        @params = new string[0],
                    }, "2.0");
                    break;
                case StratumVersion.EthProxy:
                    Send(new
                    {
                        id = 1U,
                        method = "eth_submitLogin",
                        worker,
                        @params = new string[] {
                            $"{user}{_conn.AbsolutePath}",
                            pass
                        }.ClampArray(),
                    }, "2.0");
                    break;
                case StratumVersion.EthereumStratum:
                    Send(new
                    {
                        id = 1U,
                        method = "mining.subscribe",
                        @params = new string[] {
                            ProjectNameWithVersion,
                            "EthereumStratum/1.0.0"
                        }
                    });
                    break;
                case StratumVersion.EthereumStratum2:
                    Send(new
                    {
                        id = 1U,
                        method = "mining.hello",
                        @params = new
                        {
                            agent = ProjectNameWithVersion,
                            host = _conn.Host,
                            port = $"{_conn.Port}:x",
                            proto = "EthereumStratum/2.0.0"
                        }
                    });
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(StratumVersion), _conn.GetStratumMode().ToString());
            }

            // Begin receive data
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(RecvSocketData);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            /*
            Send first message
            NOTE: It's been tested that f2pool.com does not respond with json error to wrong access message (which is needed to autodetect stratum mode).
            IT DOES NOT RESPOND AT ALL:
            Due to this we need to set a timeout (arbitrary set to 1 second) and if no response within that time consider the tentative login failed
            and switch to next stratum mode test
            */
            EnqueueResponsePlea();

            Console.WriteLine($"{nameof(EthStratumClient)}::{nameof(StartConnect)}() end");
        }

        static readonly JsonElement EmptyRequest = JsonDocument.Parse(@"{""id"":1,""result"":null,""error"":true}").RootElement;
        void WorkloopTimer_Elapsed(object ec)
        {
            // On timer cancelled or nothing to check for then early exit
            if (_conn == null) // ((string)ec == "operation_aborted") || 
                return;

            // No msg from client (EthereumStratum/2.0.0)
            if (_conn.GetStratumMode() == StratumVersion.EthereumStratum2 && _session != null)
            {
                // Send a message 5 seconds before expiration
                var s = (DateTime.Now - _session.LastTxStamp).TotalSeconds;
                if (s > _session.Timeout - 5)
                    Send(new
                    {
                        id = 7U,
                        method = "mining.noop"
                    });
            }

            if (_responsePleasCount != 0) //: atomic
            {
                var responseDelayMs = 0.0;
                var responsePleaTime = _responsePleaOlder; //: atomic

                // Check responses while in connection/disconnection phase
                if (IsPendingState)
                {
                    responseDelayMs = (DateTime.Now - responsePleaTime).TotalMilliseconds;

                    if ((_responseTimeout * 1000) >= responseDelayMs)
                    {
                        if (_connecting != 0) //: atomic
                        {
                            // The socket is closed so that any outstanding asynchronous connection operations are cancelled.
                            _socket.Dispose();
                            return;
                        }

                        // This is set for SSL disconnection
                        //if (_disconnecting != 0 && (_conn.StratumSecLevel() != StratumSecLevel.None))
                        //    if (_securesocket->lowest_layer().is_open())
                        //    {
                        //        _securesocket->lowest_layer().close();
                        //        return;
                        //    }
                    }
                }

                // Check responses while connected
                if (IsConnected)
                {
                    responseDelayMs = (DateTime.Now - responsePleaTime).TotalMilliseconds;

                    // Delay timeout to a request
                    if (responseDelayMs >= (_responseTimeout * 1000))
                    {
                        if (!_conn.StratumModeConfirmed() && !_conn.IsUnrecoverable())
                        {
                            // Waiting for a response from pool to a login request. Async self send a fake error response.
                            ClearResponsePleas();
                            Task.Run(() => ProcessResponse(EmptyRequest));
                        }
                        else
                        {
                            // Waiting for a response to solution submission
                            Console.WriteLine($"No response received in {_responseTimeout} seconds.");
                            _endpoints.Dequeue();
                            ClearResponsePleas();
                            Task.Run(DisconnectAsync);
                        }
                    }
                    // No work timeout
                    else if (_session != null && (DateTime.Now - _current_Timestamp).TotalSeconds > _workTimeout)
                    {
                        Console.WriteLine($"No new work received in {_workTimeout} seconds.");
                        _endpoints.Dequeue();
                        ClearResponsePleas();
                        Task.Run(DisconnectAsync);
                    }
                }
            }

            // Resubmit timing operations
            _workloopTimer.Change(_workloopInterval, Timeout.Infinite);
        }

        void StartSession()
        {
            // Start a new session of data
            _session = new Session();
            _current_Timestamp = DateTime.Now;

            // Invoke higher level handlers
            _onConnected?.Invoke(_f, this);
        }

        void ProcessExtranonce(string enonce)
        {
            _session.ExtraNonceSizeBytes = enonce.Length;
            Console.WriteLine($"Extranonce set to {Ansi.White}{enonce}{Ansi.Reset}");
            _session.ExtraNonce = ulong.Parse(enonce.PadRight(16, '0'));
        }

        object ProcessResponse(JsonElement res)
        {
            string ProcessError(JsonElement s)
            {
                switch (s.ValueKind)
                {
                    case JsonValueKind.Null: return null;
                    case JsonValueKind.String: return s.GetString();
                    case JsonValueKind.Array:
                    case JsonValueKind.Object:
                        var b = new StringBuilder();
                        if (s.ValueKind == JsonValueKind.Array)
                            foreach (var x in s.EnumerateArray())
                                b.Append($"{x.GetString()} ");
                        else
                            foreach (var x in s.EnumerateObject())
                                b.Append($"{x.Name}: {x.Value.GetString()} ");
                        if (b.Length > 0)
                            b.Length--;
                        return b.ToString();
                    default: return null;
                }
            }

            // Retrieve essential values
            var rpc = res.TryGetProperty("jsonrpc", out var z) ? z.GetString() ?? "2.0" : null;
            var id = res.TryGetProperty("id", out z) && z.ValueKind == JsonValueKind.Number ? z.GetUInt32() : 0; // This SHOULD be the same id as the request it is responding to (known exception is ethermine.org using 999)
            var errReason = res.TryGetProperty("error", out z) ? ProcessError(z) : null; // Content of the error reason
            var isSuccess = string.IsNullOrEmpty(errReason); // Whether or not this is a succesful or failed response (implies isNotification = false)
            var method = res.TryGetProperty("method", out z) ? z.GetString() : null; // The method of the notification (or request from pool)
            var params_ = res.TryGetProperty("params", out z) ? z : default;
            var result = res.TryGetProperty("result", out z) ? z : default;

            // Notifications of new jobs are like responses to get_work requests
            var notification = method != null || id == 0U; // Whether or not this message is a reply to previous request or is a broadcast notification
            if (notification && string.IsNullOrEmpty(method) && _conn.GetStratumMode() == StratumVersion.EthProxy && result.ValueKind == JsonValueKind.Array)
                method = "mining.notify";

            // Very minimal sanity checks
            // - For rpc2 member "jsonrpc" MUST be valued to "2.0"
            // - For responses ... well ... whatever
            // - For notifications I must receive "method" member and a not empty "params" or "result" member
            if ((rpc != null && rpc != "2.0") || (notification && params_.ValueKind == JsonValueKind.Undefined && result.ValueKind == JsonValueKind.Undefined))
            {
                Console.WriteLine("Pool sent an invalid jsonrpc message...");
                Console.WriteLine("Ask pool devs to honor http://www.jsonrpc.org/ specifications.");
                Console.WriteLine("Disconnecting...");
                Task.Run(DisconnectAsync);
                return null;
            }

            // Handle awaited responses to OUR requests (calc response times)
            if (!notification)
            {
                double responseDelayMs;
                switch (id)
                {
                    case 1U:
                        // This is the response to very first message after connection.
                        // Message request vary upon stratum flavour
                        // I wish I could manage to have different Ids but apparently ethermine.org always replies to first message with id=1 regardless the id originally sent.
                        responseDelayMs = DequeueResponsePlea();

                        // If we're in autodetection phase an error message (of any kind) means the selected stratum flavour does not comply with the one implemented by the
                        // work provider (the pool) : thus exit, disconnect and try another one
                        if (!isSuccess && !_conn.StratumModeConfirmed())
                        {
                            switch (_conn.GetStratumMode())
                            {
                                case StratumVersion.EthereumStratum2: Console.WriteLine("Negotiation of EthereumStratum/2.0.0 failed. Trying another ..."); break;
                                case StratumVersion.EthereumStratum: Console.WriteLine("Negotiation of EthereumStratum/1.0.0 failed. Trying another ..."); break;
                                case StratumVersion.EthProxy: Console.WriteLine("Negotiation of Eth-Proxy compatible failed. Trying another ..."); break;
                                case StratumVersion.Stratum: Console.WriteLine("Negotiation of Stratum failed."); break;
                                default: throw new ArgumentOutOfRangeException(nameof(_conn));
                            }
                            // Disconnect and Proceed with next step of autodetection
                            Task.Run(DisconnectAsync);
                            break;
                        }

                        /*
                        Process response for each stratum flavour:
                        ETHEREUMSTRATUM2 response to mining.hello
                        ETHEREUMSTRATUM  response to mining.subscribe
                        ETHPROXY         response to eth_submitLogin
                        STRATUM          response to mining.subscribe
                        */
                        switch (_conn.GetStratumMode())
                        {
                            case StratumVersion.EthereumStratum2:
                                isSuccess = result.ValueKind == JsonValueKind.Object &&
                                    result.TryGetProperty("proto", out z) && z.GetString() == "EthereumStratum/2.0.0" &&
                                    result.TryGetProperty("encoding", out z) && z.TryGetProperty("resume", out z) &&
                                    result.TryGetProperty("timeout", out z) && z.TryGetProperty("maxerrors", out z) &&
                                    result.TryGetProperty("node", out z);
                                if (!isSuccess)
                                {

                                    // If no autodetection the connection is not usable with this stratum flavor
                                    if (_conn.StratumModeConfirmed())
                                    {
                                        _conn.MarkUnrecoverable();
                                        Console.WriteLine("Negotiation of EthereumStratum/2.0.0 failed. Change your connection parameters");
                                    }
                                    else
                                        Console.WriteLine("Negotiation of EthereumStratum/2.0.0 failed. Trying another ...");
                                    // Disconnect
                                    Task.Run(DisconnectAsync);
                                    break;
                                }

                                // Selected flavour is confirmed
                                _conn.SetStratumMode(StratumVersion.EthereumStratum2, true);
                                Console.WriteLine("Stratum mode : EthereumStratum/2.0.0");
                                StartSession();

                                // Send request for subscription
                                Send(new
                                {
                                    id = 2U,
                                    method = "mining.subscribe",
                                });
                                EnqueueResponsePlea();
                                break;

                            case StratumVersion.EthereumStratum:
                                isSuccess = result.ValueKind == JsonValueKind.Array && result[0].ValueKind == JsonValueKind.Array &&
                                    result[0].GetArrayLength() == 3 && result[0][2].GetString() == "EthereumStratum/1.0.0";
                                if (!isSuccess)
                                {
                                    // If no autodetection the connection is not usable with this stratum flavor
                                    if (_conn.StratumModeConfirmed())
                                    {
                                        _conn.MarkUnrecoverable();
                                        Console.WriteLine("Negotiation of EthereumStratum/1.0.0 (NiceHash) failed. Change your  connection parameters");
                                    }
                                    else Console.WriteLine("Negotiation of EthereumStratum/1.0.0 (NiceHash) failed. Trying another ...");
                                    // Disconnect
                                    Task.Run(DisconnectAsync);
                                    break;
                                }

                                // Selected flavour is confirmed
                                _conn.SetStratumMode(StratumVersion.EthereumStratum, true);
                                Console.WriteLine("Stratum mode : EthereumStratum/1.0.0 (NiceHash)");
                                StartSession();
                                _session.Subscribed = true; //: atomic

                                // Notify we're ready for extra nonce subscribtion on the fly reply to this message should not perform any logic
                                Send(new
                                {
                                    id = 2U,
                                    method = "mining.extranonce.subscribe",
                                    @params = new string[0],
                                });

                                var enonce = result[0][1].GetString();
                                if (!string.IsNullOrEmpty(enonce)) ProcessExtranonce(enonce);

                                // Eventually request authorization
                                _authPending = true; //: atomic
                                var (user, worker, pass) = _conn.StratumUserInfo();
                                Send(new
                                {
                                    id = 3U,
                                    method = "mining.authorize",
                                    @params = new string[] { $"{user}.{worker}{_conn.AbsolutePath}", pass },
                                });
                                EnqueueResponsePlea();
                                break;

                            case StratumVersion.EthProxy:
                                if (!isSuccess)
                                {
                                    // If no autodetection the connection is not usable with this stratum flavor
                                    if (_conn.StratumModeConfirmed())
                                    {
                                        _conn.MarkUnrecoverable();
                                        Console.WriteLine("Negotiation of Eth-Proxy compatible failed. Change your connection parameters");
                                    }
                                    else Console.WriteLine("Negotiation of Eth-Proxy compatible failed. Trying another ...");
                                    // Disconnect
                                    Task.Run(DisconnectAsync);
                                    break;
                                }

                                // Selected flavour is confirmed
                                _conn.SetStratumMode(StratumVersion.EthProxy, true);
                                Console.WriteLine("Stratum mode : Eth-Proxy compatible");
                                StartSession();
                                _session.Subscribed = true; //: atomic;
                                _session.Authorized = true; //: atomic;

                                // Request initial work
                                Send(new
                                {
                                    id = 5U,
                                    method = "eth_getWork",
                                    @params = new string[0],
                                });
                                break;

                            case StratumVersion.Stratum:
                                if (!isSuccess)
                                {
                                    // If no autodetection the connection is not usable with this stratum flavor
                                    if (_conn.StratumModeConfirmed())
                                    {
                                        _conn.MarkUnrecoverable();
                                        Console.WriteLine("Negotiation of Eth-Proxy compatible failed. Change your connection parameters");
                                    }
                                    else Console.WriteLine("Negotiation of Eth-Proxy compatible failed. Trying another ...");
                                    // Disconnect
                                    Task.Run(DisconnectAsync);
                                    break;
                                }

                                // Selected flavour is confirmed
                                _conn.SetStratumMode(StratumVersion.Stratum, true);
                                Console.WriteLine("Stratum mode : Stratum");
                                StartSession();
                                _session.Subscribed = true; //: atomic

                                // Request authorization
                                _authPending = true; //: atomic
                                (user, worker, pass) = _conn.StratumUserInfo();
                                Send(new
                                {
                                    id = 3U,
                                    method = "mining.authorize",
                                    @params = new string[] { $"{user}.{worker}{_conn.AbsolutePath}", pass },
                                }, "2.0");
                                EnqueueResponsePlea();
                                break;

                            default: throw new ArgumentOutOfRangeException(nameof(_conn));
                        }
                        break;

                    case 2U:
                        // For EthereumStratum/1.0.0
                        // This is the response to mining.extranonce.subscribe according to this https://github.com/nicehash/Specifications/blob/master/NiceHash_extranonce_subscribe_extension.txt
                        // In all cases, client does not perform any logic when receiving back these replies.
                        // With mining.extranonce.subscribe subscription, client should handle extranonce1 changes correctly
                        // Nothing to do here.

                        // For EthereumStratum/2.0.0
                        // This is the response to mining.subscribe https://github.com/AndreaLanfranchi/EthereumStratum-2.0.0#session-handling---response-to-subscription
                        if (_conn.GetStratumMode() == StratumVersion.EthereumStratum2)
                        {
                            responseDelayMs = DequeueResponsePlea();
                            if (result.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(result.GetString()))
                            {
                                // Got invalid session id which is mandatory
                                Console.WriteLine("Got invalid or missing session id. Disconnecting ... ");
                                _conn.MarkUnrecoverable();
                                Task.Run(DisconnectAsync);
                                break;
                            }

                            _session.SessionId = result.GetString();
                            _session.Subscribed = true; //: atomic

                            // Request authorization
                            _authPending = true;
                            var (user, worker, pass) = _conn.StratumUserInfo();
                            Send(new
                            {
                                id = 3U,
                                method = "mining.authorize",
                                @params = new string[] { $"{user}.{worker}{_conn.AbsolutePath}", pass },
                            });
                            EnqueueResponsePlea();
                        }
                        break;

                    case 3U:
                        responseDelayMs = DequeueResponsePlea();
                        if (_conn.GetStratumMode() == StratumVersion.EthereumStratum2)
                        {
                            // Response to "mining.authorize" (https://en.bitcoin.it/wiki/Stratum_mining_protocol#mining.authorize)
                            // Result should be boolean, some pools also throw an error, so isSuccess can be false Due to this reevaluate isSuccess
                            if (isSuccess && result.ValueKind == JsonValueKind.False)
                                isSuccess = false;

                            _authPending = false; //: atomic
                            _session.Authorized = isSuccess; //: atomic

                            if (!IsAuthorized)
                            {
                                Console.WriteLine($"Worker {Ansi.White}{_conn.UserInfo}{Ansi.Reset} not authorized : {errReason}");
                                _conn.MarkUnrecoverable();
                                Task.Run(DisconnectAsync);
                                break;
                            }
                            else Console.WriteLine($"Authorized worker {_conn.UserInfo}");
                        }
                        else
                        {
                            if (!isSuccess || result.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(result.GetString()))
                            {
                                // Got invalid session id which is mandatory
                                Console.WriteLine($"Worker {Ansi.White}{_conn.UserInfo}{Ansi.Reset} not authorized : {errReason}");
                                _conn.MarkUnrecoverable();
                                Task.Run(DisconnectAsync);
                                break;
                            }
                            _authPending = false; //: atomic
                            _session.Authorized = true; //: atomic
                            _session.WorkerId = result.GetString();
                            Console.WriteLine($"Authorized worker {_conn.UserInfo}");
                            // Nothing else to here. Wait for notifications from pool
                        }
                        break;

                    case 5U:
                        // This is the response we get on first get_work request issued
                        // in mode EthStratumClient::ETHPROXY thus we change it to a mining.notify notification
                        if (_conn.GetStratumMode() == StratumVersion.EthProxy && result.ValueKind == JsonValueKind.Array)
                        {
                            method = "mining.notify";
                            notification = true;
                            break;
                        }
                        break;

                    case 9U:
                        // Response to hashrate submit
                        // Shall we do anything ? Hashrate submit is actually out of stratum spec
                        if (!isSuccess)
                            Console.WriteLine($"Submit hashRate failed : {errReason ?? "Unspecified error"}");
                        break;

                    case uint n when (n >= 40 && n <= _solutionSubmittedMaxId):
                        responseDelayMs = DequeueResponsePlea();
                        if (_conn.GetStratumMode() == StratumVersion.EthereumStratum2)
                        {
                            // In EthereumStratum/2.0.0 we can evaluate the severity of the error.
                            // An 2xx error means the solution have been accepted but is likely stale
                            var isStale = false;
                            if (!isSuccess)
                            {
                                var errCode = res.GetProperty("error").TryGetProperty("code", out z) ? z.GetString() : null;
                                if (errCode != null && errCode.StartsWith("2"))
                                    isSuccess = isStale = true;
                            }

                            var minerIndex = (int)(id - 40);
                            if (isSuccess)
                                _onSolutionAccepted?.Invoke(_f, this, responseDelayMs, minerIndex, isStale);
                            else
                            {
                                Console.WriteLine($"Reject reason : {errReason ?? "Unspecified"}");
                                _onSolutionRejected?.Invoke(_f, this, responseDelayMs, minerIndex);
                            }
                        }
                        else
                        {
                            // Response to solution submission mining.submit (https://en.bitcoin.it/wiki/Stratum_mining_protocol#mining.submit)
                            // Result should be boolean, some pools also throw an error, so _isSuccess can be false Due to this reevaluate _isSucess
                            if (isSuccess && result.ValueKind == JsonValueKind.False)
                                isSuccess = false;

                            var minerIndex = (int)(id - 40);
                            if (isSuccess)
                                _onSolutionAccepted?.Invoke(_f, this, responseDelayMs, minerIndex, false);
                            else
                            {
                                Console.WriteLine($"Reject reason : {errReason ?? "Unspecified"}");
                                _onSolutionRejected?.Invoke(_f, this, responseDelayMs, minerIndex);
                            }
                        }
                        break;

                    case 999U:
                        if (isSuccess)
                            break;
                        // This unfortunate case should not happen as none of the outgoing requests is marked with id 999 However it has been tested that ethermine.org responds with this id when
                        // error replying to either mining.subscribe (1) or mining.authorize requests (3) To properly handle this situation we need to rely on Subscribed/Authorized states
                        if (!_conn.StratumModeConfirmed())
                            switch (_conn.GetStratumMode())
                            {
                                case StratumVersion.EthereumStratum2: Console.WriteLine("Negotiation of EthereumStratum/2.0.0 failed. Trying another ..."); break;
                                case StratumVersion.EthereumStratum: Console.WriteLine("Negotiation of EthereumStratum/1.0.0 failed. Trying another ..."); break;
                                case StratumVersion.EthProxy: Console.WriteLine("Negotiation of Eth-Proxy compatible failed. Trying another ..."); break;
                                case StratumVersion.Stratum: Console.WriteLine("Negotiation of Stratum failed."); break;
                                default: throw new ArgumentOutOfRangeException(nameof(_conn));
                            }
                        // Subscription pending
                        else if (!IsSubscribed)
                            Console.WriteLine($"Subscription failed : {errReason ?? "Unspecified error"}");
                        // Authorization pending
                        else if (IsSubscribed && !IsAuthorized)
                            Console.WriteLine($"Worker not authorized : {errReason ?? "Unspecified error"}");
                        // Disconnect and Proceed with next step of autodetection
                        Task.Run(DisconnectAsync);
                        break;

                    default:
                        Console.WriteLine($"Got response for unknown message id [{id}] Discarding...");
                        break;
                }
                return null;
            }

            // note: Do not process any notification unless login validated which means we have detected proper stratum mode.
            if (!_conn.StratumModeConfirmed())
                return null;

            switch (method)
            {
                case "mining.notify":
                    if (_conn.GetStratumMode() == StratumVersion.EthereumStratum2)
                    {
                        /*
                        {
                            "method": "mining.notify",
                            "params": [
                                "bf0488aa",
                                "6526d5"
                                "645cf20198c2f3861e947d4f67e3ab63b7b2e24dcc9095bd9123e7b33371f6cc",
                                "0"
                            ]
                        }
                        */
                        if (_session == null || !_session.FirstMiningSet)
                        {
                            Console.WriteLine("Got mining.notify before mining.set message. Discarding ...");
                            break;
                        }

                        if (params_.ValueKind != JsonValueKind.Array || params_.GetArrayLength() != 4)
                        {
                            Console.WriteLine("Got invalid mining.notify message. Discarding ...");
                            break;
                        }

                        return new WorkPackage
                        {
                            Job = params_[0].GetString(),
                            Block = int.Parse(params_[1].GetString(), NumberStyles.HexNumber),
                            Header = new H256($"0x{params_[1].GetString().PadLeft(64, '0')}"),
                            Boundary = _session.NextWorkBoundary,
                            Epoch = _session.Epoch,
                            Algo = _session.Algo,
                            StartNonce = _session.ExtraNonce,
                            ExSizeBytes = _session.ExtraNonceSizeBytes
                        };
                    }
                    else
                    {
                        // Discard jobs if not properly subscribed or if a job for this transmission has already been processed
                        if (!IsSubscribed) // || _current != null)
                            break;

                        // Workaround for Nanopool wrong implementation see issue # 1348
                        var prmIdx = 1;
                        if (_conn.GetStratumMode() == StratumVersion.EthProxy && result.ValueKind != JsonValueKind.Undefined)
                        {
                            prmIdx = 0;
                            params_ = result;
                        }

                        if (params_.ValueKind != JsonValueKind.Array || params_.GetArrayLength() == 0)
                        {
                            Console.WriteLine("Got invalid mining.notify message. Discarding ...");
                            break;
                        }

                        if (_conn.GetStratumMode() == StratumVersion.EthereumStratum)
                        {
                            var seedHash = params_[1].GetString();
                            var headerHash = params_[2].GetString();
                            if (!string.IsNullOrEmpty(headerHash) && !string.IsNullOrEmpty(seedHash))
                                return new WorkPackage
                                {
                                    Job = params_[0].GetString(),
                                    Seed = new H256(seedHash),
                                    Header = new H256(headerHash),
                                    Boundary = _session.NextWorkBoundary,
                                    StartNonce = _session.ExtraNonce,
                                    ExSizeBytes = _session.ExtraNonceSizeBytes,
                                    Block = -1
                                };
                        }
                        else
                        {
                            var headerHash = params_[prmIdx++].GetString();
                            var seedHash = params_[prmIdx++].GetString();
                            var shareTarget = params_[prmIdx++].GetString();
                            // Only some eth-proxy compatible implementations carry the block number namely ethermine.org
                            var block = -1;
                            if (_conn.GetStratumMode() == StratumVersion.EthProxy && params_.GetArrayLength() > prmIdx && params_[prmIdx].GetString().StartsWith("0x"))
                                try
                                {
                                    block = int.Parse(params_[prmIdx].GetString(), NumberStyles.HexNumber);
                                    /*
                                    check if the block number is in a valid range
                                    A year has ~31536000 seconds
                                    50 years have ~1576800000
                                    assuming a (very fast) blocktime of 10s:
                                    ==> in 50 years we get 157680000 (=0x9660180) blocks
                                    */
                                    if (block > 0x9660180)
                                        throw new Exception();
                                }
                                catch
                                {
                                    block = -1;
                                }
                            // coinmine.pl fix
                            var l = shareTarget.Length;
                            if (l < 66)
                                shareTarget = $"0x{shareTarget.Substring(2).PadLeft(66, '0')}";
                            return new WorkPackage
                            {
                                Job = params_[0].GetString(),
                                Seed = new H256(seedHash),
                                Header = new H256(headerHash),
                                Boundary = new H256(shareTarget)
                            };
                        }
                    }
                    break;

                case "mining.set_target":
                    if (_conn.GetStratumMode() != StratumVersion.Stratum)
                        goto case null;
                    if (params_.ValueKind == JsonValueKind.Array)
                    {
                        var target2 = params_[0].GetString();
                        target2 = $"0x{target2.PadLeft(64, '0')}";
                        _session.NextWorkBoundary = new H256(target2);
                    }
                    break;

                case "mining.set_difficulty":
                    if (_conn.GetStratumMode() != StratumVersion.EthereumStratum)
                        goto case null;
                    if (params_.ValueKind == JsonValueKind.Array)
                    {
                        var nextWorkDifficulty = Math.Max(params_[0].GetDouble(), 0.0001);
                        _session.NextWorkBoundary = new H256(nextWorkDifficulty.ToTargetFromDiff());
                    }
                    break;

                case "mining.set_extranonce":
                    if (_conn.GetStratumMode() != StratumVersion.EthereumStratum)
                        goto case null;
                    if (params_.ValueKind == JsonValueKind.Array)
                    {
                        var enonce = params_[0].GetString();
                        if (!string.IsNullOrEmpty(enonce))
                            ProcessExtranonce(enonce);
                    }
                    break;

                case "mining.set":
                    if (_conn.GetStratumMode() != StratumVersion.EthereumStratum2)
                        goto case null;
                    /*
                    {
                        "method": "mining.set",
                        "params": {
                            "epoch" : "dc",
                            "target" : "0112e0be826d694b2e62d01511f12a6061fbaec8bc02357593e70e52ba",
                            "algo" : "ethash",
                            "extranonce" : "af4c"
                        }
                    }
                    */
                    if (params_.ValueKind != JsonValueKind.Object || !params_.EnumerateObject().Any())
                    {
                        Console.WriteLine("Got invalid mining.set message. Discarding ...");
                        break;
                    }
                    _session.FirstMiningSet = true;
                    var timeout = params_.TryGetProperty("timeout", out z) ? z.GetString() : null;
                    var epoch = params_.TryGetProperty("epoch", out z) ? z.GetString() : null;
                    var target = params_.TryGetProperty("target", out z) ? z.GetString() : null;
                    if (!string.IsNullOrEmpty(timeout))
                        _session.Timeout = int.Parse(timeout, NumberStyles.HexNumber);
                    if (!string.IsNullOrEmpty(epoch))
                        _session.Epoch = int.Parse(epoch, NumberStyles.HexNumber);
                    if (!string.IsNullOrEmpty(target))
                    {
                        target = $"0x{target.PadLeft(64, '0')}";
                        _session.NextWorkBoundary = new H256(target);
                    }
                    _session.Algo = params_.TryGetProperty("algo", out z) ? z.GetString() : "ethash";
                    {
                        var enonce = params_.TryGetProperty("extranonce", out z) ? z.GetString() : null;
                        if (!string.IsNullOrEmpty(enonce))
                            ProcessExtranonce(enonce);
                    }
                    break;

                case "mining.bye":
                    if (_conn.GetStratumMode() != StratumVersion.EthereumStratum2)
                        goto case null;
                    Console.WriteLine($"{_conn.Host} requested connection close. Disconnecting ...");
                    Task.Run(DisconnectAsync);
                    break;

                case "client.get_version":
                    if (rpc == null)
                        Send(new
                        {
                            id,
                            result = ProjectNameWithVersion,
                            error = (string)null,
                        }, rpc);
                    else
                        Send(new
                        {
                            id,
                            result = ProjectNameWithVersion,
                        }, rpc);
                    break;

                case null:
                default:
                    Console.WriteLine($"Got unknown method [{method}] from pool. Discarding...");
                    // Respond back to issuer
                    Send(new
                    {
                        id,
                        error = "Method not found",
                    }, rpc);
                    break;
            }
            return null;
        }

        public override void SubmitHashrate(ulong rate, string id)
        {
            if (!IsConnected)
                return;

            var (_, worker, _) = _conn.StratumUserInfo();
            if (_conn.GetStratumMode() != StratumVersion.EthereumStratum2)
                // There is no stratum method to submit the hashrate so we use the rpc variant.
                // Note !!
                // id = 6 is also the id used by ethermine.org and nanopool to push new jobs
                // thus we will be in trouble if we want to check the result of hashrate submission actually change the id from 6 to 9
                Send(new
                {
                    id = 9U,
                    worker,
                    method = "eth_submitHashrate",
                    @params = new string[] {
                        $"0x{rate:x}",
                        id
                    }.ClampArray()
                }, "2.0");
            else
                Send(new
                {
                    id = 9U,
                    method = "mining.hashrate",
                    @params = new string[] {
                        $"{rate:x}",
                        _session.WorkerId
                    }
                });
        }

        public override void SubmitSolution(Solution solution)
        {
            if (!IsAuthorized)
            {
                Console.WriteLine("Solution not submitted. Not authorized.");
                return;
            }

            var (user, worker, pass) = _conn.StratumUserInfo();
            switch (_conn.GetStratumMode())
            {
                case StratumVersion.Stratum:
                    Send(new
                    {
                        id = 40 + solution.MIdx,
                        method = "mining.submit",
                        @parms = new string[]
                        {
                            user,
                            solution.Work.Job,
                            $"0x{solution.Nonce:x}",
                            $"0x{solution.Work.Header:x}",
                            $"0x{solution.MixHash:x}",
                            worker,
                        }.ClampArray()
                    }, "2.0");
                    break;

                case StratumVersion.EthProxy:
                    Send(new
                    {
                        id = 40 + solution.MIdx,
                        method = "eth_submitWork",
                        @parms = new string[]
                        {
                            $"0x{solution.Nonce:x}",
                            $"0x{solution.Work.Header:x}",
                            $"0x{solution.MixHash:x}",
                            worker,
                         }.ClampArray()
                    });
                    break;

                case StratumVersion.EthereumStratum:
                    Send(new
                    {
                        id = 40 + solution.MIdx,
                        method = "mining.submit",
                        @parms = new string[]
                        {
                            worker != null ? $"{user}.{worker}" : user,
                            solution.Work.Job,
                            $"{solution.Nonce:x}".Substring(solution.Work.ExSizeBytes),
                         }
                    });
                    break;

                case StratumVersion.EthereumStratum2:
                    Send(new
                    {
                        id = 40 + solution.MIdx,
                        method = "mining.submit",
                        @parms = new string[]
                        {
                            solution.Work.Job,
                            $"{solution.Nonce:x}".Substring(solution.Work.ExSizeBytes),
                            _session.WorkerId
                         }.ClampArray()
                    });
                    break;

                default: throw new ArgumentOutOfRangeException(nameof(StratumVersion), _conn.GetStratumMode().ToString());
            }
            EnqueueResponsePlea();
        }

        #region Send / Recieve

        void Send(object req, string rpc = null)
        {
            var line = req is string z ? z : JsonSerializer.Serialize(req);
            if (rpc != null)
                line = $"{line.Substring(0, line.Length - 1)},\"jsonrpc\":\"{rpc}\"}}";
            _txQueue.Enqueue(line);
            if (Interlocked.CompareExchange(ref _txPending, 0, 1) != 1)
                Task.Run(SendSocketData);
        }

        void SendSocketData()
        {
            if (!IsConnected || _txQueue.Count == 0)
            {
                _txQueue.Clear();
                _txPending = 0; //: atomic
                return;
            }

            while (_txQueue.Count > 0)
            {
                var line = _txQueue.Dequeue();

                //if (_logOptions & LOG_JSON)
                Console.WriteLine($" >> {line}");

                try
                {
                    _sendBuf.Write(line);
                    _sendBuf.Write('\n');
                    _sendBuf.Flush();
                }
                catch (Exception ex)
                {
                    //_sendBuffer.consume(_sendBuffer.capacity());
                    _txQueue.Clear();

                    _txPending = 0; //: atomic

                    //if (ex is IOException && SSL_R_PROTOCOL_IS_SHUTDOWN == ERR_GET_REASON(ec.value())))
                    //{
                    //    Console.WriteLine($"SSL Stream error : {ex.Message}");
                    //    Task.Run(DisconnectAsync);
                    //}

                    if (IsConnected)
                    {
                        Console.WriteLine($"Socket write failed : {ex.Message}");
                        Task.Run(DisconnectAsync);
                    }
                    return;
                }
            }

            // Register last transmission tstamp to prevent timeout in EthereumStratum/2.0.0
            if (_session != null && _conn.GetStratumMode() == StratumVersion.EthereumStratum2)
                _session.LastTxStamp = DateTime.Now;

            if (_txQueue.Count == 0) _txPending = 0; //: atomic
            else SendSocketData();
        }

        Task RecvSocketData()
        {
            while (IsConnected)
            {
                //catch (Exception ec)
                //{
                //    if (IsConnected)
                //    {
                //        if (_authPending != false) //: atomic
                //        {
                //            Console.WriteLine("Error while waiting for authorization from pool");
                //            Console.WriteLine("Double check your pool credentials.");
                //            _conn.MarkUnrecoverable();
                //        }
                //        //if ((ex.category() == boost::asio::error::get_ssl_category()) && (ERR_GET_REASON(ec.value()) == SSL_RECEIVED_SHUTDOWN))
                //        //    Console.WriteLine($"SSL Stream remotely closed by {_conn.Host}");
                //        //else if (ex == boost::asio::error::eof)
                //        //    Console.WriteLine($"Connection remotely closed by {_conn.Host}");
                //        //else
                //        Console.WriteLine($"Socket read failed: {ec.Message}");
                //        Task.Run(DisconnectAsync);
                //    }
                //    return;
                //}

                // Process each line in the transmission
                // NOTE : as multiple jobs may come in with a single transmission only the last will be dispatched
                var line = _recvBuf.ReadLine().Trim();
                while (line != null)
                {
                    if (line.Length == 0)
                        continue;

                    // Out received message only for debug purpouses
                    //if (_logOptions & LOG_JSON)
                    Console.WriteLine($" << {line}");

                    // Test validity of chunk and process
                    try
                    {
                        var msg = JsonDocument.Parse(line).RootElement;
                        var res = ProcessResponse(msg);
                        if (res is WorkPackage current)
                        {
                            // There is a new job - dispatch it
                            _current_Timestamp = DateTime.Now;
                            _onWorkReceived?.Invoke(_f, this, current);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Stratum got invalid Json message : {ex.Message}");
                    }
                    line = _recvBuf.ReadLine().Trim();
                }
            }
            return Task.CompletedTask;
        }

        #endregion

        //void OnSSLShutdownCompleted(Exception ex)
        //{
        //    ClearResponsePleas();
        //    Task.Run(DisconnectFinalize);
        //}

        void EnqueueResponsePlea()
        {
            var responsePleaTime = DateTime.Now;
            if (_responsePleasCount++ == 0)
                _responsePleaOlder = responsePleaTime; //: atomic
            _responsePleaTimes.Enqueue(responsePleaTime);
        }

        double DequeueResponsePlea()
        {
            var responsePleaTime = _responsePleaOlder; //: atomic
            var responseDelayMs = (DateTime.Now - responsePleaTime).TotalMilliseconds;
            if ((responsePleaTime = _responsePleaTimes.Dequeue()) != default)
                _responsePleaOlder = responsePleaTime;
            if (_responsePleasCount > 0)
            {
                _responsePleasCount--;
                return responseDelayMs;
            }
            return 0.0;
        }

        void ClearResponsePleas()
        {
            _responsePleasCount = 0; //: atomic
            _responsePleaTimes.Clear();
            _responsePleaOlder = DateTime.Now; //: atomic
        }
    }
}