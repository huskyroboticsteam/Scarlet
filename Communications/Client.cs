using Scarlet.Utilities;

using System;
using System.Collections.Generic;
using System.Net;
using System.Linq;
using System.Net.Sockets;
using System.Threading;

namespace Scarlet.Communications
{
    /// <summary>
    /// Client is a class used to network a client to a server.
    /// Please read the Scarlet documentation before use.
    /// </summary>
    public static class Client
    {

        #region Private Variables

        private const int TCP_CONNECTION_TIMEOUT = 2000; // Timeout (in ms) for TcpClient.BeginConnect()

        private static IPEndPoint ServerEndpointTCP; // Endpoints for TCP and UDP
        private static IPEndPoint ServerEndpointUDP;
        private static UdpClient ServerUDP; // TCP and UDP Clients
        private static TcpClient ServerTCP;

        private static PacketBuffer SendQueue; // Buffer of packets waiting to be sent
        private static QueueBuffer ReceiveQueue; // Buffer of received packets waiting to be processed
        public static PacketPriority DefaultPriority { get; private set; } // Default packet priority

        private static Thread SendThread, ProcessThread; // Threads for sending and parsing/processing
        private static Thread ReceiveThreadUDP, ReceiveThreadTCP; // Threads for receiving on TCP and UDP
        private static Thread RetryConnection; // Retries Server connection after detecting disconnect. Only runs in disconnected state
        private static Thread RetryStartup; // Retries Client startup to establish connection with the Server

        private static int ReceiveBufferSize; // Size of the receive buffer. Change this in Start() to receive larger packets
        private static int OperationPeriod; // The operation period (the higher this is, the longer the wait in between receiving/sending packets)
        private static bool Initialized; // Whether or not the client is initialized
        private static bool StartedUp; // Whether or not the client has started (different from Initialized, because Initialize() invokes the startup prcoess)
        private static volatile bool StopProcesses; // If true, stops all threading processes
        private static bool HasDetectedDisconnect; // To be set if there is a send or receive error that would likely be caused by a disconnect.
        private static bool ThreadsStarted; // Whether or not the send/receive/process threads have started

		#endregion

		public static string Name { get; private set; } // Name of the client.
        public static bool IsConnected { get; private set; } // Whether or not the client and server are connected
        public static event EventHandler<ConnectionStatusChanged> ClientConnectionChanged; // Invoked when the Client detects a connection change

        public static bool StorePackets; // Whether or not the client stores packets
        public static List<Packet> SentPackets { get; private set; } // Storage for send packets.
        public static List<Packet> ReceivedPackets { get; private set; } // Storage for received packets.

