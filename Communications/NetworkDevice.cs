using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Scarlet.Utilities;

namespace Scarlet.Communications {

	/// <summary>
	/// <para>A NetworkDevice allows you to send and receive "messages" reliably and unreliable
	/// to and from a single remote IP address. Received messages are handled by registered
	/// delegate methods for IDs, registered through the RegisterMessageParser method. Any
	/// unreliable messages received out of order will be dropped. NetworkDevice instances
	/// are not reusable once closed.</para>
	/// 
	/// <para>Usage: Call NetworkDevice.Start(...) with one argument to start an 'unconnected' device
	/// or with two arguments to connect to an unconnected NetworkDevice.
	/// NetworkDevice.Send(Reliable/Unreliable) to send messages. Call Close to close the connection.
	/// NetworkDevice instances are not reusable once closed.</para>
	/// 
	/// <para>Lacking functionality: Asynchronous sending, named 'clients'</para>
	/// </summary>
	public class NetworkDevice {
		private readonly Dictionary<MessageTypeID, MessageProcessor> handlers;
		private readonly IList<MessageIDHolder> reliableQueue; //holds messages for sending
		private readonly Socket socket;
		private volatile IPEndPoint remote;
		private volatile bool isConnected; //used for checking if connected to an endpoint
		
		//ids for keeping track of message sending/receiving state
		//these are not to be confused with MessageTypeID for specifying the type of data sent
		private int nextUnreliableSendID = STARTING_SEND_ID,
					nextReliableSendID = STARTING_SEND_ID,
					nextUnreliableReceiveID = STARTING_SEND_ID,
					nextReliableReceiveID = STARTING_SEND_ID;

		//used for reliable message sending
		private sealed class MessageIDHolder {
			public readonly int messageID;
			public volatile bool received;

			public MessageIDHolder(int messageID) {
				this.messageID = messageID;
			}
		}

		//packet header sizes
		private const int FULL_HEADER_SIZE = 14, RESPONSE_HEADER_SIZE = 5, CONNECT_HEADER_SIZE = 2;

		/// <summary>
		/// The maximum size in bytes that a message passed to 
		/// SendReliable or SendUnreliable can be.
		/// </summary>
		public const int MAX_MESSAGE_SIZE = 60;

		private const int MAX_PACKET_SIZE = MAX_MESSAGE_SIZE + FULL_HEADER_SIZE;
		
		//initial messageID sent/received during connection
		private const int STARTING_SEND_ID = 0;

		//internal packet types
		private const byte CONNECT_TYPE = 0, RESPONSE_TYPE = 1, RELIABLE_TYPE = 2, UNRELIABLE_TYPE = 3;

		/// <summary>
		/// Creates a NetworkDevice andb blocks until it connects to a remote IP address or times out.
		/// </summary>
		/// <param name="bind">The local address of the network interface.</param>
		/// <param name="remote">The address to connect to.</param>
		/// <param name="processors">An optional dictionary of message parsers 
		/// to register before starting the NetworkDevice.</param>
		/// <returns>The connected and running NetworkDevice.</returns>
		/// <exception cref="ConnectionFailException">Throws ConnectFailException 
		/// if the the NetworkDevice could not connect to the specified remote IP.</exception>
		public static NetworkDevice Start(IPEndPoint bind, IPEndPoint remote, IDictionary<MessageTypeID, MessageProcessor> processors = null) {
			return new NetworkDevice(bind, remote, processors);
		}

		/// <summary>
		/// Creates an unconnected NetworkDevice that will connect to the first IP
		/// address a message is received from.
		/// </summary>
		/// <param name="bind">The local address of the network interface.</param>
		/// <param name="processors">An optional dictionary of message parsers 
		/// to register before starting the NetworkDevice.</param>
		/// <returns>The running NetworkDevice.</returns>
		public static NetworkDevice Start(IPEndPoint bind, IDictionary<MessageTypeID, MessageProcessor> processors = null) {
			return new NetworkDevice(bind, null, processors);
		}
		
