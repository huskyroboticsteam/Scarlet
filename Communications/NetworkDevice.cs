using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Scarlet.Utilities;

namespace Scarlet.Communications {

	/// <summary>
	/// <para>A NetworkDevice allows you to send and receive "messages" reliably and unreliable
	/// to and from a single remote IP address. Received messages are handled by registered
	/// delete methods for IDs, registered through the RegisterMessageParser method.</para>
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
		private readonly UdpClient socket;
		private volatile bool isConnected; //used for checking if connected to an endpoint
		
		//ids for keeping track of message sending/receiving state
		//these are not to be confused with MessageTypeID for specifying the type of data sent
		private int nextUnreliableSendID = STARTING_SEND_ID,
					nextReliableSendID = STARTING_SEND_ID,
					nextUnreliableReceiveID = STARTING_SEND_ID,
					nextReliableReceiveID = STARTING_SEND_ID;

		private readonly IList<MessageIDHolder> reliableSendQueue; //holds messages for sending

		//used for reliable message sending
		private sealed class MessageIDHolder {
			public readonly int messageID;
			public volatile bool received;

			public MessageIDHolder(int messageID) {
				this.messageID = messageID;
			}
		}

		//packet header sizes
		private const int FULL_HEADER_SIZE = 14, RESPONSE_HEADER_SIZE = 5;
		
		//initial messageID sent/received during connection
		private const int STARTING_SEND_ID = 0;

		//internal packet types
		private const byte RESPONSE_TYPE = 1, RELIABLE_TYPE = 2, UNRELIABLE_TYPE = 3;

		public delegate void MessageProcessor(DateTime sendTime, byte[] data);

		public void RegisterMessageParser(MessageTypeID messageID, MessageProcessor processor) {
			if (handlers.ContainsKey(messageID)) {
				throw new InvalidOperationException($"Message ID {messageID.ToByte()} is already registered.");
			} else {
				handlers.Add(messageID, processor);
			}
		}

		public void Close() {
			if (isConnected)
				throw new InvalidOperationException("already connected");
			socket.Close();
		}

		/// <summary>
		/// Creates a NetworkDevice and connects to a remote IP address.
		/// </summary>
		/// <param name="bind">The local address of the network interface.</param>
		/// <param name="remote">The address to connect to.</param>
		/// <returns>The connected and running NetworkDevice.</returns>
		public static NetworkDevice Start(IPEndPoint bind, IPEndPoint remote) {
			return new NetworkDevice(bind, remote);
		}

		/// <summary>
		/// Creates an unconnected NetworkDevice that will connect to the first IP
		/// address a message is received from.
		/// </summary>
		/// <param name="bind">The local address of the network interface.</param>
		/// <returns>The running NetworkDevice.</returns>
		public static NetworkDevice Start(IPEndPoint bind) {
			return new NetworkDevice(bind);
		}

		//internal constructor
		private NetworkDevice(IPEndPoint bind, IPEndPoint remote = null) {
			handlers = new Dictionary<MessageTypeID, MessageProcessor>();
			reliableSendQueue = new List<MessageIDHolder>();
			socket = new UdpClient(bind);
			if(remote != null) {
				socket.Connect(remote);
				isConnected = true;
			}
			StartReceiveThread();
		}

		private void CheckConnected() {
			if(!isConnected)
				throw new InvalidOperationException("not connected to an endpoint");
		}

		public void SendReliable(MessageTypeID ID, byte[] data) {
			SendReliable(ID, data, 10, 100);
		}

