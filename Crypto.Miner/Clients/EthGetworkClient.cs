using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;

namespace Crypto.IO
{
    public class EthGetworkClient : PoolClient
    {
        int _farmRecheckPeriod = 500;  // In milliseconds
        WorkPackage _current;

        int _connecting = 0; //: atomic<bool>  // Whether or not socket is on first try connect
        int _txPending = 0; //: atomic<bool> // Whether or not an async socket operation is pending
        Queue<string> _txQueue;

        TcpClient _socket;
        Queue<IPEndPoint> _endpoints;

        Stream _stream;
        object _jsonGetWork = new
        {
            id = 1U,
            jsonrpc = "2.0",
            method = "eth_getWork",
            @params = new object[] { },
        };
        JsonElement? _pendingJReq;
        DateTime _pendingTstamp;

        Timer _getworkTimer;  // The timer which triggers getWork requests

        // seconds to trigger a work_timeout (overwritten in constructor)
        int _workTimeout;
        DateTime _currentTstamp;

        int _solutionSubmittedMaxId;  // maximum json id we used to send a solution

        public EthGetworkClient(int workTimeout, int farmRecheckPeriod) : base()
        {
            _workTimeout = workTimeout;
            _farmRecheckPeriod = farmRecheckPeriod;
        }

        public override void Connect()
        {
            // Prevent unnecessary and potentially dangerous recursion
            if (Interlocked.CompareExchange(ref _connecting, 0, 1) != 1)
                return;

            // Reset status flags
            _getworkTimer?.Dispose(); _getworkTimer = null;

            // Initialize a new queue of end points
            _endpoints = new Queue<IPEndPoint>();

            // Begin resolve all ips associated to hostname calling the resolver each time is useful as most load balancers will give Ips in different order
            if (_conn.HostNameType == UriHostNameType.Dns || _conn.HostNameType == UriHostNameType.Basic)
                Dns.BeginGetHostEntry(_conn.DnsSafeHost, HandleResolve, null);
            // No need to use the resolver if host is already an IP address
            else
            {
                _endpoints.Enqueue(new IPEndPoint(IPAddress.Parse(_conn.Host), _conn.Port));
                Send(_jsonGetWork);
            }
        }

        public override void Disconnect()
        {
            // Release session
            _connected = false; //: atomic
            if (_session != null)
                _conn.AddDuration(_session.Duration);
            _session = null;

            _connecting = 0; //: atomic
            _txPending = 0; //: atomic
            _getworkTimer?.Dispose(); _getworkTimer = null;

            _txQueue.Clear();
            _stream.Close();

            _onDisconnected?.Invoke(Farm.F, this);
        }

        void BeginConnect()
        {
            if (_endpoints.Count != 0)
            {
                // Pick the first endpoint in list.
                // Eventually endpoints get discarded on connection errors
                _endpoint = _endpoints.First();
                _socket = new TcpClient();
                _socket.BeginConnect(_endpoint.Address, _endpoint.Port, HandleConnect, null);
            }
            else
            {
                Console.WriteLine($"No more IP addresses to try for host: {_conn.Host}");
                Disconnect();
            }
        }

        void HandleConnect(object ec)
        {
            if (ec == null && _socket.Connected)
            {
                // If in "connecting" phase raise the proper event
                if (_connecting != 0) //: atomic
                {
                    // Initialize new session
                    _connected = true; //: atomic
                    _session = new Session
                    {
                        Subscribed = true, //: atomic
                        Authorized = true, //: atomic
                    };
                    _connecting = 0; //: atomic
                    _onConnected?.Invoke(Farm.F, this);
                    _currentTstamp = DateTime.Now;
                }

                // Retrieve 1st line waiting in the queue and submit
                // if other lines waiting they will be processed at the end of the processed request
                //std::ostream os(&m_request);
                string line;
                if (_txQueue.Count != 0)
                    while ((line = _txQueue.Dequeue()) != null)
                        if (line.Length != 0)
                        {
                            _pendingJReq = JsonDocument.Parse(line)?.RootElement;
                            _pendingTstamp = DateTime.Now;

                            // Make sure path begins with "/"
                            var path = string.IsNullOrEmpty(_conn.AbsolutePath) ? "/" : _conn.AbsolutePath;
                            var os = $@"POST {path} HTTP/1.0
Host: {_conn.Host}
Content-Type: application/json
\r\n
Content-Length: {line.Length}
Connection: close

{line}";
                            // Out received message only for debug purpouses
                            //if (_logOptions & LOG_JSON)
                            Console.WriteLine($" >> {line}");
                            _stream.BeginWrite(null, 0, 1, HandleWrite, null);
                            //async_write(m_socket, m_request, m_io_strand.wrap(boost::bind(&EthGetworkClient::handle_write, this, boost::asio::placeholders::error)));
                            break;
                        }
                        else _txPending = 0; //: atomic
            }
            else if ((string)ec != "operation_aborted")
            {
                // This endpoint does not respond. Pop it and retry.
                Console.WriteLine($"Error connecting to {_conn.Host}:{_conn.Port} : "); // << ec.message();
                _endpoints.Dequeue();
                BeginConnect();
            }
        }

