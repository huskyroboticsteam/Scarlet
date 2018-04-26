using System;
using System.Linq;
using System.Text;

namespace Scarlet.Utilities
{
    public static class UtilData
    {
        internal static byte[] EnsureBigEndian(byte[] Input, int index = 0, int length = -1)
        {
            if (BitConverter.IsLittleEndian) { Array.Reverse(Input, index, length == -1 ? Input.Length : length); }
            return Input;
        }

        public static byte[] ToBytes(bool Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }
        public static byte[] ToBytes(char Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }
        public static byte[] ToBytes(double Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }
        public static byte[] ToBytes(float Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }
        public static byte[] ToBytes(int Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }
        public static byte[] ToBytes(long Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }
        public static byte[] ToBytes(short Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }
        public static byte[] ToBytes(uint Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }
        public static byte[] ToBytes(ulong Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }
        public static byte[] ToBytes(ushort Input) { return EnsureBigEndian(BitConverter.GetBytes(Input)); }

        public static bool ToBool(byte[] Input, int index = 0) {
            //if (Input.Length != 1) { throw new FormatException("Given byte[] does not convert to bool."); }
            return BitConverter.ToBoolean(EnsureBigEndian(Input, index, 1), index);
        }

        public static char ToChar(byte[] Input, int index = 0) {
            //if (Input.Length != 2) { throw new FormatException("Given byte[] does not convert to char."); }
            return BitConverter.ToChar(EnsureBigEndian(Input, index, 2), index);
        }

        public static double ToDouble(byte[] Input, int index = 0) {
            //if (Input.Length != 8) { throw new FormatException("Given byte[] does not convert to double."); }
            return BitConverter.ToDouble(EnsureBigEndian(Input, index, 8), index);
        }

        public static float ToFloat(byte[] Input, int index = 0) {
            //if (Input.Length != 4) { throw new FormatException("Given byte[] does not convert to float."); }
            return BitConverter.ToSingle(EnsureBigEndian(Input, index, 4), index);
        }

        public static int ToInt(byte[] Input, int index = 0) {
            //if (Input.Length != 4) { throw new FormatException("Given byte[] does not convert to int."); }
            return BitConverter.ToInt32(EnsureBigEndian(Input, index, 4), index);
        }

        public static long ToLong(byte[] Input, int index = 0) {
            //if (Input.Length != 8) { throw new FormatException("Given byte[] does not convert to long."); }
            return BitConverter.ToInt64(EnsureBigEndian(Input, index, 8), index);
        }

        public static short ToShort(byte[] Input, int index = 0) {
            //if (Input.Length != 2) { throw new FormatException("Given byte[] does not convert to short."); }
            return BitConverter.ToInt16(EnsureBigEndian(Input, index, 2), index);
        }

        public static uint ToUInt(byte[] Input, int index = 0) {
            //if (Input.Length != 4) { throw new FormatException("Given byte[] does not convert to uint."); }
            return BitConverter.ToUInt32(EnsureBigEndian(Input, index, 4), index);
        }

        public static ulong ToULong(byte[] Input, int index = 0) {
            //if (Input.Length != 8) { throw new FormatException("Given byte[] does not convert to ulong."); }
            return BitConverter.ToUInt64(EnsureBigEndian(Input, index, 8), index);
        }

        public static ushort ToUShort(byte[] Input, int index = 0) {
            //if (Input.Length != 2) { throw new FormatException("Given byte[] does not convert to ushort."); }
            return BitConverter.ToUInt16(EnsureBigEndian(Input, index, 2), index);
        }

		/// <summary>
		/// Converts a UTF-8 encoded stored as a byte array to a string.
		/// </summary>
		/// <param name="data"></param>
		/// <param name="index"></param>
		/// <param name="length"></param>
		/// <returns></returns>
		public static String ToString(byte[] data, int index = 0, int length = -1) {
			return Encoding.UTF8.GetString(data, index, length == -1 ? data.Length : length);
		}

		/// <summary>
		/// Converts a string object to a UTF-8 encoded byte array
		/// </summary>
		/// <param name="data"></param>
		/// <returns></returns>
		public static byte[] ToBytes(String data) {
			return Encoding.UTF8.GetBytes(data);
		}

		/// <summary> Determines if the given type is numeric. </summary>
		/// <param name="Type"> Type to determine whether or not it is a numeric </param>
		/// <returns> Returns <c>true</c> if param is a numeric; otherwise returns <c>false</c>. </returns>
		public static bool IsNumericType(Type Type)
        {
            switch (Type.GetTypeCode(Type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }

		/// <summary> 
		/// Takes a 16bit number, and swaps the locations of the first and last 8b. 
		/// E.g. 0x54EC would become 0xEC54. Intended for use with 16b I2C devices 
		/// that expect the byte order reversed. </summary>
		public static ushort SwapBytes(ushort Input)
        {
            return (ushort)(((Input & 0b1111_1111) << 8) | ((Input >> 8) & 0b1111_1111));
        }
	}
}
