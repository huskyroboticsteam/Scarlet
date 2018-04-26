using System;

namespace Scarlet.Communications
{
    /// <summary> This is an enhanced version of <see cref="PacketScanner"/> that can check data type consistancy when reading data. </summary>
    public class SafePacketScanner
    {
        private PacketScanner Scanner; // Scanner to read data from

        /// <summary> Construct <see cref="SafePacketScanner"/> from a normal <see cref="PacketScanner"/>. </summary>
        /// <param name="Scanner"> A normal packet scanner. </param>
        public SafePacketScanner(PacketScanner Scanner) { this.Scanner = Scanner; }

        /// <summary> Construct <see cref="SafePacketScanner"/> from an existing <see cref="Packet"/>. </summary>
        /// <param name="Packet"> The packet to read data from. </param>
        public SafePacketScanner(Packet Packet) { this.Scanner = new PacketScanner(Packet); }

        /// <summary> Check if there's next data block available. </summary>
        /// <param name="Type"> The expected type of next data block. </param>
        /// <returns> true if there is data of expected type; false otherwise. </returns>
        public bool HasNext(TypeID Type)
        {
            try { return (byte)Type == Scanner.PeekNextByte(); }
            catch (InvalidOperationException) { return false; }
        }

        /// <summary> Throw <see cref="InvalidOperationException"/> if type of next data block is not expected type. </summary>
        /// <param name="Type"> Expected type. </param>
        private void AssertType(TypeID Type)
        {
            if(!HasNext(Type)) {
                TypeID NextType = (TypeID)Scanner.PeekNextByte();
                String StrExpectedType = Type.ToString().ToLower();

                if (NextType >= TypeID.MAX) { throw new InvalidOperationException("Expected to read " + StrExpectedType + ", but got unknown type id. Did you use SafePacketWriter on the other end?"); }
                else
                {
                    String StrActualType = NextType.ToString().ToLower();
                    throw new InvalidOperationException("Expected to read " + StrExpectedType + ", but actual type of next data block is " + StrActualType + ".");
                }
            }
            Scanner.NextByte(); // Move cursor forward
        }

        /// <summary>
        /// Check the type of next data block.
        /// Return the data block as the requested type if the type matches. 
        /// </summary>
        /// <exception cref="InvalidOperationException"> If type doesn't match. </exception>
        /// <returns> Next data. </returns>
        public bool   NextBool()   { AssertType(TypeID.BOOL); return Scanner.NextBool(); }
        public char   NextChar()   { AssertType(TypeID.CHAR); return Scanner.NextChar(); }
		public short  NextShort() { AssertType(TypeID.SHORT); return Scanner.NextShort(); }
		public double NextDouble() { AssertType(TypeID.DOUBLE); return Scanner.NextDouble(); }
        public float  NextFloat()  { AssertType(TypeID.FLOAT); return Scanner.NextFloat(); }
        public int    NextInt()    { AssertType(TypeID.INT); return Scanner.NextInt(); }
		public long   NextLong() { AssertType(TypeID.LONG); return Scanner.NextLong(); }
		public byte   NextByte()   { AssertType(TypeID.BYTE); return Scanner.NextByte(); }

        public byte[] NextBytes()
        {
            AssertType(TypeID.BYTES);
            int Length = Scanner.NextInt();
            return Scanner.NextBytes(Length);
        }

        public string NextString()
        {
            AssertType(TypeID.STRING);
            int Length = Scanner.NextInt();
            return Scanner.NextString(Length*2);
        }
    }
}