        void HandleWrite(object ec)
        {
            if (ec == null)
                // Transmission succesfully sent. Read the response async. 
                _stream.BeginRead(null, 0, 0, HandleRead, null);
            else if ((string)ec != "operation_aborted")
            {
                Console.WriteLine($"Error writing to {_conn.Host}:{_conn.Port} : "); // ec.message();
                _endpoints.Dequeue();
                BeginConnect();
            }
        }

        void HandleRead(object ec) //, int bytesTransferred)
        {
            //    if (ec != null || (string)ec == "eof")
            //    {
            //        // Close socket
            //        if (m_socket.is_open())
            //            m_socket.close();

            //        // Get the whole message
            //        std::string rx_message(
            //            boost::asio::buffer_cast<const char*> (m_response.data()), bytes_transferred);
            //        m_response.consume(bytes_transferred);

            //        // Empty response ?
            //        if (!rx_message.size())
            //        {
            //            cwarn << "Invalid response from " << m_conn->Host() << ":" << toString(m_conn->Port());
            //            disconnect();
            //            return;
            //        }

            //        // Read message by lines.
            //        // First line is http status
            //        // Other lines are headers
            //        // A double "\r\n" identifies begin of body
            //        // The rest is body
            //        std::string line;
            //        std::string linedelimiter = "\r\n";
            //        std::size_t delimiteroffset = rx_message.find(linedelimiter);

            //        unsigned int linenum = 0;
            //        bool isHeader = true;
            //        while (rx_message.length() && delimiteroffset != std::string::npos)
            //{
            //            linenum++;
            //            line = rx_message.substr(0, delimiteroffset);
            //            rx_message.erase(0, delimiteroffset + 2);

            //            // This identifies the beginning of body
            //            if (line.empty())
            //            {
            //                isHeader = false;
            //                delimiteroffset = rx_message.find(linedelimiter);
            //                if (delimiteroffset != std::string::npos)
            //            continue;
            //                boost::replace_all(rx_message, "\n", "");
            //                line = rx_message;
            //            }

            //            // Http status
            //            if (isHeader && linenum == 1)
            //            {
            //                if (line.substr(0, 7) != "HTTP/1.")
            //                {
            //                    cwarn << "Invalid response from " << m_conn->Host() << ":"
            //                          << toString(m_conn->Port());
            //                    disconnect();
            //                    return;
            //                }
            //                std::size_t spaceoffset = line.find(' ');
            //                if (spaceoffset == std::string::npos)
            //        {
            //                    cwarn << "Invalid response from " << m_conn->Host() << ":"
            //                          << toString(m_conn->Port());
            //                    disconnect();
            //                    return;
            //                }
            //                std::string status = line.substr(spaceoffset + 1);
            //                if (status.substr(0, 3) != "200")
            //                {
            //                    cwarn << m_conn->Host() << ":" << toString(m_conn->Port())
            //                          << " reported status " << status;
            //                    disconnect();
            //                    return;
            //                }
            //            }

            //            // Body
            //            if (!isHeader)
            //            {
            //                // Out received message only for debug purpouses
            //                if (g_logOptions & LOG_JSON)
            //                    cnote << " << " << line;

            //                // Test validity of chunk and process
            //                Json::Value jRes;
            //                Json::Reader jRdr;
            //                if (jRdr.parse(line, jRes))
            //                {
            //                    // Run in sync so no 2 different async reads may overlap
            //                    processResponse(jRes);
            //                }
            //                else
            //                {
            //                    string what = jRdr.getFormattedErrorMessages();
            //                    boost::replace_all(what, "\n", " ");
            //                    Console.WriteLine($"Got invalid Json message : {what}");
            //                }

            //            }

            //            delimiteroffset = rx_message.find(linedelimiter);
            //        }

            //        // Is there anything else in the queue
            //        if (_txQueue.Count != 0)
            //            BeginConnect();
            //        // Signal end of async send/receive operations
            //        else _txPending = 0; //: atomic
            //    }
            //    else if ((string)ec != "operation_aborted")
            //    {
            //        Console.WriteLine($"Error reading from : {_conn.Host}:{_conn.Port} : "); // ec.message();
            //        Disconnect();
            //    }
        }

