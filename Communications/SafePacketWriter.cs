namespace Scarlet.Communications
{
    /// <summary> This is an enhanced verision of <see cref="PacketWriter"/> that will also record the type of data so the receiver can check data type consistancy. </summary>
    public class SafePacketWriter : IPacketWriter
    {
        private PacketWriter Writer; // A normal packet writer to write data into
        public Packet Packet { get { return Writer.Packet; } } // Get the constructed packet

        /// <summary> Construct a <see cref="SafePacketWriter"/> from a normal <see cref="PacketWriter"/>. </summary>
        /// <param name="Writer"> A normal <see cref="PacketWriter"/>. </param>
        public SafePacketWriter(PacketWriter Writer) { this.Writer = Writer; }

        /// <summary> Cosntruct a <see cref="SafePacketWriter"/> from an existing <see cref="Scarlet.Communications.Packet"/>. </summary>
        /// <param name="Packet"> The <see cref="Scarlet.Communications.Packet"/> to write data into. </param>
        public SafePacketWriter(Packet Packet) { this.Writer = new PacketWriter(Packet); }

        /// <summary> Construct an empty <see cref="SafePacketWriter"/> with an empty <see cref="Scarlet.Communications.Packet"/>. </summary>
        /// <param name="ID"> Packet ID. </param>
        /// <param name="IsUDP"> Whether it is an UDP packet. </param>
        public SafePacketWriter(byte ID, bool IsUDP = false, string Endpoint = null) { this.Writer = new PacketWriter(ID, IsUDP, Endpoint); }

		/// <summary> Put data into packet. </summary>
		/// <remarks> Those methods can be chained together because they return `this`. </remarks>
		/// <param name="data"> The packet to be written. </param>
		/// <returns> `this` </returns>
		public IPacketWriter Put(bool data)   { this.Writer.Put((byte)TypeID.BOOL).Put(data); return this; }
        public IPacketWriter Put(char data)   { this.Writer.Put((byte)TypeID.CHAR).Put(data); return this; }
		public IPacketWriter Put(short data) { this.Writer.Put((byte)TypeID.SHORT).Put(data); return this; }
		public IPacketWriter Put(double data) { this.Writer.Put((byte)TypeID.DOUBLE).Put(data); return this; }
        public IPacketWriter Put(float data)  { this.Writer.Put((byte)TypeID.FLOAT).Put(data); return this; }
        public IPacketWriter Put(int data)    { this.Writer.Put((byte)TypeID.INT).Put(data); return this; }
		public IPacketWriter Put(long data)   { this.Writer.Put((byte)TypeID.LONG).Put(data); return this; }
		public IPacketWriter Put(byte data)   { this.Writer.Put((byte)TypeID.BYTE).Put(data); return this; }

        public IPacketWriter Put(string data)
        {
            this.Writer.Put((byte)TypeID.STRING);
            this.Writer.Put(data.Length);
            this.Writer.Put(data);
            return this;
        }

        public IPacketWriter Put(byte[] data)
        {
            this.Writer.Put((byte)TypeID.BYTES);
            this.Writer.Put(data.Length);
            this.Writer.Put(data);
            return this;
        }
    }
}