        /// <summary> Starts a Client process. </summary>
        /// <param name="ServerIP"> String representation of the IP Address of server.</param>
        /// <param name="PortTCP"> Target port for TCP Communications on the server.</param>
        /// <param name="PortUDP"> Target port for UDP Communications on the server.</param>
        /// <param name="Name"> Name of client. </param>
        /// <param name="ReceiveBufferSize"> Size of buffer for receving incoming packet. Unit: byte. Increase this if you are receiving larger packets. </param>
        /// <param name="OperationPeriod"> Time to wait after receiving or sending each packet. Unit: ms. Decrease this if the send/receive operations cannot keep up with your data rate. </param>
        /// <param name="UsePriorityQueue"> If it is true, packet priority control will be enabled, and packets will be sent in an order corresponding to their importance. If false, packets are sent in the order that they are provided. </param>
        public static void Start(string ServerIP, 
			int PortTCP, 
			int PortUDP, 
			string Name, 
			int ReceiveBufferSize = 64, 
			int OperationPeriod = 20, 
			bool UsePriorityQueue = false)
        {
            // Initialize PacketHandler
            PacketHandler.Start();
            // Output to debug that the client is starting up
            Log.Output(Log.Severity.DEBUG, Log.Source.NETWORK, "Initializing Client.");
            // Set local variables given the parameters
            Client.Name = Name;
            Client.ReceiveBufferSize = ReceiveBufferSize;
            Client.OperationPeriod = OperationPeriod;
            IPAddress ServerIPA = IPAddress.Parse(ServerIP);
            // Setup Endpoints for TCP and UDP
            ServerEndpointTCP = new IPEndPoint(ServerIPA, PortTCP);
            ServerEndpointUDP = new IPEndPoint(ServerIPA, PortUDP);
            if (!Initialized)
            {
                // Setup Watchdog
                // Client watchdog manager is automatically started
                // when it receives the first watchdog from the server.
                // Subscribe to the watchdog manager
                WatchdogManager.ConnectionChanged += ConnectionChange;

                // Initialize the receiving queue
                ReceiveQueue = new QueueBuffer();

                // Initialize sending queue and default priority
                if (UsePriorityQueue)
                {
                    SendQueue = new GenericController();
                    DefaultPriority = PacketPriority.MEDIUM;
                }
                else
                {
                    SendQueue = new QueueBuffer();
                    DefaultPriority = 0;
                }

                // Initialize packet storage structures
                SentPackets = new List<Packet>();
                ReceivedPackets = new List<Packet>();

                // Initialize (but do not start the threads)
                SendThread = new Thread(new ThreadStart(SendPackets));
                ProcessThread = new Thread(new ThreadStart(ProcessPackets));
                RetryConnection = RetryConnectionThreadFactory();
                ReceiveThreadTCP = new Thread(new ParameterizedThreadStart(ReceiveFromSocket));
                ReceiveThreadUDP = new Thread(new ParameterizedThreadStart(ReceiveFromSocket));

                // Initialize client for the first time on a new thread so that it doesn't block
                new Thread(new ThreadStart(InitialStartup)).Start();
                // Set the state of client to initialized.
                Initialized = true;
            }
        }

        #region Internal

        /// <summary> Event triggered when WatchdogManager detects a change in connection status. </summary>
        /// <param name="Sender"> If triggered internally by WatchdogManager, this will be "Watchdog Timer" </param>
        /// <param name="Args"> A ConnectionStatusChanged object containing the new status of the Client. </param>
        private static void ConnectionChange(object Sender, ConnectionStatusChanged Args)
        {
            // Whether or not the connection has changed state (this should usually be true, unless connecting for the first time)
            bool ChangedState = Args.StatusConnected ^ IsConnected;
            // Set the connection state of Client
            IsConnected = Args.StatusConnected;
            // Invoke a ClientConnectionChanged event (for end-user connection subscribers)
            if (ChangedState) { ClientConnectionChanged?.Invoke(Sender, Args); }
            // Outut the state if the state has changed from false to true
            if (ChangedState && IsConnected) { ConnectionAliveOutput(true); }
            // If the threads are still alive, then join the current thread
            if (RetryStartup != null && RetryStartup.IsAlive) { RetryStartup.Join(); }
            if (RetryConnection != null && RetryConnection.IsAlive) { RetryConnection.Join(); }
            // Determine if we are connecting or reconnecting
            if (!IsConnected && ThreadsStarted)
            {
                // Output the current connection status (disconnected) to the console
                ConnectionAliveOutput(false);
                // If the thread is alive, then join the current connection thread
                if (RetryConnection.IsAlive) { RetryConnection.Join(); }
                // Get a new thread from the factory and start retrying the connection
                // Using a factory because you cannot restart a thread
                RetryConnection = RetryConnectionThreadFactory();
                RetryConnection.Start();
            }
            else if (!IsConnected && !ThreadsStarted)
            {
                // Get a new thread from the thread factory
                RetryStartup = RetryStartupThreadFactory();
                // Start the retry thread
                RetryStartup.Start();
                // Output the current connectionstatus (disconnected) to the console
                ConnectionAliveOutput(false);
            }
        }

