namespace Scarlet.Communications
{
    /// <summary> Represents data type. </summary>
    public enum TypeID
    {
        BOOL = 0,
        CHAR = 1,
        DOUBLE = 2,
        FLOAT = 3,
        INT = 4,
        LONG = 5,
        SHORT = 6,
        UINT = 7,
        ULONG = 8,
        USHORT = 9,
        BYTE = 10,
        STRING = 11,
        BYTES = 12,
        MAX = 13,
    }
	
	/// <summary>
	/// Message type for networking packets.
	/// </summary>
	public struct MessageTypeID {
		public static readonly MessageTypeID 
			TEST_ID = new MessageTypeID(43),
			CONSOLE_MESSAGE = new MessageTypeID(10),
			WATCHDOG_PING = new MessageTypeID(240);

		private readonly byte ID; //internal ID

		//public constructor necessary for NetworkDevice wrapping of received byte ID
		public MessageTypeID(byte ID) {
			this.ID = ID;
		}

		public byte ToByte() {
			return ID;
		}

		public override bool Equals(object obj) {
			if (!(obj is MessageTypeID)) {
				return false;
			}

			var iD = (MessageTypeID)obj;
			return ID == iD.ID;
		}

		public override int GetHashCode() {
			return ID;
		}
	}

    /// <summary> Represents the priority of packet, from highest to lowest. </summary>
    public enum PacketPriority
    {
        USE_DEFAULT = -1,
        EMERGENT = 0,
        HIGH = 1,
        MEDIUM = 2,
        LOW = 3,
        LOWEST = 4
    }

    public static class Constants
    {
		#region Communication Defaults
		public const ushort DEFAULT_SOCKET_PORT = 5232;
		public const int WATCHDOG_WAIT = 5000;  // ms
        public const int WATCHDOG_INTERVAL = 1000; // ms
		#endregion

		#region Reserved Packet IDs
		public const byte WATCHDOG_PING = 0xF0;
        #endregion

    }

}