		//internal constructor
		private NetworkDevice(IPEndPoint bind, IPEndPoint remote, IDictionary<MessageTypeID, MessageProcessor> processors) {
			if (remote != null && bind.AddressFamily != remote.AddressFamily) {
				throw new InvalidOperationException("bind and remote addresses must use the same protocol");
			}

			handlers = processors == null ? 
				new Dictionary<MessageTypeID, MessageProcessor>() : 
				new Dictionary<MessageTypeID, MessageProcessor>(processors);
			reliableQueue = new List<MessageIDHolder>();
			socket = new Socket(bind.Address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
			socket.Bind(bind);
			new Thread(RunReceive).Start();
			if (remote != null && !Connect(remote, 10, 100)) {
				socket.Shutdown(SocketShutdown.Both);
				throw new ConnectionFailException();
			}
		}

		public sealed class ConnectionFailException : Exception { }

		private bool Connect(IPEndPoint remote, int attempts, int resendInterval) {
			this.remote = remote;
			for(int attemptsRemaining = attempts; 
				attemptsRemaining > 0 && !isConnected && remote != null; 
				attemptsRemaining--) {
				SendConnect(remote, true);
				lock(socket) {
					Monitor.Wait(socket, resendInterval);
				}
			}
			return isConnected;
		}

		private void SendConnect(IPEndPoint remote, bool query) {
			socket.SendTo(new byte[] { CONNECT_TYPE, (byte)(query ? 1 : 0)}, remote);
		}

		/// <summary>
		/// Parses a received message.
		/// </summary>
		/// <param name="sendTime">The time the message was sent by the connected IP.</param>
		/// <param name="data">The message data.</param>
		public delegate void MessageProcessor(DateTime sendTime, byte[] data);

		/// <summary>
		/// Registers a MessageProcessor with the NetworkDevice.
		/// When a message is received, message receiving will block until the processor returns.
		/// </summary>
		/// <param name="messageID">The message type that this message parser can handle</param>
		/// <param name="processor">The parser callback (delegate) method</param>
		public void RegisterMessageParser(MessageTypeID messageID, MessageProcessor processor) {
			lock(handlers) {
				if (handlers.ContainsKey(messageID)) {
					throw new InvalidOperationException($"Message ID {messageID.ToByte()} is already registered.");
				} else {
					handlers.Add(messageID, processor);
				}
			}
		}

		/// <summary>
		/// Closes the internal socket. Does not notify the connected IP.
		/// </summary>
		public void Close() {
			socket.Close();
			isConnected = false;
			remote = null;
		}

		private void CheckConnected() {
			if(!isConnected) {
				throw new InvalidOperationException("not connected to an endpoint");
			}
		}

		private void CheckMessageSize(byte[] message) {
			if(message.Length > MAX_MESSAGE_SIZE) {
				throw new InvalidOperationException(
					$"message {message.Length - MAX_MESSAGE_SIZE} " +
					$"bytes too large. {MAX_MESSAGE_SIZE} maximum allowed.");
			}
		}

		/// <summary>
		/// Sends a packet reliably. Attempts to send the packet 10 times
		/// with 100 milliseconds between each attempt before throwing
		/// a TimeoutException. Thread-safe.
		/// </summary>
		/// <param name="ID">The message type</param>
		/// <param name="data">The message data</param>
		/// <exception cref="TimeoutException">Throws TimeoutException if
		/// a packet is not received after <paramref name="attempts"/> send attempts.</exception>
		public void SendReliable(MessageTypeID ID, byte[] data) {
			SendReliable(ID, data, 10, 100);
		}

		/// <summary>
		/// Sends a packet reliably. 
		/// Allows specification of send interval and attempts. Thread-safe.
		/// </summary>
		/// <param name="ID">The message type.</param>
		/// <param name="data">The message data.</param>
		/// <param name="attempts">The number of send attempts.</param>
		/// <param name="resendInterval">The time between resends.</param>
		/// <exception cref="TimeoutException">Throws TimeoutException if
		/// a packet is not received after <paramref name="attempts"/> send attempts.</exception>
		public void SendReliable(MessageTypeID ID, byte[] data, int attempts, int resendInterval) {
			CheckConnected();
			CheckMessageSize(data);

			//packet format:
			//type(1), messageID(4), id(1), time(8), data(remaining bytes)
			byte[] packet = new byte[FULL_HEADER_SIZE + data.Length];
			packet[0] = RELIABLE_TYPE;
			int messageID = nextReliableSendID;
			UtilData.ToBytes(messageID).CopyTo(packet, 1);
			packet[5] = ID.ToByte();
			UtilData.ToBytes(DateTime.Now.ToBinary()).CopyTo(packet, 6);
			data.CopyTo(packet, FULL_HEADER_SIZE);
			MessageIDHolder holder = new MessageIDHolder(messageID);

			//increment message id (but don't use it for this packet)
			Interlocked.Increment(ref nextReliableSendID);

			lock (holder) {
				lock (reliableQueue) { reliableQueue.Add(holder); }
				int attemptsRemaining = attempts;
				while (attemptsRemaining > 0 && !holder.received && isConnected) {
					socket.SendTo(packet, remote);
					attemptsRemaining--;
					//wait for ack to be received by network controller receiver
					Monitor.Wait(holder, resendInterval);
				}

				lock (reliableQueue) {
					reliableQueue.Remove(holder);
				}

				if (!holder.received) {
					throw new TimeoutException();
				}
			}
		}

		/// <summary>
		/// Sends a packet without ensuring it is received. Thread-safe.
		/// </summary>
		/// <param name="ID">The message type</param>
		/// <param name="data">The message data</param>
		public void SendUnreliable(MessageTypeID ID, byte[] data) {
			CheckConnected();
			CheckMessageSize(data);

			//packet format:
			//type(1), messageID(4), id(1), time(8), data(remaining bytes)
			byte[] packet = new byte[FULL_HEADER_SIZE + data.Length];
			packet[0] = UNRELIABLE_TYPE;
			int messageID = nextUnreliableSendID;
			UtilData.ToBytes(messageID).CopyTo(packet, 1);
			packet[5] = ID.ToByte();
			UtilData.ToBytes(DateTime.Now.ToBinary()).CopyTo(packet, 6);
			data.CopyTo(packet, FULL_HEADER_SIZE);
			socket.SendTo(packet, remote);
			//increment the send ID counter
			Interlocked.Increment(ref nextUnreliableSendID);
		}

		//create a new byte array only containing message data, no header info
		private byte[] GetMessageData(byte[] packet, int packetLength) {
			int messageLength = packetLength - FULL_HEADER_SIZE;
			byte[] message = new byte[messageLength];
			Array.Copy(packet, FULL_HEADER_SIZE, message, 0, messageLength);
			return message;
		}

		//run message parser callback
		private void HandleMessage(MessageTypeID ID, DateTime time, byte[] message) {
			MessageProcessor processor;
			lock(handlers) {
				if(handlers.ContainsKey(ID)) {
					processor = handlers[ID];
				} else {
					throw new InvalidOperationException($"No message handler for ID {ID}");
				}
			}
			processor.Invoke(time, message);
		}

		//sends reliable message received ack pakcet
		private void SendResponse(int receivedMessageID) {
			byte[] packet = new byte[RESPONSE_HEADER_SIZE]; //type(1) and receivedMessageID(4)
			packet[0] = RESPONSE_TYPE;
			UtilData.ToBytes(receivedMessageID).CopyTo(packet, 1);
			socket.SendTo(packet, remote);
		}

		//can be started on a thread to constantly receive and process messages
		private void RunReceive() {
			byte[] buffer = new byte[MAX_PACKET_SIZE];

			//this is literally the dumbest thing, 
			//I have to provide a placeholder address to ReceiveFrom for no apparent reason
			EndPoint placeholder = new IPEndPoint(
				socket.AddressFamily == AddressFamily.InterNetwork ? 
				IPAddress.None : IPAddress.IPv6None, 0);

			while (true) {
				try {
					//receive packet
					EndPoint sender = placeholder;
					int length = socket.ReceiveFrom(buffer, ref sender);
					//deserialize data and process
					ProcessPacket(buffer, length, (IPEndPoint)sender);
				} catch (SocketException e) {
					if (e.SocketErrorCode == SocketError.Interrupted) {
						break;
					} else {
						Log.Output(Log.Severity.ERROR, Log.Source.NETWORK,
							$"Socket Error {e.SocketErrorCode.ToString()}: {e.Message}");
					}
				}
			}
		}

		private void ProcessPacket(byte[] packet, int length, IPEndPoint sender) {
			byte type = packet[0];

			switch(type) {
				case CONNECT_TYPE: {
						bool isQuery = packet[1] == 1 ? true : false;
						if (isQuery) {
							if (remote == null) {
								remote = sender;
								isConnected = true;
								SendConnect(sender, false); //send ack
							} else if (sender.Equals(remote)) {
								SendConnect(sender, false); //send ack
							}
						} else if(!isConnected && remote != null && sender.Equals(remote)) {
							isConnected = true;
							lock (socket) {
								Monitor.PulseAll(socket);
							}
						}
						break;
					}
				//on receiving an acknowledgement of a remote received reliably sent packet
				case RESPONSE_TYPE: {
						int messageID = UtilData.ToInt(packet, 1);
						lock (reliableQueue) {
							for (int i = 0; i < reliableQueue.Count; i++) {
								MessageIDHolder holder = reliableQueue[i]; //holder is removed by send method
								if (holder.messageID == messageID) {
									lock (holder) {
										//notify the SendReliable call that the message was received
										holder.received = true;
										Monitor.PulseAll(holder);
									}
									break;
								}
							}
						}
						break;
					}
				//on receiving a packet of reliably sent data
				case RELIABLE_TYPE: {
						int messageID = UtilData.ToInt(packet, 1);
						if (messageID == nextReliableReceiveID) {
							//if the message is the next one, process it and update last message
							SendResponse(messageID);
							//doesnt need sync, only used by receive thread
							nextReliableReceiveID++;
							//deserialize the packet and send it to the parser
							MessageTypeID ID = new MessageTypeID(packet[5]);
							DateTime time = DateTime.FromBinary(UtilData.ToLong(packet, 6));
							HandleMessage(ID, time, GetMessageData(packet, length));
						} else if (messageID < nextReliableReceiveID) { //message already received
							SendResponse(messageID);
						}
						break; //else: message received too early
					}
				//on receiving a packet of unreliably sent data
				case UNRELIABLE_TYPE: {
						int messageID = UtilData.ToInt(packet, 1);
						if (messageID >= nextUnreliableReceiveID) {
							//doesnt need sync, only used by receive thread
							nextUnreliableReceiveID++;
							//deserialize the packet and send it to the parser
							MessageTypeID ID = new MessageTypeID(packet[5]);
							DateTime time = DateTime.FromBinary(UtilData.ToLong(packet, 6));
							HandleMessage(ID, time, GetMessageData(packet, length));
						}
						break; //else: message is outdated
					}
			}
		}
	}
}