        /// <summary>
        /// Logs to the console the appropriate information if the connection is alive or dead. 
        /// * Will tell the console that the connection will be retried if the connection is not alive.
        /// </summary>
        /// <param name="IsAlive"> Whether or not the connection is now alive </param>
        private static void ConnectionAliveOutput(bool IsAlive)
        {
            // Tell the console that the server is connected if the connection is alive
            // Otherwise tell the console that it is disconnected and is retrying to connect.
            if (IsAlive) { Log.Output(Log.Severity.INFO, Log.Source.NETWORK, "Server Connected..."); }
            else { Log.Output(Log.Severity.ERROR, Log.Source.NETWORK, "Disconnected from server... Retrying..."); }
        }

        /// <summary> Starts up the Client </summary>
        private static void Startup()
        {
            // Initialize the TCP and UDP clients
            try { InitializeClients(); }
            catch { throw new Exception("Could not initialize TCP and UDP client communication with server."); }
            // Send names to the server
            try { SendNames(); }
            catch { throw new InvalidOperationException("Could not begin communication with Server."); }
            // Start all primary thread procedures
            StartThreads();
            ThreadsStarted = true;
            // Invoke the Client event for a connection change using the known endpoint "Server" per the Client specification
            ConnectionStatusChanged Event = new ConnectionStatusChanged() { StatusConnected = true, StatusEndpoint = "Server" };
            ConnectionChange(Name, Event);
        }

        /// <summary>
        /// Initializes the client for the FIRST time.
        /// Only call this method once.
        /// </summary>
        private static void InitialStartup()
        {
            if (StartedUp) { return; }
            // Try to startup the client
            try { Startup(); }
            catch // If it can't, trigger a connection change event to false
            {
                // Invoke a Client event for a connection change (to disconnected) using "Server", known per the Client specification
                ConnectionStatusChanged Event = new ConnectionStatusChanged() { StatusConnected = false, StatusEndpoint = "Server" };
                ConnectionChange(Name, Event);
            }
            StartedUp = true;
        }

        /// <summary> Retries sending names to the server until the Watchdog Manager finds watchdog ping again. </summary>
        private static void RetryConnecting()
        {
            while (!IsConnected)
            {
                // Initialized the Client connections
                // (retries the connection establishment)
                try
                {
                    // Both these commands are allowed to fail
                    // Initialize the TCP connection
                    // We only need to initialize the TCP connection if the TCP connection has ever began, otherwise server will open a TCP socket
                    InitializeTCPClient();
                    // Send the name of the client on the TCP and UDP bus
                    SendNames();
                }
                catch { }
                // Sleep longer than the operation period to reconnect
                // We do not need to reconnect with that much speed.
                Thread.Sleep(Constants.WATCHDOG_WAIT);
            }
        }

        /// <summary> Retry Startup until connection is established </summary>
        private static void RetryStart()
        {
            // While the Client is not connected
            while (!IsConnected)
            {
                // Try starting up the client
                // Startup is allowed to fail because if we do not have a valid connection, then we will get an exception
                try { Startup(); }
                catch { }
                // Wait some amount of time
                Thread.Sleep(Constants.WATCHDOG_WAIT);
            }
        }

        /// <summary>
        /// Assumes TCP and UCP clients are connected.
        /// Sends name to initialize a connection with server.
        /// </summary>
        private static void SendNames()
        {
            byte[] SendData = UtilData.ToBytes(Name);
            ServerUDP.Client.Send(SendData);
            ServerTCP.Client.Send(SendData);
        }

        /// <summary> Initializes the Client connections </summary>
        private static void InitializeClients()
        {
            // Initialize and connect to the UDP and TCP clients
            InitializeTCPClient();
            ServerUDP = new UdpClient();
            ServerUDP.Connect(ServerEndpointUDP);
        }