		public void SendReliable(MessageTypeID ID, byte[] data, int attempts, int resendInterval) {
			CheckConnected();

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
				lock (reliableSendQueue) { reliableSendQueue.Add(holder); }
				int attemptsRemaining = attempts;
				while (attemptsRemaining > 0 && !holder.received && isConnected) {
					socket.Send(packet, packet.Length);
					attemptsRemaining--;
					//wait for ack to be received by network controller receiver
					Monitor.Wait(holder, resendInterval);
				}

				lock (reliableSendQueue) {
					reliableSendQueue.Remove(holder);
				}

				if (!holder.received)
					throw new TimeoutException();
			}
		}

		public void SendUnreliable(MessageTypeID ID, byte[] data) {
			CheckConnected();

			//packet format:
			//type(1), messageID(4), id(1), time(8), data(remaining bytes)
			byte[] packet = new byte[FULL_HEADER_SIZE + data.Length];
			packet[0] = UNRELIABLE_TYPE;
			int messageID = nextUnreliableSendID;
			UtilData.ToBytes(messageID).CopyTo(packet, 1);
			packet[5] = ID.ToByte();
			UtilData.ToBytes(DateTime.Now.ToBinary()).CopyTo(packet, 6);
			data.CopyTo(packet, FULL_HEADER_SIZE);
			socket.Send(packet, packet.Length);

			Interlocked.Increment(ref nextUnreliableSendID);
		}

		private void StartReceiveThread() {
			new Thread(() => {
				while(true) {
					try {
						//receive packet
						IPEndPoint sender = null;
						byte[] data = socket.Receive(ref sender);

						//connect to the remote IP a message is received from
						if(!isConnected) {
							socket.Connect(sender);
							isConnected = true;
						}
						
						//deserialize data and process
						byte type = data[0];
						int messageID = UtilData.ToInt(data, 1);
						ProcessPacket(data, type, messageID);
					} catch(SocketException e) {
						if(e.SocketErrorCode == SocketError.Shutdown) {
							break;
						} else {
							Log.Output(Log.Severity.ERROR, Log.Source.NETWORK,
								$"Socket Error {e.SocketErrorCode.ToString()}");
						}
					}
				}
			}).Start();
		}

		//create a new byte array only containing message data, no header info
		private byte[] GetMessageData(byte[] packet) {
			byte[] message = new byte[packet.Length - FULL_HEADER_SIZE];
			Array.Copy(packet, FULL_HEADER_SIZE, message, 0, packet.Length - FULL_HEADER_SIZE);
			return message;
		}

		//run message parser callback
		private void HandleMessage(MessageTypeID ID, DateTime time, byte[] message) {
			if(handlers.ContainsKey(ID)) {
				handlers[ID].Invoke(time, message);
			} else {
				throw new InvalidOperationException($"No message handler for ID {ID}");
			}
		}

		private void SendResponse(int receivedMessageID) {
			byte[] packet = new byte[RESPONSE_HEADER_SIZE]; //type(1) and receivedMessageID(4)
			packet[0] = RESPONSE_TYPE;
			UtilData.ToBytes(receivedMessageID).CopyTo(packet, 1);
			socket.Send(packet, packet.Length);
		}

		private void ProcessPacket(byte[] packet, byte type, int messageID) {
			switch(type) {
				case RESPONSE_TYPE: //on receiving an acknowledgement of a remote received reliably sent packet
					lock(reliableSendQueue) {
						for(int i = 0; i < reliableSendQueue.Count; i++) {
							MessageIDHolder holder = reliableSendQueue[i]; //holder is removed by send method
							if (holder.messageID == messageID) {
								lock (holder) {
									//notify the SendReliable call that the message was received
									holder.received = true;
									Monitor.PulseAll(holder);
								}
								break;
							}
						}
					} break;
				case RELIABLE_TYPE: //on receiving a packet of reliably sent data
					if (messageID == nextReliableReceiveID) {
						//if the message is the next one, process it and update last message
						SendResponse(messageID);
						//doesnt need sync, only used by receive thread
						nextReliableReceiveID++;
						//deserialize the packet and send it to the parser
						MessageTypeID ID = new MessageTypeID(packet[5]);
						DateTime time = DateTime.FromBinary(UtilData.ToLong(packet, 6));
						HandleMessage(ID, time, GetMessageData(packet));
					} else if(messageID < nextReliableReceiveID) { //message already received
						SendResponse(messageID);
					} break; //else: message received too early
				case UNRELIABLE_TYPE: //on receiving a packet of unreliably sent data
					if (messageID >= nextUnreliableReceiveID) {
						//doesnt need sync, only used by receive thread
						nextUnreliableReceiveID++;
						//deserialize the packet and send it to the parser
						MessageTypeID ID = new MessageTypeID(packet[5]);
						DateTime time = DateTime.FromBinary(UtilData.ToLong(packet, 6));
						HandleMessage(ID, time, GetMessageData(packet));
					} break; //else: message is outdated
			}
		}
	}
}
