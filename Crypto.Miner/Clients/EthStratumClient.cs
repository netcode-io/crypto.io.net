using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Crypto.IO
{
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

        WorkPackage _current;
        DateTime _currentTimestamp;

        TcpClient _socket = null;
        string _message;  // The internal message string buffer
        bool _newJobProcessed = false;

        Stream _stream;
        Timer _workloopTimer;

        int _responsePleasCount = 0; //: atomic
        DateTime _responsePleaOlder; //: atomic
        Queue<DateTime> _responsePleaTimes = new Queue<DateTime>(64);

        int _txPending = 0; //: atomic
        Queue<string> _txQueue = new Queue<string>(64);

        Queue<IPEndPoint> _endpoints;

        int _solutionSubmittedMaxId;  // maximum json id we used to send a solution

        public EthStratumClient(int workTimeout, int responseTimeout) : base()
        {
            _workTimeout = workTimeout;
            _responseTimeout = responseTimeout;

            // Initialize workloop_timer to infinite wait
            _workloopTimer = new Timer(WorkloopTimer_Elapsed, null, -1, -1);
            ClearResponsePleas();
        }

        public void InitSocket()
        {
            //            // Prepare Socket
            //            var secLevel = _conn.StratumSecLevel();
            //            if (secLevel != StratumSecLevel.None)
            //            {
            //                boost::asio::ssl::context::method method = boost::asio::ssl::context::tls_client;
            //                if (secLevel == StratumSecLevel.TLS12)
            //                    method = boost::asio::ssl::context::tlsv12;

            //                boost::asio::ssl::context ctx(method);
            //                m_securesocket = std::make_shared<boost::asio::ssl::stream<boost::asio::ip::tcp::socket>>(
            //                    m_io_service, ctx);
            //                m_socket = &m_securesocket->next_layer();

            //                if (getenv("SSL_NOVERIFY"))
            //                {
            //                    _secureSocket->set_verify_mode(boost::asio::ssl::verify_none);
            //                }
            //                else
            //                {
            //                    m_securesocket->set_verify_mode(boost::asio::ssl::verify_peer);
            //                    m_securesocket->set_verify_callback(make_verbose_verification(boost::asio::ssl::rfc2818_verification(m_conn->Host())));
            //                }
            //# ifdef _WIN32
            //                HCERTSTORE hStore = CertOpenSystemStore(0, "ROOT");
            //                if (hStore == nullptr)
            //                {
            //                    return;
            //                }

            //                X509_STORE* store = X509_STORE_new();
            //                PCCERT_CONTEXT pContext = nullptr;
            //                while ((pContext = CertEnumCertificatesInStore(hStore, pContext)) != nullptr)
            //                {
            //                    X509* x509 = d2i_X509(nullptr, (const unsigned char**)&pContext->pbCertEncoded, pContext->cbCertEncoded);
            //                    if (x509 != nullptr)
            //                    {
            //                        X509_STORE_add_cert(store, x509);
            //                        X509_free(x509);
            //                    }
            //                }

            //                CertFreeCertificateContext(pContext);
            //                CertCloseStore(hStore, 0);

            //                SSL_CTX_set_cert_store(ctx.native_handle(), store);
            //#else
            //                char* certPath = getenv("SSL_CERT_FILE");
            //                try
            //                {
            //                    ctx.load_verify_file(certPath ? certPath : "/etc/ssl/certs/ca-certificates.crt");
            //                }
            //                catch (...)
            //        {
            //                    cwarn << "Failed to load ca certificates. Either the file "
            //                     "'/etc/ssl/certs/ca-certificates.crt' does not exist";
            //                    cwarn << "or the environment variable SSL_CERT_FILE is set to an invalid or "
            //                     "inaccessible file.";
            //                    cwarn << "It is possible that certificate verification can fail.";
            //                }
            //#endif
            //                }
            //    else
            //                {
            //                    m_nonsecuresocket = std::make_shared<boost::asio::ip::tcp::socket>(m_io_service);
            //                    m_socket = m_nonsecuresocket.get();
            //                }

            //                // Activate keep alive to detect disconnects
            //                unsigned int keepAlive = 10000;

            //#if defined(_WIN32)
            //    int32_t timeout = keepAlive;
            //    setsockopt(
            //        m_socket->native_handle(), SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeout, sizeof(timeout));
            //    setsockopt(
            //        m_socket->native_handle(), SOL_SOCKET, SO_SNDTIMEO, (const char*)&timeout, sizeof(timeout));
            //#else
            //                timeval tv{ static_cast<suseconds_t>(keepAlive / 1000), static_cast<suseconds_t>(keepAlive % 1000)};
            //                setsockopt(m_socket->native_handle(), SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
            //                setsockopt(m_socket->native_handle(), SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof(tv));
            //#endif
        }

        public override void Connect()
        {
            // Prevent unnecessary and potentially dangerous recursion
            if (_connecting != 0)
                return;
            Console.WriteLine("EthStratumClient::connect() begin");

            // Start timing operations
            _workloopTimer.Change(new TimeSpan(0, 0, 0, 0, _workloopInterval), new TimeSpan(0, 0, -1));

            // Reset status flags
            _authPending = false; //: atomic

            // Initializes socket and eventually secure stream
            if (_socket == null)
                InitSocket();

            // Initialize a new queue of end points
            _endpoints = new Queue<IPEndPoint>();
            _endpoint = null;

            // Begin resolve all ips associated to hostname calling the resolver each time is useful as most load balancers will give Ips in different order
            if (_conn.HostNameType == UriHostNameType.Dns || _conn.HostNameType == UriHostNameType.Basic)
                Dns.BeginGetHostEntry(_conn.DnsSafeHost, ResolveHandler, null);
            // No need to use the resolver if host is already an IP address
            else
            {
                _endpoints.Enqueue(new IPEndPoint(IPAddress.Parse(_conn.Host), _conn.Port));
                Task.Run(() => StartConnect());
            }
            Console.WriteLine("EthStratumClient::connect() end");
        }

        public override void Disconnect()
        {
            // Prevent unnecessary and potentially dangerous recursion
            if (Interlocked.CompareExchange(ref _disconnecting, 0, 1) != 1)
                return;
            _connected = false; //: atomic

            Console.WriteLine("EthStratumClient::disconnect() begin");

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
            Console.WriteLine("EthStratumClient::disconnect() end");
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
                        Task.Run(() => StartConnect());
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
            _onDisconnected?.Invoke(Farm.F, this);
        }

        void ResolveHandler(object ec)
        {
            if (ec == null)
            {
                //while (i != tcp::resolver::iterator())
                //{
                //    m_endpoints.push(i->endpoint());
                //    i++;
                //}
                //_resolver.cancel();

                // Resolver has finished so invoke connection asynchronously
                Task.Run(() => StartConnect());
            }
            else
            {
                Console.WriteLine($"Could not resolve host {_conn.Host}, "); // << ec.message();

                // Release locking flag and set connection status
                _connecting = 0; //: atomic

                // We "simulate" a disconnect, to ensure a fully shutdown state
                DisconnectFinalize();
            }
        }

        void StartConnect()
        {
            if (_connecting != 0) //: atomic
                return;
            _connecting = 1; //: atomic

            if (_endpoints.Count != 0)
            {
                // Pick the first endpoint in list.
                // Eventually endpoints get discarded on connection errors
                _endpoint = _endpoints.First();

                // Re-init socket if we need to
                if (_socket == null)
                    InitSocket();

#if DEBUG
                //if (_logOptions & LOG_CONNECT)
                Console.WriteLine($"Trying {_endpoint} ...");
#endif

                ClearResponsePleas();
                _connecting = 1; //: atomic
                EnqueueResponsePlea();
                _solutionSubmittedMaxId = 0;

                // Start connecting async
                _socket.BeginConnect(_endpoint.Address, _endpoint.Port, ConnectHandler, null);
            }
            else
            {
                _connecting = 0; //: atomic
                Console.WriteLine($"No more IP addresses to try for host: {_conn.Host}");

                // We "simulate" a disconnect, to ensure a fully shutdown state
                DisconnectFinalize();
            }
        }

        void WorkloopTimer_Elapsed(object ec)
        {
            // On timer cancelled or nothing to check for then early exit
            if (((string)ec == "operation_aborted") || _conn == null)
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
                            var emptyRequest = JsonDocument.Parse(@"{""id"" = 1, ""result"" = null, ""error"" = true}").RootElement;
                            Task.Run(() => ProcessResponse(emptyRequest));
                        }
                        else
                        {
                            // Waiting for a response to solution submission
                            Console.WriteLine($"No response received in {_responseTimeout} seconds.");
                            _endpoints.Dequeue();
                            ClearResponsePleas();
                            Task.Run(() => Disconnect());
                        }
                    }
                    // No work timeout
                    else if (_session != null && (DateTime.Now - _currentTimestamp).TotalSeconds > _workTimeout)
                    {
                        Console.WriteLine($"No new work received in {_workTimeout} seconds.");
                        _endpoints.Dequeue();
                        ClearResponsePleas();
                        Task.Run(() => Disconnect());
                    }
                }
            }

            // Resubmit timing operations
            _workloopTimer.Change(new TimeSpan(0, 0, 0, 0, _workloopInterval), new TimeSpan(0, 0, -1));
        }

        void ConnectHandler(object ec)
        {
            Console.WriteLine("EthStratumClient::connect_handler() begin");

            // Set status completion
            _connecting = 0; //: atomic

            // Timeout has run before or we got error
            if (ec != null || !_socket.Connected)
            {
                Console.WriteLine($"Error {_endpoint} [ {(ec != null ? ec : "Timeout")} ]");

                // We need to close the socket used in the previous connection attempt before starting a new one.
                // In case of error, in fact, boost does not close the socket
                // If socket is not opened it means we got timed out
                if (_socket.Connected)
                    _socket.Dispose();

                // Discard this endpoint and try the next available.
                // Eventually is start_connect which will check for an empty list.
                _endpoints.Dequeue();
                Task.Run(() => StartConnect());

                Console.WriteLine("EthStratumClient::connect_handler() end1");
                return;
            }

            // We got a socket connection established
            //_conn.Responds(true);
            _connected = true; //: atomic

            _message = null;

            // Clear txqueue
            _txQueue.Clear();

#if DEBUG
            //if (_logOptions & LOG_CONNECT)
            Console.WriteLine($"Socket connected to {ActiveEndPoint}");
#endif

            if (_conn.GetStratumSecLevel() != StratumSecLevel.None)
            {
                _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                _socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.NoDelay, true);

                //_securesocket->handshake(boost::asio::ssl::stream_base::client, hec);
                //_secureSocket.AuthenticateAsClient()
                var hec = (int?)null;
                if (hec != null)
                {
                    Console.WriteLine("SSL/TLS Handshake failed: {hec.message()}");
                    if (hec.Value == 337047686)
                    {  // certificate verification failed
                        Console.WriteLine(@"
This can have multiple reasons:
* Root certs are either not installed or not found
* Pool uses a self-signed certificate
* Pool hostname you're connecting to does not match the CN registered for the certificate.

Possible fixes:

WINDOWS:
* Make sure the file '/etc/ssl/certs/ca-certificates.crt' exists and is accessible
* Export the correct path via 'export SSL_CERT_FILE=/etc/ssl/certs/ca-certificates.crt' to the correct file
  On most systems you can install the 'ca-certificates' package
  You can also get the latest file here:
https://curl.haxx.se/docs/caextract.html

ALL:
* Double check hostname in the -P argument.
* Disable certificate verification all-together via environment variable. See ethminer --help for info about environment variables

If you do the latter please be advised you might expose yourself to the risk of seeing your shares stolen
");
                    }

                    // This is a fatal error
                    // No need to try other IPs as the certificate is based on host-name not ip address. Trying other IPs would end up with the very same error.
                    _conn.MarkUnrecoverable();
                    Task.Run(() => Disconnect());
                    Console.WriteLine("EthStratumClient::connect_handler() end2");
                    return;
                }
            }
            else
            {
                //_nonsecuresocket.set_option(boost::asio::socket_base::keep_alive(true));
                //_nonsecuresocket.set_option(tcp::no_delay(true));
            }

            // Clean buffer from any previous stale data
            //_sendBuffer.consume(4096);
            ClearResponsePleas();

            /*
            If connection has been set-up with a specific scheme then set it's related stratum version as confirmed.

            Otherwise let's go through an autodetection.

            Autodetection process passes all known stratum modes.
            - 1st pass EthStratumClient::ETHEREUMSTRATUM2 (3)
            - 2nd pass EthStratumClient::ETHEREUMSTRATUM  (2)
            - 3rd pass EthStratumClient::ETHPROXY         (1)
            - 4th pass EthStratumClient::STRATUM          (0)
            */
            if (_conn.GetStratumVersion() < StratumVersion.AutoDetect)
                _conn.SetStratumMode(_conn.GetStratumVersion(), true);
            else if (!_conn.StratumModeConfirmed() && _conn.GetStratumMode() == StratumVersion.AutoDetect)
                _conn.SetStratumMode(StratumVersion.EthereumStratum2, false);

            object req;
            var (user, worker, pass) = _conn.StratumUserInfo();
            switch (_conn.GetStratumMode())
            {
                case StratumVersion.Stratum:
                    req = new
                    {
                        id = 1U,
                        method = "mining.subscribe",
                        jsonrpc = "2.0",
                        @params = new string[0],
                    };
                    break;
                case StratumVersion.EthProxy:
                    req = new
                    {
                        id = 1U,
                        method = "eth_submitLogin",
                        worker,
                        jsonrpc = "2.0",
                        @params = new string[] {
                            $"{user}{_conn.AbsolutePath}",
                            pass
                        }.ClampArray(),
                    };
                    break;
                case StratumVersion.EthereumStratum:
                    req = new
                    {
                        id = 1U,
                        method = "mining.subscribe",
                        @params = new string[] {
                            ProjectNameWithVersion,
                            "EthereumStratum/1.0.0"
                        }
                    };
                    break;
                case StratumVersion.EthereumStratum2:
                    req = new
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
                    };
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(StratumVersion), _conn.GetStratumMode().ToString());
            }

            // Begin receive data
            RecvSocketData();

            /*
            Send first message
            NOTE: It's been tested that f2pool.com does not respond with json error to wrong access message (which is needed to autodetect stratum mode).
            IT DOES NOT RESPOND AT ALL:
            Due to this we need to set a timeout (arbitrary set to 1 second) and if no response within that time consider the tentative login failed
            and switch to next stratum mode test
            */
            EnqueueResponsePlea();
            Send(req);

            Console.WriteLine("EthStratumClient::connect_handler() end");
        }

        void StartSession()
        {
            // Start a new session of data
            _session = new Session();
            _currentTimestamp = DateTime.Now;

            // Invoke higher level handlers
            _onConnected?.Invoke(Farm.F, this);
        }

        string ProcessError()
        {
            string retVar;
            //if (responseObject.isMember("error") && !responseObject.get("error", Json::Value::null).isNull())
            //{
            //    //if (responseObject["error"].isConvertibleTo(Json::ValueType::stringValue))
            //    //{
            //    //    retVar = responseObject.get("error", "Unknown error").asString();
            //    //}
            //    //else if (responseObject["error"].isConvertibleTo(Json::ValueType::arrayValue))
            //    //{
            //    //    foreach (var i in responseObject["error"])
            //    //        retVar += i.asString() + " ";
            //    //}
            //    //else if (responseObject["error"].isConvertibleTo(Json::ValueType::objectValue))
            //    //{
            //    //    foreach (var i in responseObject["error"])
            //    //    {
            //    //        Json::Value k = i.key();
            //    //        Json::Value v = (*i);
            //    //        retVar += (std::string)i.name() + ":" + v.asString() + " ";
            //    //    }
            //    //}
            //}
            //else
            retVar = "Unknown error";
            return retVar;
        }

        void ProcessExtranonce(string enonce)
        {
            _session.ExtraNonceSizeBytes = enonce.Length;
            Console.WriteLine($"Extranonce set to {Ansi.White}{enonce}{Ansi.Reset}");
            enonce = enonce.PadRight(16, '0');
            _session.ExtraNonce = ulong.Parse(enonce);
        }

        void ProcessResponse(JsonElement responseObject)
        {
            //// Store jsonrpc version to test against
            //int _rpcVer = responseObject.isMember("jsonrpc") ? 2 : 1;

            //bool _isNotification = false;  // Whether or not this message is a reply to previous request or
            //                               // is a broadcast notification
            //bool _isSuccess = false;       // Whether or not this is a succesful or failed response (implies
            //                               // _isNotification = false)
            //string _errReason = "";        // Content of the error reason
            //string _method = "";           // The method of the notification (or request from pool)
            //unsigned _id = 0;  // This SHOULD be the same id as the request it is responding to (known
            //                   // exception is ethermine.org using 999)


            //// Retrieve essential values
            //_id = responseObject.get("id", unsigned(0)).asUInt();
            //_isSuccess = responseObject.get("error", Json::Value::null).empty();
            //_errReason = (_isSuccess ? "" : processError(responseObject));
            //_method = responseObject.get("method", "").asString();
            //_isNotification = (_method != "" || _id == unsigned(0));

            //// Notifications of new jobs are like responses to get_work requests
            //if (_isNotification && _method == "" && m_conn->StratumMode() == EthStratumClient::ETHPROXY &&
            //    responseObject["result"].isArray())
            //{
            //    _method = "mining.notify";
            //}

            //// Very minimal sanity checks
            //// - For rpc2 member "jsonrpc" MUST be valued to "2.0"
            //// - For responses ... well ... whatever
            //// - For notifications I must receive "method" member and a not empty "params" or "result"
            //// member
            //if ((_rpcVer == 2 && (!responseObject["jsonrpc"].isString() ||
            //                         responseObject.get("jsonrpc", "") != "2.0")) ||
            //    (_isNotification && (responseObject["params"].empty() && responseObject["result"].empty())))
            //{
            //    cwarn << "Pool sent an invalid jsonrpc message...";
            //    cwarn << "Do not blame ethminer for this. Ask pool devs to honor http://www.jsonrpc.org/ "
            //             "specifications ";
            //    cwarn << "Disconnecting...";
            //    m_io_service.post(m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //    return;
            //}


            //// Handle awaited responses to OUR requests (calc response times)
            //if (!_isNotification)
            //{
            //    Json::Value jReq;
            //    Json::Value jResult = responseObject.get("result", Json::Value::null);
            //    std::chrono::milliseconds response_delay_ms(0);

            //    if (_id == 1)
            //    {
            //        response_delay_ms = dequeue_response_plea();

            //        /*
            //        This is the response to very first message after connection.
            //        Message request vary upon stratum flavour
            //        I wish I could manage to have different Ids but apparently ethermine.org always replies
            //        to first message with id=1 regardless the id originally sent.
            //        */

            //        /*
            //        If we're in autodetection phase an error message (of any kind) means
            //        the selected stratum flavour does not comply with the one implemented by the
            //        work provider (the pool) : thus exit, disconnect and try another one
            //        */

            //        if (!_isSuccess && !m_conn->StratumModeConfirmed())
            //        {
            //            // Disconnect and Proceed with next step of autodetection
            //            switch (m_conn->StratumMode())
            //            {
            //                case ETHEREUMSTRATUM2:
            //                    cnote << "Negotiation of EthereumStratum/2.0.0 failed. Trying another ...";
            //                    break;
            //                case ETHEREUMSTRATUM:
            //                    cnote << "Negotiation of EthereumStratum/1.0.0 failed. Trying another ...";
            //                    break;
            //                case ETHPROXY:
            //                    cnote << "Negotiation of Eth-Proxy compatible failed. Trying another ...";
            //                    break;
            //                case STRATUM:
            //                    cnote << "Negotiation of Stratum failed.";
            //                    break;
            //                default:
            //                    // Should not happen
            //                    break;
            //            }

            //            m_io_service.post(
            //                m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //            return;
            //        }

            //        /*
            //        Process response for each stratum flavour :
            //        ETHEREUMSTRATUM2 response to mining.hello
            //        ETHEREUMSTRATUM  response to mining.subscribe
            //        ETHPROXY         response to eth_submitLogin
            //        STRATUM          response to mining.subscribe
            //        */

            //        switch (m_conn->StratumMode())
            //        {
            //            case EthStratumClient::ETHEREUMSTRATUM2:

            //                _isSuccess = (jResult.isConvertibleTo(Json::ValueType::objectValue) &&
            //                              jResult.isMember("proto") &&
            //                              jResult["proto"].asString() == "EthereumStratum/2.0.0" &&
            //                              jResult.isMember("encoding") && jResult.isMember("resume") &&
            //                              jResult.isMember("timeout") && jResult.isMember("maxerrors") &&
            //                              jResult.isMember("node"));

            //                if (_isSuccess)
            //                {
            //                    // Selected flavour is confirmed
            //                    m_conn->SetStratumMode(3, true);
            //                    cnote << "Stratum mode : EthereumStratum/2.0.0";
            //                    startSession();

            //                    // Send request for subscription
            //                    jReq["id"] = unsigned(2);
            //                    jReq["method"] = "mining.subscribe";
            //                    enqueue_response_plea();
            //                }
            //                else
            //                {
            //                    // If no autodetection the connection is not usable
            //                    // with this stratum flavor
            //                    if (m_conn->StratumModeConfirmed())
            //                    {
            //                        m_conn->MarkUnrecoverable();
            //                        cnote << "Negotiation of EthereumStratum/2.0.0 failed. Change your "
            //                                 "connection parameters";
            //                    }
            //                    else
            //                    {
            //                        cnote << "Negotiation of EthereumStratum/2.0.0 failed. Trying another ...";
            //                    }
            //                    // Disconnect
            //                    m_io_service.post(
            //                        m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //                    return;
            //                }

            //                break;

            //            case EthStratumClient::ETHEREUMSTRATUM:

            //                _isSuccess = (jResult.isArray() && jResult[0].isArray() && jResult[0].size() == 3 &&
            //                              jResult[0].get(Json::Value::ArrayIndex(2), "").asString() ==
            //                                  "EthereumStratum/1.0.0");
            //                if (_isSuccess)
            //                {
            //                    // Selected flavour is confirmed
            //                    m_conn->SetStratumMode(2, true);
            //                    cnote << "Stratum mode : EthereumStratum/1.0.0 (NiceHash)";
            //                    startSession();
            //                    m_session->subscribed.store(true, memory_order_relaxed);

            //                    // Notify we're ready for extra nonce subscribtion on the fly
            //                    // reply to this message should not perform any logic
            //                    jReq["id"] = unsigned(2);
            //                    jReq["method"] = "mining.extranonce.subscribe";
            //                    jReq["params"] = Json::Value(Json::arrayValue);
            //                    send(jReq);

            //                    std::string enonce = jResult.get(Json::Value::ArrayIndex(1), "").asString();
            //                    if (!enonce.empty()) processExtranonce(enonce);

            //                    // Eventually request authorization
            //                    m_authpending.store(true, std::memory_order_relaxed);
            //                    jReq["id"] = unsigned(3);
            //                    jReq["method"] = "mining.authorize";
            //                    jReq["params"].append(m_conn->UserDotWorker() + m_conn->Path());
            //                    jReq["params"].append(m_conn->Pass());
            //                    enqueue_response_plea();
            //                }
            //                else
            //                {
            //                    // If no autodetection the connection is not usable
            //                    // with this stratum flavor
            //                    if (m_conn->StratumModeConfirmed())
            //                    {
            //                        m_conn->MarkUnrecoverable();
            //                        cnote << "Negotiation of EthereumStratum/1.0.0 (NiceHash) failed. Change "
            //                                 "your "
            //                                 "connection parameters";
            //                    }
            //                    else
            //                    {
            //                        cnote << "Negotiation of EthereumStratum/1.0.0 (NiceHash) failed. Trying "
            //                                 "another ...";
            //                    }
            //                    // Disconnect
            //                    m_io_service.post(
            //                        m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //                    return;
            //                }

            //                break;

            //            case EthStratumClient::ETHPROXY:

            //                if (_isSuccess)
            //                {
            //                    // Selected flavour is confirmed
            //                    m_conn->SetStratumMode(1, true);
            //                    cnote << "Stratum mode : Eth-Proxy compatible";
            //                    startSession();

            //                    m_session->subscribed.store(true, std::memory_order_relaxed);
            //                    m_session->authorized.store(true, std::memory_order_relaxed);

            //                    // Request initial work
            //                    jReq["id"] = unsigned(5);
            //                    jReq["method"] = "eth_getWork";
            //                    jReq["params"] = Json::Value(Json::arrayValue);
            //                }
            //                else
            //                {
            //                    // If no autodetection the connection is not usable
            //                    // with this stratum flavor
            //                    if (m_conn->StratumModeConfirmed())
            //                    {
            //                        m_conn->MarkUnrecoverable();
            //                        cnote << "Negotiation of Eth-Proxy compatible failed. Change your "
            //                                 "connection parameters";
            //                    }
            //                    else
            //                    {
            //                        cnote << "Negotiation of Eth-Proxy compatible failed. Trying "
            //                                 "another ...";
            //                    }
            //                    // Disconnect
            //                    m_io_service.post(
            //                        m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //                    return;
            //                }

            //                break;

            //            case EthStratumClient::STRATUM:

            //                if (_isSuccess)
            //                {
            //                    // Selected flavour is confirmed
            //                    m_conn->SetStratumMode(0, true);
            //                    cnote << "Stratum mode : Stratum";
            //                    startSession();
            //                    m_session->subscribed.store(true, memory_order_relaxed);

            //                    // Request authorization
            //                    m_authpending.store(true, std::memory_order_relaxed);
            //                    jReq["id"] = unsigned(3);
            //                    jReq["jsonrpc"] = "2.0";
            //                    jReq["method"] = "mining.authorize";
            //                    jReq["params"] = Json::Value(Json::arrayValue);
            //                    jReq["params"].append(m_conn->UserDotWorker() + m_conn->Path());
            //                    jReq["params"].append(m_conn->Pass());
            //                    enqueue_response_plea();
            //                }
            //                else
            //                {
            //                    // If no autodetection the connection is not usable
            //                    // with this stratum flavor
            //                    if (m_conn->StratumModeConfirmed())
            //                    {
            //                        m_conn->MarkUnrecoverable();
            //                        cnote << "Negotiation of Eth-Proxy compatible failed. Change your "
            //                                 "connection parameters";
            //                    }
            //                    else
            //                    {
            //                        cnote << "Negotiation of Eth-Proxy compatible failed. Trying "
            //                                 "another ...";
            //                    }
            //                    // Disconnect
            //                    m_io_service.post(
            //                        m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //                    return;
            //                }

            //                break;

            //            default:

            //                // Should not happen
            //                break;
            //        }


            //        send(jReq);
            //    }

            //    else if (_id == 2)
            //    {
            //        // For EthereumStratum/1.0.0
            //        // This is the response to mining.extranonce.subscribe
            //        // according to this
            //        // https://github.com/nicehash/Specifications/blob/master/NiceHash_extranonce_subscribe_extension.txt
            //        // In all cases, client does not perform any logic when receiving back these replies.
            //        // With mining.extranonce.subscribe subscription, client should handle extranonce1
            //        // changes correctly
            //        // Nothing to do here.

            //        // For EthereumStratum/2.0.0
            //        // This is the response to mining.subscribe
            //        // https://github.com/AndreaLanfranchi/EthereumStratum-2.0.0#session-handling---response-to-subscription
            //        if (m_conn->StratumMode() == 3)
            //        {
            //            response_delay_ms = dequeue_response_plea();

            //            if (!jResult.isString() || !jResult.asString().size())
            //            {
            //                // Got invalid session id which is mandatory
            //                cwarn << "Got invalid or missing session id. Disconnecting ... ";
            //                m_conn->MarkUnrecoverable();
            //                m_io_service.post(
            //                    m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //                return;
            //            }

            //            m_session->sessionId = jResult.asString();
            //            m_session->subscribed.store(true, memory_order_relaxed);

            //            // Request authorization
            //            m_authpending.store(true, std::memory_order_relaxed);
            //            jReq["id"] = unsigned(3);
            //            jReq["method"] = "mining.authorize";
            //            jReq["params"] = Json::Value(Json::arrayValue);
            //            jReq["params"].append(m_conn->UserDotWorker() + m_conn->Path());
            //            jReq["params"].append(m_conn->Pass());
            //            enqueue_response_plea();
            //            send(jReq);
            //        }
            //    }

            //    else if (_id == 3 && m_conn->StratumMode() != ETHEREUMSTRATUM2)
            //    {
            //        response_delay_ms = dequeue_response_plea();

            //        // Response to "mining.authorize"
            //        // (https://en.bitcoin.it/wiki/Stratum_mining_protocol#mining.authorize) Result should
            //        // be boolean, some pools also throw an error, so _isSuccess can be false Due to this
            //        // reevaluate _isSuccess

            //        if (_isSuccess && jResult.isBool())
            //            _isSuccess = jResult.asBool();

            //        m_authpending.store(false, std::memory_order_relaxed);
            //        m_session->authorized.store(_isSuccess, std::memory_order_relaxed);

            //        if (!isAuthorized())
            //        {
            //            cnote << "Worker " << EthWhite << m_conn->UserDotWorker() << EthReset
            //                  << " not authorized : " << _errReason;
            //            m_conn->MarkUnrecoverable();
            //            m_io_service.post(
            //                m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //            return;
            //        }
            //        else
            //        {
            //            cnote << "Authorized worker " << m_conn->UserDotWorker();
            //        }
            //    }

            //    else if (_id == 3 && m_conn->StratumMode() == ETHEREUMSTRATUM2)
            //    {
            //        response_delay_ms = dequeue_response_plea();

            //        if (!_isSuccess || (!jResult.isString() || !jResult.asString().size()))
            //        {
            //            // Got invalid session id which is mandatory
            //            cnote << "Worker " << EthWhite << m_conn->UserDotWorker() << EthReset
            //                  << " not authorized : " << _errReason;
            //            m_conn->MarkUnrecoverable();
            //            m_io_service.post(
            //                m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //            return;
            //        }
            //        m_authpending.store(false, memory_order_relaxed);
            //        m_session->authorized.store(true, memory_order_relaxed);
            //        m_session->workerId = jResult.asString();
            //        cnote << "Authorized worker " << m_conn->UserDotWorker();

            //        // Nothing else to here. Wait for notifications from pool
            //    }

            //    else if ((_id >= 40 && _id <= m_solution_submitted_max_id) &&
            //             m_conn->StratumMode() != ETHEREUMSTRATUM2)
            //    {
            //        response_delay_ms = dequeue_response_plea();

            //        // Response to solution submission mining.submit
            //        // (https://en.bitcoin.it/wiki/Stratum_mining_protocol#mining.submit) Result should be
            //        // boolean, some pools also throw an error, so _isSuccess can be false Due to this
            //        // reevaluate _isSucess

            //        if (_isSuccess && jResult.isBool())
            //            _isSuccess = jResult.asBool();

            //        const unsigned miner_index = _id - 40;
            //        if (_isSuccess)
            //        {
            //            if (m_onSolutionAccepted)
            //                m_onSolutionAccepted(response_delay_ms, miner_index, false);
            //        }
            //        else
            //        {
            //            if (m_onSolutionRejected)
            //            {
            //                cwarn << "Reject reason : "
            //                      << (_errReason.empty() ? "Unspecified" : _errReason);
            //                m_onSolutionRejected(response_delay_ms, miner_index);
            //            }
            //        }
            //    }

            //    else if ((_id >= 40 && _id <= m_solution_submitted_max_id) &&
            //             m_conn->StratumMode() == ETHEREUMSTRATUM2)
            //    {
            //        response_delay_ms = dequeue_response_plea();

            //        // In EthereumStratum/2.0.0 we can evaluate the severity of the
            //        // error. An 2xx error means the solution have been accepted but is
            //        // likely stale
            //        bool isStale = false;
            //        if (!_isSuccess)
            //        {
            //            string errCode = responseObject["error"].get("code", "").asString();
            //            if (errCode.substr(0, 1) == "2")
            //                _isSuccess = isStale = true;
            //        }


            //        const unsigned miner_index = _id - 40;
            //        if (_isSuccess)
            //        {
            //            if (m_onSolutionAccepted)
            //                m_onSolutionAccepted(response_delay_ms, miner_index, isStale);
            //        }
            //        else
            //        {

            //            if (m_onSolutionRejected)
            //            {
            //                cwarn << "Reject reason : "
            //                      << (_errReason.empty() ? "Unspecified" : _errReason);
            //                m_onSolutionRejected(response_delay_ms, miner_index);
            //            }
            //        }
            //    }

            //    else if (_id == 5)
            //    {
            //        // This is the response we get on first get_work request issued
            //        // in mode EthStratumClient::ETHPROXY
            //        // thus we change it to a mining.notify notification
            //        if (m_conn->StratumMode() == EthStratumClient::ETHPROXY &&
            //            responseObject["result"].isArray())
            //        {
            //            _method = "mining.notify";
            //            _isNotification = true;
            //        }
            //    }

            //    else if (_id == 9)
            //    {
            //        // Response to hashrate submit
            //        // Shall we do anything ?
            //        // Hashrate submit is actually out of stratum spec
            //        if (!_isSuccess)
            //        {
            //            cwarn << "Submit hashRate failed : "
            //                  << (_errReason.empty() ? "Unspecified error" : _errReason);
            //        }
            //    }

            //    else if (_id == 999)
            //    {
            //        // This unfortunate case should not happen as none of the outgoing requests is marked
            //        // with id 999 However it has been tested that ethermine.org responds with this id when
            //        // error replying to either mining.subscribe (1) or mining.authorize requests (3) To
            //        // properly handle this situation we need to rely on Subscribed/Authorized states

            //        if (!_isSuccess && !m_conn->StratumModeConfirmed())
            //        {
            //            // Disconnect and Proceed with next step of autodetection
            //            switch (m_conn->StratumMode())
            //            {
            //                case ETHEREUMSTRATUM2:
            //                    cnote << "Negotiation of EthereumStratum/2.0.0 failed. Trying another ...";
            //                    break;
            //                case ETHEREUMSTRATUM:
            //                    cnote << "Negotiation of EthereumStratum/1.0.0 failed. Trying another ...";
            //                    break;
            //                case ETHPROXY:
            //                    cnote << "Negotiation of Eth-Proxy compatible failed. Trying another ...";
            //                    break;
            //                case STRATUM:
            //                    cnote << "Negotiation of Stratum failed.";
            //                    break;
            //                default:
            //                    // Should not happen
            //                    break;
            //            }

            //            m_io_service.post(
            //                m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //            return;
            //        }

            //        if (!_isSuccess)
            //        {
            //            if (!isSubscribed())
            //            {
            //                // Subscription pending
            //                cnote << "Subscription failed : "
            //                      << (_errReason.empty() ? "Unspecified error" : _errReason);
            //                m_io_service.post(
            //                    m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //                return;
            //            }
            //            else if (isSubscribed() && !isAuthorized())
            //            {
            //                // Authorization pending
            //                cnote << "Worker not authorized : "
            //                      << (_errReason.empty() ? "Unspecified error" : _errReason);
            //                m_io_service.post(
            //                    m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //                return;
            //            }
            //        };
            //    }

            //    else
            //    {
            //        cnote << "Got response for unknown message id [" << _id << "] Discarding...";
            //        return;
            //    }
            //}

            ///*


            //Handle unsolicited messages FROM pool AKA notifications

            //NOTE !
            //Do not process any notification unless login validated
            //which means we have detected proper stratum mode.

            //*/

            //if (_isNotification && m_conn->StratumModeConfirmed())
            //{
            //    Json::Value jReq;
            //    Json::Value jPrm;

            //    unsigned prmIdx;

            //    if (_method == "mining.notify" && m_conn->StratumMode() != ETHEREUMSTRATUM2)
            //    {
            //        // Discard jobs if not properly subscribed
            //        // or if a job for this transmission has already
            //        // been processed
            //        if (!isSubscribed() || m_newjobprocessed)
            //            return;

            //        /*
            //        Workaround for Nanopool wrong implementation
            //        see issue # 1348
            //        */

            //        if (m_conn->StratumMode() == EthStratumClient::ETHPROXY &&
            //            responseObject.isMember("result"))
            //        {
            //            jPrm = responseObject.get("result", Json::Value::null);
            //            prmIdx = 0;
            //        }
            //        else
            //        {
            //            jPrm = responseObject.get("params", Json::Value::null);
            //            prmIdx = 1;
            //        }


            //        if (jPrm.isArray() && !jPrm.empty())
            //        {
            //            m_current.job = jPrm.get(Json::Value::ArrayIndex(0), "").asString();

            //            if (m_conn->StratumMode() == EthStratumClient::ETHEREUMSTRATUM)
            //            {
            //                string sSeedHash = jPrm.get(Json::Value::ArrayIndex(1), "").asString();
            //                string sHeaderHash = jPrm.get(Json::Value::ArrayIndex(2), "").asString();

            //                if (sHeaderHash != "" && sSeedHash != "")
            //                {
            //                    m_current.seed = h256(sSeedHash);
            //                    m_current.header = h256(sHeaderHash);
            //                    m_current.boundary = m_session->nextWorkBoundary;
            //                    m_current.startNonce = m_session->extraNonce;
            //                    m_current.exSizeBytes = m_session->extraNonceSizeBytes;
            //                    m_current_timestamp = std::chrono::steady_clock::now();
            //                    m_current.block = -1;

            //                    // This will signal to dispatch the job
            //                    // at the end of the transmission.
            //                    m_newjobprocessed = true;
            //                }
            //            }
            //            else
            //            {
            //                string sHeaderHash = jPrm.get(Json::Value::ArrayIndex(prmIdx++), "").asString();
            //                string sSeedHash = jPrm.get(Json::Value::ArrayIndex(prmIdx++), "").asString();
            //                string sShareTarget =
            //                    jPrm.get(Json::Value::ArrayIndex(prmIdx++), "").asString();

            //                // Only some eth-proxy compatible implementations carry the block number
            //                // namely ethermine.org
            //                m_current.block = -1;
            //                if (m_conn->StratumMode() == EthStratumClient::ETHPROXY &&
            //                    jPrm.size() > prmIdx &&
            //                    jPrm.get(Json::Value::ArrayIndex(prmIdx), "").asString().substr(0, 2) ==
            //                        "0x")
            //                {
            //                    try
            //                    {
            //                        m_current.block =
            //                            std::stoul(jPrm.get(Json::Value::ArrayIndex(prmIdx), "").asString(),
            //                                nullptr, 16);
            //                        /*
            //                        check if the block number is in a valid range
            //                        A year has ~31536000 seconds
            //                        50 years have ~1576800000
            //                        assuming a (very fast) blocktime of 10s:
            //                        ==> in 50 years we get 157680000 (=0x9660180) blocks
            //                        */
            //                        if (m_current.block > 0x9660180)
            //                            throw new std::exception();
            //                    }
            //                    catch (const std::exception&)
            //            {
            //                        m_current.block = -1;
            //                    }
            //                    }

            //                    // coinmine.pl fix
            //                    int l = sShareTarget.length();
            //                    if (l < 66)
            //                        sShareTarget = "0x" + string(66 - l, '0') + sShareTarget.substr(2);

            //                    m_current.seed = h256(sSeedHash);
            //                    m_current.header = h256(sHeaderHash);
            //                    m_current.boundary = h256(sShareTarget);
            //                    m_current_timestamp = std::chrono::steady_clock::now();

            //                    // This will signal to dispatch the job
            //                    // at the end of the transmission.
            //                    m_newjobprocessed = true;
            //                }
            //            }
            //        }
            //        else if (_method == "mining.notify" && m_conn->StratumMode() == ETHEREUMSTRATUM2)
            //        {
            //            /*
            //            {
            //              "method": "mining.notify",
            //              "params": [
            //                  "bf0488aa",
            //                  "6526d5"
            //                  "645cf20198c2f3861e947d4f67e3ab63b7b2e24dcc9095bd9123e7b33371f6cc",
            //                  "0"
            //              ]
            //            }
            //            */
            //            if (!m_session || !m_session->firstMiningSet)
            //            {
            //                cwarn << "Got mining.notify before mining.set message. Discarding ...";
            //                return;
            //            }

            //            if (!responseObject.isMember("params") || !responseObject["params"].isArray() ||
            //                responseObject["params"].empty() || responseObject["params"].size() != 4)
            //            {
            //                cwarn << "Got invalid mining.notify message. Discarding ...";
            //                return;
            //            }

            //            jPrm = responseObject["params"];
            //            m_current.job = jPrm.get(Json::Value::ArrayIndex(0), "").asString();
            //            m_current.block =
            //                stoul(jPrm.get(Json::Value::ArrayIndex(1), "").asString(), nullptr, 16);

            //            string header =
            //                "0x" + dev::padLeft(jPrm.get(Json::Value::ArrayIndex(2), "").asString(), 64, '0');

            //            m_current.header = h256(header);
            //            m_current.boundary = h256(m_session->nextWorkBoundary.hex(HexPrefix::Add));
            //            m_current.epoch = m_session->epoch;
            //            m_current.algo = m_session->algo;
            //            m_current.startNonce = m_session->extraNonce;
            //            m_current.exSizeBytes = m_session->extraNonceSizeBytes;
            //            m_current_timestamp = std::chrono::steady_clock::now();

            //            // This will signal to dispatch the job
            //            // at the end of the transmission.
            //            m_newjobprocessed = true;
            //        }
            //        else if (_method == "mining.set_difficulty" && m_conn->StratumMode() == ETHEREUMSTRATUM)
            //        {
            //            if (m_conn->StratumMode() == EthStratumClient::ETHEREUMSTRATUM)
            //            {
            //                jPrm = responseObject.get("params", Json::Value::null);
            //                if (jPrm.isArray())
            //                {
            //                    double nextWorkDifficulty =
            //                        max(jPrm.get(Json::Value::ArrayIndex(0), 1).asDouble(), 0.0001);

            //                    m_session->nextWorkBoundary = h256(dev::getTargetFromDiff(nextWorkDifficulty));
            //                }
            //            }
            //            else
            //            {
            //                cwarn << "Invalid mining.set_difficulty rpc method. Disconnecting ...";
            //                if (m_conn->StratumModeConfirmed())
            //                {
            //                    m_conn->MarkUnrecoverable();
            //                }
            //                m_io_service.post(
            //                    m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //            }
            //        }
            //        else if (_method == "mining.set_extranonce" && m_conn->StratumMode() == ETHEREUMSTRATUM)
            //        {
            //            jPrm = responseObject.get("params", Json::Value::null);
            //            if (jPrm.isArray())
            //            {
            //                std::string enonce = jPrm.get(Json::Value::ArrayIndex(0), "").asString();
            //                if (!enonce.empty())
            //                    processExtranonce(enonce);
            //            }
            //        }
            //        else if (_method == "mining.set" && m_conn->StratumMode() == ETHEREUMSTRATUM2)
            //        {
            //            /*
            //            {
            //              "method": "mining.set",
            //              "params": {
            //                  "epoch" : "dc",
            //                  "target" : "0112e0be826d694b2e62d01511f12a6061fbaec8bc02357593e70e52ba",
            //                  "algo" : "ethash",
            //                  "extranonce" : "af4c"
            //              }
            //            }
            //            */
            //            if (!responseObject.isMember("params") || !responseObject["params"].isObject() ||
            //                responseObject["params"].empty())
            //            {
            //                cwarn << "Got invalid mining.set message. Discarding ...";
            //                return;
            //            }
            //            m_session->firstMiningSet = true;
            //            jPrm = responseObject["params"];
            //            string timeout = jPrm.get("timeout", "").asString();
            //            string epoch = jPrm.get("epoch", "").asString();
            //            string target = jPrm.get("target", "").asString();

            //            if (!timeout.empty())
            //                m_session->timeout = stoi(timeout, nullptr, 16);

            //            if (!epoch.empty())
            //                m_session->epoch = stoul(epoch, nullptr, 16);

            //            if (!target.empty())
            //            {
            //                target = "0x" + dev::padLeft(target, 64, '0');
            //                m_session->nextWorkBoundary = h256(target);
            //            }

            //            m_session->algo = jPrm.get("algo", "ethash").asString();
            //            string enonce = jPrm.get("extranonce", "").asString();
            //            if (!enonce.empty())
            //                processExtranonce(enonce);
            //        }
            //        else if (_method == "mining.bye" && m_conn->StratumMode() == ETHEREUMSTRATUM2)
            //        {
            //            cnote << m_conn->Host() << " requested connection close. Disconnecting ...";
            //            m_io_service.post(m_io_strand.wrap(boost::bind(&EthStratumClient::disconnect, this)));
            //        }
            //        else if (_method == "client.get_version")
            //        {
            //            jReq["id"] = _id;
            //            jReq["result"] = ethminer_get_buildinfo()->project_name_with_version;

            //            if (_rpcVer == 1)
            //            {
            //                jReq["error"] = Json::Value::null;
            //            }
            //            else if (_rpcVer == 2)
            //            {
            //                jReq["jsonrpc"] = "2.0";
            //            }

            //            send(jReq);
            //        }
            //        else
            //        {
            //            cwarn << "Got unknown method [" << _method << "] from pool. Discarding...";

            //            // Respond back to issuer
            //            if (_rpcVer == 2)
            //                jReq["jsonrpc"] = "2.0";

            //            jReq["id"] = _id;
            //            jReq["error"] = "Method not found";

            //            send(jReq);
            //        }
            //    }
        }
        public override void SubmitHashrate(ulong rate, string id)
        {
            if (!IsConnected)
                return;

            var (user, worker, pass) = _conn.StratumUserInfo();
            if (_conn.GetStratumMode() != StratumVersion.EthereumStratum2)
                // There is no stratum method to submit the hashrate so we use the rpc variant.
                // Note !!
                // id = 6 is also the id used by ethermine.org and nanopool to push new jobs
                // thus we will be in trouble if we want to check the result of hashrate submission actually change the id from 6 to 9
                Send(new
                {
                    id = 9U,
                    jsonrpc = "2.0",
                    worker,
                    method = "eth_submitHashrate",
                    @params = new string[] {
                        $"0x{rate:x}",
                        id
                    }.ClampArray()
                });
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

            object req;
            var (user, worker, pass) = _conn.StratumUserInfo();
            switch (_conn.GetStratumMode())
            {
                case StratumVersion.Stratum:
                    req = new
                    {
                        id = 40 + solution.MIdx,
                        method = "mining.submit",
                        jsonrpc = "2.0",
                        @parms = new string[]
                        {
                            user,
                            solution.Work.Job,
                            $"0x{solution.Nonce:x}",
                            $"0x{solution.Work.Header:x}",
                            $"0x{solution.MixHash:x}",
                            worker,
                        }.ClampArray()
                    };
                    break;
                case StratumVersion.EthProxy:
                    req = new
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
                    };
                    break;
                case StratumVersion.EthereumStratum:
                    req = new
                    {
                        id = 40 + solution.MIdx,
                        method = "mining.submit",
                        @parms = new string[]
                        {
                            worker != null ? $"{user}.{worker}" : user,
                            solution.Work.Job,
                            $"{solution.Nonce:x}".Substring(solution.Work.ExSizeBytes),
                         }
                    };
                    break;
                case StratumVersion.EthereumStratum2:
                    req = new
                    {
                        id = 40 + solution.MIdx,
                        method = "mining.submit",
                        @parms = new string[]
                        {
                            solution.Work.Job,
                            $"{solution.Nonce:x}".Substring(solution.Work.ExSizeBytes),
                            _session.WorkerId
                         }.ClampArray()
                    };
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(StratumVersion), _conn.GetStratumMode().ToString());
            }

            EnqueueResponsePlea();
            Send(req);
        }


        void RecvSocketData()
        {
            if (_conn.GetStratumSecLevel() != StratumSecLevel.None)
            {
                //async_read(*m_securesocket, m_recvBuffer, boost::asio::transfer_at_least(1),
                //    m_io_strand.wrap(boost::bind(&EthStratumClient::onRecvSocketDataCompleted, this,
                //        boost::asio::placeholders::error, boost::asio::placeholders::bytes_transferred)));
            }
            else
            {
                //async_read(*m_nonsecuresocket, m_recvBuffer, boost::asio::transfer_at_least(1),
                //    m_io_strand.wrap(boost::bind(&EthStratumClient::onRecvSocketDataCompleted, this,
                //        boost::asio::placeholders::error, boost::asio::placeholders::bytes_transferred)));
            }
        }

        void OnRecvSocketDataCompleted(Exception ec)
        {
            if (ec != null)
            {
                if (IsConnected)
                {
                    if (_authPending != false) //: atomic
                    {
                        Console.WriteLine("Error while waiting for authorization from pool");
                        Console.WriteLine("Double check your pool credentials.");
                        _conn.MarkUnrecoverable();
                    }

                    //if ((ex.category() == boost::asio::error::get_ssl_category()) && (ERR_GET_REASON(ec.value()) == SSL_RECEIVED_SHUTDOWN))
                    //    Console.WriteLine($"SSL Stream remotely closed by {_conn.Host}");
                    //else if (ex == boost::asio::error::eof)
                    //    Console.WriteLine($"Connection remotely closed by {_conn.Host}");
                    //else
                    Console.WriteLine($"Socket read failed: {ec.Message}");
                    Task.Run(() => Disconnect());
                }
                return;
            }

            // Due to the nature of io_service's queue and the implementation of the loop this event may trigger
            // late after clean disconnection. Check status of connection before triggering all stack of calls

            // DO NOT DO THIS !!!!!
            // std::istream is(&m_recvBuffer);
            // std::string message;
            // getline(is, message)
            /*
            There are three reasons :
            1 - Previous async_read_until calls this handler (aside from error codes)
                with the number of bytes in the buffer's get area up to and including
                the delimiter. So we know where to split the line
            2 - Boost's documentation clearly states that after a succesfull
                async_read_until operation the stream buffer MAY contain additional
                data which HAVE to be left in the buffer for subsequent read operations.
                If another delimiter exists in the buffer then it will get caught
                by the next async_read_until()
            3 - std::istream is(&m_recvBuffer) will CONSUME ALL data in the buffer
                thus invalidating the previous point 2
            */

            // Extract received message and free the buffer
            //std::string rx_message(boost::asio::buffer_cast<const char*> (m_recvBuffer.data()), bytes_transferred);
            //m_recvBuffer.consume(bytes_transferred);
            //m_message.append(rx_message);

            // Process each line in the transmission
            // NOTE : as multiple jobs may come in with a single transmission only the last will be dispatched
            _newJobProcessed = false;
            string line;
            var offset = _message.IndexOf("\n");
            while (offset != -1)
            {
                if (offset > 0)
                {
                    line = _message.Substring(0, offset).Trim();
                    if (line.Length != 0)
                    {
                        // Out received message only for debug purpouses
                        //if (_logOptions & LOG_JSON)
                        Console.WriteLine($" << {line}");

                        // Test validity of chunk and process
                        try
                        {
                            var msg = JsonDocument.Parse(line)?.RootElement;
                            if (msg != null)
                            {
                                try
                                {
                                    // Run in sync so no 2 different async reads may overlap
                                    ProcessResponse(msg.Value);
                                }
                                catch (Exception ex2)
                                {
                                    Console.WriteLine($"Stratum got invalid Json message : {ex2.Message}");
                                }
                            }
                        }
                        catch (Exception ex3)
                        {
                            Console.WriteLine($"Stratum got invalid Json message : {ex3.Message}");
                        }
                    }
                }

                _message.Remove(0, offset + 1);
                offset = _message.IndexOf("\n");
            }

            // There is a new job - dispatch it
            _onWorkReceived?.Invoke(Farm.F, this, _current);

            // Eventually keep reading from socket
            if (IsConnected)
                RecvSocketData();
        }

        void Send(object req)
        {
            var line = req is string z ? z : JsonSerializer.Serialize(req);
            _txQueue.Enqueue(line);
            if (Interlocked.CompareExchange(ref _txPending, 0, 1) != 1)
                SendSocketData();
        }

        void SendSocketData()
        {
            if (!IsConnected || _txQueue.Count == 0)
            {
                //_sendBuffer.consume(m_sendBuffer.capacity());
                _txQueue.Clear();
                _txPending = 0; //: atomic
                return;
            }

            //std::ostream os(&m_sendBuffer);
            string line;
            while ((line = _txQueue.Dequeue()) != null)
            {
                //os << *line << std::endl;
                // Out received message only for debug purpouses
                //if (_logOptions & LOG_JSON)
                Console.WriteLine($" >> {line}");
            }

            //if (_conn.StratumSecLevel() != StratumSecLevel.NONE)
            //{
            //    async_write(*m_securesocket, m_sendBuffer,
            //        m_io_strand.wrap(boost::bind(&EthStratumClient::onSendSocketDataCompleted, this,
            //            boost::asio::placeholders::error)));
            //}
            //else
            //{
            //    async_write(*m_nonsecuresocket, m_sendBuffer,
            //        m_io_strand.wrap(boost::bind(&EthStratumClient::onSendSocketDataCompleted, this,
            //            boost::asio::placeholders::error)));
            //}
        }

        void OnSendSocketDataCompleted(Exception ex)
        {
            if (ex != null)
            {
                //_sendBuffer.consume(_sendBuffer.capacity());
                _txQueue.Clear();

                _txPending = 0; //: atomic

                //if (ex is IOException && SSL_R_PROTOCOL_IS_SHUTDOWN == ERR_GET_REASON(ec.value())))
                //{
                //    Console.WriteLine($"SSL Stream error : {ex.Message}");
                //    Task.Run(() => Disconnect());
                //}

                if (IsConnected)
                {
                    Console.WriteLine($"Socket write failed : {ex.Message}");
                    Task.Run(() => Disconnect());
                }
                return;
            }

            // Register last transmission tstamp to prevent timeout in EthereumStratum/2.0.0
            if (_session != null && _conn.GetStratumMode() == StratumVersion.EthereumStratum2)
                _session.LastTxStamp = DateTime.Now;

            if (_txQueue.Count == 0) _txPending = 0; //: atomic
            else SendSocketData();
        }


        void OnSSLShutdownCompleted(Exception ex)
        {
            ClearResponsePleas();
            Task.Run(() => DisconnectFinalize());
        }

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