        /// <summary> Initializes TCP Client connection </summary>
        private static void InitializeTCPClient()
        {
            // Initialize and connect to the UDP and TCP clients
            ServerTCP = new TcpClient();
            // Begin connection process
            IAsyncResult TCPConnection = ServerTCP.BeginConnect(ServerEndpointTCP.Address, ServerEndpointTCP.Port, null, null);
            // Wait for timeout period
            bool Success = TCPConnection.AsyncWaitHandle.WaitOne(TCP_CONNECTION_TIMEOUT);
            // Throw an exception if it could not connect in the given timeout
            if (!Success) { ServerTCP.Close(); throw new SocketException(); }
            // These parameters help avoid double-buffering
            ServerTCP.NoDelay = true;                           // Sets the TCP Client to have send delay (i.e. stores no overflow bits in the buffer)
            ServerTCP.ReceiveBufferSize = ReceiveBufferSize;    // Sets the receive buffer size to the max buffer size
        }

        /// <summary> Starts the threads for the receive, send, and processing systems. </summary>
        private static void StartThreads()
        {
            SendThread.Start();                         // Start sending packets
            ProcessThread.Start();                      // Begin processing packets
            ReceiveThreadTCP.Start(ServerTCP.Client);   // Start receiving on the TCP socket
            ReceiveThreadUDP.Start(ServerUDP.Client);   // Start receiving on the UDP socket
        }

        /// <summary> Creates a new thread for handling connection retries </summary>
        /// <returns> New RetryConnecting Thread </returns>
        private static Thread RetryConnectionThreadFactory() { return new Thread(new ThreadStart(RetryConnecting)); }

        /// <summary> Creates a new thread for handling startup retries </summary>
        /// <returns> New RetryStart Thread </returns>
        private static Thread RetryStartupThreadFactory() { return new Thread(new ThreadStart(RetryStart)); }

        #endregion

        #region Receive

        /// <summary>
        /// Receives packets from a given socket
        /// Object type parameter, because it must be if called from a thread.
        /// </summary>
        /// <param name="ReceiveSocket"> The socket to recieve from. </param>
        private static void ReceiveFromSocket(object ReceiveSocket)
        {
            // Cast to a socket
            Socket Socket = (Socket)ReceiveSocket;
            // While we need to continue receiving
            while (!StopProcesses)
            {
                // Sleep for the operation period
                Thread.Sleep(OperationPeriod);
                // Checks if the client is connected and if
                // the server has available data
                if (Socket.Available > 0)
                {
                    // Buffer for the newly received data
                    byte[] ReceiveBuffer = new byte[ReceiveBufferSize];
                    try
                    {
                        // Receives data from the server, and stored the length 
                        // of the received data in bytes.
                        int Size = Socket.Receive(ReceiveBuffer, ReceiveBuffer.Length, SocketFlags.None);
                        // Check if data has correct header
                        if (Size < 4) { Log.Output(Log.Severity.ERROR, Log.Source.NETWORK, "Incoming Packet is Corrupt"); }
                        // Parses the data into a message
                        ReceiveBuffer = ReceiveBuffer.Take(Size).ToArray();
                        Packet Received = Packet.FromBytes(ReceiveBuffer, Socket.ProtocolType);
                        // Set the packet endpoint to "Server", because that is where it originated
                        Received.Endpoint = "Server";
                        // Queues the packet for processing
                        ReceiveQueue.Enqueue(Received);
                        // Check if the client is storing packets
                        if (StorePackets)
                        {
                            // Lock the packet store
                            // Add the received packet into the store
                            lock (ReceivedPackets) { ReceivedPackets.Add((Packet)Received.Clone()); }
                        }
                        // Set the state of client to have not detected a disconnect or network fault
                        HasDetectedDisconnect = false;
                    }
                    catch (Exception Exception) // Catch any exception
                    {
                        // If the client is connected and has not detected a disconnect or network fault, output the fault to log
                        // (The if check is only there so we don't bog the console)
                        if (IsConnected && !HasDetectedDisconnect)
                        {
                            Log.Output(Log.Severity.ERROR, Log.Source.NETWORK, "Failed to receive from socket. Check network connectivity.");
                            Log.Exception(Log.Source.NETWORK, Exception);
                            HasDetectedDisconnect = true;
                        }
                    }
                }
            }

        }