        void HandleResolve(object ec)
        {
            if (ec == null)
            {
                //var 
                //while (i != tcp::resolver::iterator())
                //{
                //    m_endpoints.push(i->endpoint());
                //    i++;
                //}
                //_resolver.cancel();

                // Resolver has finished so invoke connection asynchronously
                Send(_jsonGetWork);
            }
            else
            {
                Console.WriteLine($"Could not resolve host {_conn.Host}, "); // << ec.message();
                Disconnect();
            }
        }

        void ProcessResponse(object JRes)
        {
            //unsigned _id = 0;  // This SHOULD be the same id as the request it is responding to 
            //bool _isSuccess = false;  // Whether or not this is a succesful or failed response
            //string _errReason = "";   // Content of the error reason

            //if (!JRes.isMember("id"))
            //{
            //    cwarn << "Missing id member in response from " << m_conn->Host() << ":"
            //          << toString(m_conn->Port());
            //    return;
            //}
            //// We get the id from pending jrequest
            //// It's not guaranteed we get response labelled with same id
            //// For instance Dwarfpool always responds with "id":0
            //_id = m_pendingJReq.get("id", unsigned(0)).asUInt();
            //_isSuccess = JRes.get("error", Json::Value::null).empty();
            //_errReason = (_isSuccess ? "" : processError(JRes));

            //// We have only theese possible ids
            //// 0 or 1 as job notification
            //// 9 as response for eth_submitHashrate
            //// 40+ for responses to mining submissions
            //if (_id == 0 || _id == 1)
            //{
            //    // Getwork might respond with an error to
            //    // a request. (eg. node is still syncing)
            //    // In such case delay further requests
            //    // by 30 seconds.
            //    // Otherwise resubmit another getwork request
            //    // with a delay of m_farmRecheckPeriod ms.
            //    if (!_isSuccess)
            //    {
            //        cwarn << "Got " << _errReason << " from " << m_conn->Host() << ":"
            //              << toString(m_conn->Port());
            //        m_getwork_timer.expires_from_now(boost::posix_time::seconds(30));
            //        m_getwork_timer.async_wait(
            //            m_io_strand.wrap(boost::bind(&EthGetworkClient::getwork_timer_elapsed, this,
            //                boost::asio::placeholders::error)));
            //    }
            //    else
            //    {
            //        if (!JRes.isMember("result"))
            //        {
            //            cwarn << "Missing data for eth_getWork request from " << m_conn->Host() << ":"
            //                  << toString(m_conn->Port());
            //        }
            //        else
            //        {
            //            Json::Value JPrm = JRes.get("result", Json::Value::null);
            //            WorkPackage newWp;

            //            newWp.header = h256(JPrm.get(Json::Value::ArrayIndex(0), "").asString());
            //            newWp.seed = h256(JPrm.get(Json::Value::ArrayIndex(1), "").asString());
            //            newWp.boundary = h256(JPrm.get(Json::Value::ArrayIndex(2), "").asString());
            //            newWp.job = newWp.header.hex();
            //            if (m_current.header != newWp.header)
            //            {
            //                m_current = newWp;
            //                m_current_tstamp = std::chrono::steady_clock::now();

            //                if (m_onWorkReceived)
            //                    m_onWorkReceived(m_current);
            //            }
            //            m_getwork_timer.expires_from_now(boost::posix_time::milliseconds(m_farmRecheckPeriod));
            //            m_getwork_timer.async_wait(
            //                m_io_strand.wrap(boost::bind(&EthGetworkClient::getwork_timer_elapsed, this,
            //                    boost::asio::placeholders::error)));
            //        }
            //    }

            //}
            //else if (_id == 9)
            //{
            //    // Response to hashrate submission
            //    // Actually don't do anything
            //}
            //else if (_id >= 40 && _id <= m_solution_submitted_max_id)
            //{
            //    if (_isSuccess && JRes["result"].isConvertibleTo(Json::ValueType::booleanValue))
            //        _isSuccess = JRes["result"].asBool();

            //    std::chrono::milliseconds _delay = std::chrono::duration_cast<std::chrono::milliseconds>(
            //        std::chrono::steady_clock::now() - m_pending_tstamp);

            //    const unsigned miner_index = _id - 40;
            //    if (_isSuccess)
            //    {
            //        if (m_onSolutionAccepted)
            //            m_onSolutionAccepted(_delay, miner_index, false);
            //    }
            //    else
            //    {
            //        if (m_onSolutionRejected)
            //            m_onSolutionRejected(_delay, miner_index);
            //    }
            //}
        }

