using Scarlet.Utilities;
using System;

namespace Scarlet.Communications
{
    /// <summary> A helper class that extract data from <see cref="Scarlet.Communications.Packet"/>. </summary>
    public class PacketScanner
    {
        private int Cursor; // The position of next unread data block
        public Packet Packet; // The packet to extract data from

        /// <summary> Construct a new scanner from <see cref="Scarlet.Communications.Packet"/>. </summary>
        /// <param name="Packet"> The packet to extract data from. </param>
        public PacketScanner(Packet Packet)
        {
            this.Packet = Packet;
            this.Cursor = 0;
        }

        /// <summary> Get next data block and move cursor forward. </summary>
        /// <param name="Size"> Size of data block in bytes. </param>
        /// <returns> Next data block in bytes. </returns>
        private byte[] NextData(int Size)
        {
            if (this.Cursor + Size > this.Packet.Data.Payload.Length) { throw new InvalidOperationException("Reached the end of packet data"); }

            byte[] Data = this.Packet.Data.GetDataSlice(this.Cursor, Size);
            this.Cursor += Size;
            return Data;
        }

        /// <summary> Interperate next data block as bool. </summary>
        /// <returns> Next data. </returns>
        public bool NextBool() { return UtilData.ToBool(NextData(sizeof(bool))); }
        
        /// <summary> Interperate next data block as short. </summary>
        /// <returns> Next data. </returns>
        public char NextChar() { return UtilData.ToChar(NextData(sizeof(char))); }

		/// <summary> Interperate next data block as char. </summary>
		/// <returns> Next data. </returns>
		public short NextShort() { return UtilData.ToShort(NextData(sizeof(short))); }

		/// <summary> Interperate next data block as double. </summary>
		/// <returns> Next data. </returns>
		public double NextDouble() { return UtilData.ToDouble(NextData(sizeof(double))); }
        
        /// <summary> Interperate next data block as float. </summary>
        /// <returns> Next data. </returns>
        public float  NextFloat() { return UtilData.ToFloat(NextData(sizeof(float))); }
        
        /// <summary> Interperate next data block as int. </summary>
        /// <returns> Next data. </returns>
        public int NextInt() { return UtilData.ToInt(NextData(sizeof(int))); }

		/// <summary> Interperate next data block as long. </summary>
		/// <returns> Next data. </returns>
		public long NextLong() { return UtilData.ToLong(NextData(sizeof(long))); }


		/// <summary> Interperate data from cursor to the end as string. </summary>
		/// <returns> Next data. </returns>
		public string NextString() {
            if(this.Cursor >= this.Packet.Data.Payload.Length)
				throw new InvalidOperationException("Reached the end of packet data");
            string data = UtilData.ToString(this.Packet.Data.GetDataSlice(Cursor));
            this.Cursor = Packet.Data.Payload.Length;
            return data;
        }
        
        /// <summary> Interperate next `Length` bytes of data as a string. </summary>
        /// <remarks> Because each char occupies 2 bytes, the returned string should have half of the length. </remarks>
        /// <param name="Length"> Length of data in bytes. </param>
        /// <returns> Next data. </returns>
        public string NextString(int Length)
        {
            if (this.Cursor >= this.Packet.Data.Payload.Length) { throw new InvalidOperationException("Reached the end of packet data"); }

            string data = UtilData.ToString(this.Packet.Data.GetDataSlice(Cursor, Length));
            this.Cursor += Length;
            return data;
        }

        /// <summary> Get data from cursor to the end. </summary>
        /// <returns> Next data. </returns>
        public byte[] NextBytes()
        {
            if (this.Cursor >= this.Packet.Data.Payload.Length) { throw new InvalidOperationException("Reached the end of packet data"); }

            byte[] data = this.Packet.Data.GetDataSlice(Cursor);
            this.Cursor = Packet.Data.Payload.Length;
            return data;
        }

        /// <summary> Get data from cursor of length `Length`. </summary>
        /// <param name="Length"> Length of data in bytes. </param>
        /// <returns> Next data. </returns>
        public byte[] NextBytes(int Length)
        {
            if (this.Cursor >= this.Packet.Data.Payload.Length) { throw new InvalidOperationException("Reached the end of packet data"); }

            byte[] data = this.Packet.Data.GetDataSlice(Cursor, Length);
            this.Cursor += Length;
            return data;
        }

        /// <summary> Interperate next data block as byte. </summary>
        /// <returns> Next byte. </returns>
        public byte NextByte()
        {
            if (this.Cursor >= this.Packet.Data.Payload.Length) { throw new InvalidOperationException("Reached the end of packet data"); }

            byte data = Packet.Data.Payload[Cursor];
            Cursor++;
            return data;
        }

        /// <summary> Get next byte without moving the cursor. </summary>
        /// <returns> Next byte. </returns>
        public byte PeekNextByte()
        {
            try { return Packet.Data.Payload[Cursor]; }
            catch (IndexOutOfRangeException) { throw new InvalidOperationException("Reached the end of packet data"); }
        }
    }
}