        /// <summary> Processes packets/sends them to the parsing system. </summary>
        private static void ProcessPackets()
        {
            // While we need to continue the processing
            while (!StopProcesses)
            {
                Packet CurrentPacket = ReceiveQueue.Dequeue();
                if (CurrentPacket != null)
                {
                    // Make sure endpoint is set
                    CurrentPacket.Endpoint = CurrentPacket.Endpoint ?? "Server";

                    // Parses the message
                    Parse.ParseMessage(CurrentPacket);
                }

                // Sleep for the operation period
                Thread.Sleep(OperationPeriod);
            }
        }

        #endregion

        #region Send

        /// <summary>
        /// Sends a packet. Handles both UDP and TCP.
        /// Places the packet into sending queue to wait to be sent.
        /// </summary>
        /// 
        /// <remark> Please use SendNow for packets that need to be send immediately. </remark>
        /// 
        /// <param name="SendPacket"> Packet to send. </param>
        /// <returns> true if packet is successfully added to queue. false otherwise. </returns>
        public static bool Send(Packet SendPacket, PacketPriority Priority = PacketPriority.USE_DEFAULT)
        {
            // Use default priority if needed
            if (Priority == PacketPriority.USE_DEFAULT) { Priority = DefaultPriority; }

            // Check initialization status of Client.
            if (!Initialized) { throw new InvalidOperationException("Client not initialized. Please call Client.Start() to establish connection. first"); }

            // Check if we have stopped the process
            if (StopProcesses) { return false; }
            else
            {
                // Ensure that cloning the packet will not try to clone a null string
                SendPacket.Endpoint = SendPacket.Endpoint ?? "Server";
                
                // Add cloned packet to the send queue
                SendQueue.Enqueue((Packet)SendPacket.Clone(), (int) Priority);
                return true;
            }
        }

        /// <summary> Sends a packet asynchronously, handles both UDP and TCP Packets. </summary>
        /// <param name="SendPacket"> Packet to send. </param>
        /// <returns> Success of packet sending. </returns>
        public static bool SendNow(Packet SendPacket)
        {
            // Check initialization status of Client
            if (!Initialized) { throw new InvalidOperationException("Cannot use client before initialization. Call Client.Start();"); }
            // Checks the connection status of client
            // And sends the packet if a connection is existing
            if (IsConnected) { return SendRegardless(SendPacket); }
            else { return false; }
        }

        /// <summary>
        /// Assumes IsConnected is true.
        /// Sends a packet to the Server UDP port.
        /// </summary>
        /// <param name="UDPPacket"> The Packet to send via UDP </param>
        /// <returns> True always, because there is no way to detect a successful UDP transmission </returns>
        private static bool SendUDPNow(Packet UDPPacket)
        {
            // Sends the data as a byte array
            byte[] Data = UDPPacket.Data.GetRawData();
            // Update the timestamp if it doesn't exist
            if (UDPPacket.Data.Timestamp == null) { UDPPacket.UpdateTimestamp(); }
            // Send the UDP data
            ServerUDP.Send(Data, Data.Length);
            // Returns true always, because there is no way to detect if a UDP message is received
            return true;
        }

        /// <summary>
        /// Assumes IsConnected is true.
        /// Sends a packet to the Server TCP port.
        /// </summary>
        /// <param name="TCPPacket"> The Packet to send via TCP </param>
        /// <returns> The success of the TCP transmission </returns>
        private static bool SendTCPNow(Packet TCPPacket)
        {
            // Get the packet's raw data
            byte[] Data = TCPPacket.Data.GetRawData();
            // Set the TCP Send buffer to the size of the data to avoid double buffering
            ServerTCP.SendBufferSize = Data.Length;
            // Update the timestamp if it doesn't exist
            if (TCPPacket.Data.Timestamp == null) { TCPPacket.UpdateTimestamp(); }
            // Try to send the TCP data to the client
            try { ServerTCP.Client.Send(Data); }
            catch (Exception Exception) // Catch any exception, but return that the transmission has failed
            {
                // If the client has not detected a possible disconnect log the exception
                if (!HasDetectedDisconnect) { Log.Exception(Log.Source.NETWORK, Exception); }
                // Reset the state of client to having detected a disconnect or network fault
                HasDetectedDisconnect = true;
                // Return the send process failed
                return false;
            }
            // (Re)set the state of client to have not detected a disconnect or network fault
            HasDetectedDisconnect = false;
            return true; // Return that the send process was successful
        }