        string ProcessError(object JRes)
        {
            //    std::string retVar;

            //    if (JRes.isMember("error") &&
            //        !JRes.get("error", Json::Value::null).isNull())
            //    {
            //        if (JRes["error"].isConvertibleTo(Json::ValueType::stringValue))
            //        {
            //            retVar = JRes.get("error", "Unknown error").asString();
            //        }
            //        else if (JRes["error"].isConvertibleTo(Json::ValueType::arrayValue))
            //        {
            //            for (auto i : JRes["error"])
            //            {
            //                retVar += i.asString() + " ";
            //            }
            //        }
            //        else if (JRes["error"].isConvertibleTo(Json::ValueType::objectValue))
            //        {
            //            for (Json::Value::iterator i = JRes["error"].begin(); i != JRes["error"].end(); ++i)
            //            {
            //                Json::Value k = i.key();
            //                Json::Value v = (*i);
            //                retVar += (std::string)i.name() + ":" + v.asString() + " ";
            //        }
            //    }
            //}
            //    else
            //{
            //    retVar = "Unknown error";
            //}

            //return retVar;
            return null;
        }

        void Send(object req) => Send(JsonSerializer.Serialize(req));
        void Send(string req)
        {
            var line = req;
            _txQueue.Enqueue(line);
            if (Interlocked.CompareExchange(ref _txPending, 0, 1) != 1)
                BeginConnect();
        }

        public override void SubmitHashrate(ulong rate, string id)
        {
            // No need to check for authorization
            if (_session != null)
                Send(new
                {
                    id = 9U,
                    jsonrpc = "2.0",
                    method = "eth_submitHashrate",
                    @params = new object[] {
                        "rate", id
                    }
                });
        }

        public override void SubmitSolution(Solution solution)
        {
            if (_session != null)
            {
                var id = 40 + solution.MIdx;
                _solutionSubmittedMaxId = Math.Max(_solutionSubmittedMaxId, id);
                Send(new
                {
                    id = id,
                    jsonrpc = "2.0",
                    method = "eth_submitWork",
                    @params = new object[] {
                        $"0x{solution.Nonce:x}",
                        $"0x{solution.Work.Header:x}",
                        $"0x{solution.MixHash:x}"
                    }
                });
            }
        }

        void GetworkTimerElapsed(object ec)
        {
            // Triggers the resubmission of a getWork request
            if (ec != null)
            {
                // Check if last work is older than timeout
                var delay = (DateTime.Now - _currentTstamp).TotalSeconds;
                if (delay > _workTimeout)
                {
                    Console.WriteLine($"No new work received in {_workTimeout} seconds.");
                    _endpoints.Dequeue();
                    Disconnect();
                }
                else Send(_jsonGetWork);
            }
        }
    }
}