        /// <summary> Iteratively sends packets to the server from the send queue. </summary>
        private static void SendPackets()
        {
            while (!StopProcesses)
            {
                Packet ToSend = SendQueue.Dequeue(); // Get next packet for sending

                // Send packet only if packet is not empty (i.e. buffer is not empty)
                if (ToSend != null)
                {
                    try { SendNow(ToSend); } // Try to send the packet
                    catch (Exception Exception)
                    {
                        if (IsConnected) // Log an exception if the client is supposedly connected (so we don't bog down the console)
                        {
                            Log.Output(Log.Severity.ERROR, Log.Source.NETWORK, "Failed to send packet. Check connection status.");
                            Log.Exception(Log.Source.NETWORK, Exception);
                        }
                    }
                }

                // Sleep for the operation period
                Thread.Sleep(OperationPeriod);
            }
        }

        /// <summary>
        /// Sends an error packet to the server via
        /// TCP.
        /// Adds Error packet to packet send queue
        /// </summary>
        /// <param name="ErrorPacketID">ID for the Error Packet</param>
        /// <param name="ErrorCode">Error code for the indicated error</param>
        public static void SendError(byte ErrorPacketID, int ErrorCode)
        {
            Packet ErrorPacket = new Packet(ErrorPacketID, false);
            ErrorPacket.AppendData(UtilData.ToBytes(ErrorCode));
            Send(ErrorPacket);
        }

        /// <summary> Sends a Packet regardless of the connection status of Client. This method will throw an exception if you try and send a TCP exception without IsConnected being true. </summary>
        /// <param name="SendPacket"> The Packet to send </param>
        /// <returns> Whether or not the packet was sent. </returns>
        internal static bool SendRegardless(Packet SendPacket)
        {
            // Check to make sure that you are not sending a TCP connection without an established connection
            if (SendPacket.IsUDP || IsConnected)
            {
                // Check if the Client is storing packets
                // If so, lock that packet store and add the packet into the list
                if (StorePackets)
                {
                    lock (SentPackets) { SentPackets.Add(SendPacket); }
                }
                // Chooses the correct send method for the type of packet (TCP/UDP)
                if (SendPacket.IsUDP) { return SendUDPNow(SendPacket); }
                else { return SendTCPNow(SendPacket); }
            }
            else
            {
                // Cannot send a TCP message without an established connection
                throw new InvalidOperationException("Must have a known, established connection to send a TCP packet.");
            }
        }

        #endregion

        #region Info and Control

        /// <summary>
        /// Stops the Client.
        /// Removes all packets from the receuve and send queues.
        /// Changes the initialization state of the Client.
        /// </summary>
        public static void Stop()
        {
            StopProcesses = true;
            if (ThreadsStarted)
            {
                SendThread.Join();
                ProcessThread.Join();
                ReceiveThreadTCP.Join();
                ReceiveThreadUDP.Join();
            }
            Initialized = false;
        }

        /// <summary> Gets the length of the current receive queue. i.e. The number of packets that have yet to be parsed. </summary>
        /// <returns> The length of the recieve queue </returns>
        public static int GetReceiveQueueCount() { return ReceiveQueue.Count; }

        /// <summary> Gets the length of the current send queue. i.e. The number of packets that have yet to be sent. </summary>
        /// <returns> The length of the send queue </returns>
        public static int GetSendQueueCount() { return SendQueue.Count; }

        #endregion

    }
}
