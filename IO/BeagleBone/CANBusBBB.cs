using System;
using System.Runtime.InteropServices;

namespace Scarlet.IO.BeagleBone
{
	public static class CANBBB
	{
		public static CANBusBBB CANBus0 { get; private set; }
		public static CANBusBBB CANBus1 { get; private set; }

		/// <summary> Prepares the given CAN buses for use. Should only be called from BeagleBone.Initialize(). </summary>
		static internal void Initialize(bool[] EnableBuses)
		{
			if (EnableBuses == null || EnableBuses.Length != 2) { throw new Exception("Invalid enable array given to CANBBB.Initialize."); }
			if (EnableBuses[0]) { CANBus0 = new CANBusBBB("can0", true); }
			if (EnableBuses[1]) { CANBus1 = new CANBusBBB("can1", true); }
		}

		/// <summary> Converts a pin number to the corresponding CAN bus ID. 255 if invalid. </summary>
		static internal byte PinToCANBus(BBBPin Pin)
		{
			switch (Pin)
			{
				case BBBPin.P9_19:
				case BBBPin.P9_20: return 0;

				case BBBPin.P9_24:
				case BBBPin.P9_26: return 1;
			}
			return 255;
		}
	}

	public class CANBusBBB : ICANBus
	{
		[StructLayout(LayoutKind.Sequential)]
		private struct SockAddrCan
		{
			[MarshalAs(UnmanagedType.U2)]
			public ushort CanFamily;

			[MarshalAs(UnmanagedType.I4)]
			public int CanIfIndex;

			[MarshalAs(UnmanagedType.U4)]
			public uint RxId;

			[MarshalAs(UnmanagedType.U4)]
			public uint TxId;
		}

		[StructLayout(LayoutKind.Explicit)]
		private unsafe struct IfRequest
		{
			[FieldOffset(0), MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
			public fixed byte Name[16];

			[FieldOffset(16), MarshalAs(UnmanagedType.I4)]
			public int IfIndex;

			[FieldOffset(16), MarshalAs(UnmanagedType.ByValArray, SizeConst = 24)]
			public fixed byte IfrU[24];
		}

		[StructLayout(LayoutKind.Explicit)]
		private unsafe struct CANFrame
		{
			[FieldOffset(0), MarshalAs(UnmanagedType.U4)]
			public uint CANID;

			[FieldOffset(4), MarshalAs(UnmanagedType.U1)]
			public byte DataLength;

			[FieldOffset(5), MarshalAs(UnmanagedType.U1)]
			public byte Padding;

			[FieldOffset(6), MarshalAs(UnmanagedType.U1)]
			public byte Reserved0;

			[FieldOffset(7), MarshalAs(UnmanagedType.U1)]
			public byte Reserved1;

			//The original source forced this to 8-byte alignment with __attribute__((aligned(8)))
			//If something breaks, maybe fix it?
			[FieldOffset(8), MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
			public fixed byte Data[8];
		}

		[StructLayout(LayoutKind.Explicit)]
		private unsafe struct ExtendedCANFrame
		{
			[FieldOffset(0), MarshalAs(UnmanagedType.U4)]
			public uint CANID;

			[FieldOffset(4), MarshalAs(UnmanagedType.U1)]
			public byte DataLength;

			[FieldOffset(5), MarshalAs(UnmanagedType.U1)]
			public byte Flags;

			[FieldOffset(6), MarshalAs(UnmanagedType.U1)]
			public byte Reserved0;

			[FieldOffset(7), MarshalAs(UnmanagedType.U1)]
			public byte Reserved1;

			//The original source forced this to 8-byte alignment with __attribute__((aligned(8)))
			//If something breaks, maybe fix it?
			[FieldOffset(8), MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
			public fixed byte Data[64];
		}

		[DllImport("libc", SetLastError = true)]
		private static extern int socket(int Namespace, int Style, int Protocol);

		[DllImport("libc", SetLastError = true)]
		private static extern int ioctl(int FileDescriptor, uint Request, ref IfRequest Req);

		[DllImport("libc", SetLastError = true)]
		private static extern int bind(int FileDescriptor, ref SockAddrCan Addr, int AddrLen);

		[DllImport("libc", SetLastError = true)]
		private static extern int read(int FileDescriptor, ref CANFrame Frame, int Size);

		[DllImport("libc", SetLastError = true)]
		private static extern int read(int FileDescriptor, ref ExtendedCANFrame Frame, int Size);

		[DllImport("libc", SetLastError = true)]
		private static extern int write(int FileDescriptor, ref CANFrame Frame, int Size);

		[DllImport("libc", SetLastError = true)]
		private static extern int write(int FileDescriptor, ref ExtendedCANFrame Frame, int Size);

		[DllImport("libc", SetLastError = true)]
		private static extern int close(int FileDescriptor);

		[DllImport("libc", SetLastError = true)]
		private static extern int setsockopt(int FileDescriptor, int Level, int Optname, ref int EnableCanFD, int Size);

		private const int AF_CAN = 29;
		private const int PF_CAN = AF_CAN;
		private const int SOCK_RAW = 3;
		private const int CAN_RAW = 1;
		private const int CAN_RAW_FD_FRAMES = 5;
		private const uint SIOCGIFINDEX = 0x8933;
		private const int SOL_CAN_BASE = 100;
		private const int SOL_CAN_RAW = SOL_CAN_BASE + CAN_RAW;

		private int Socket;
		private bool Extended;

		internal CANBusBBB(string CanName, bool Extended)
		{
			this.Extended = Extended;
			this.Socket = socket(PF_CAN, SOCK_RAW, CAN_RAW);
			if (this.Socket < 0) { throw new Exception("Error while opening socket. Error code: " + Marshal.GetLastWin32Error()); }

			unsafe
			{
				IfRequest Req = new IfRequest();
				for (int i = 0; i < CanName.Length; i++) { Req.Name[i] = Convert.ToByte(CanName[i]); }
				Req.Name[CanName.Length] = 0;
				if (ioctl(Socket, SIOCGIFINDEX, ref Req) < 0) { throw new Exception("Error during IO Control. Error code: " + Marshal.GetLastWin32Error()); }
				SockAddrCan Addr = new SockAddrCan();
				Addr.CanFamily = AF_CAN;
				Addr.CanIfIndex = Req.IfIndex;
				if (bind(Socket, ref Addr, Marshal.SizeOf(Addr)) < 0) { throw new Exception("Error while binding socket. Error code: " + Marshal.GetLastWin32Error()); };
				int ExtendedCAN = Extended ? 1 : 0;
				if (setsockopt(Socket, SOL_CAN_RAW, CAN_RAW_FD_FRAMES, ref ExtendedCAN, Marshal.SizeOf(ExtendedCAN)) != 0) { throw new Exception("Failed to enable Extended CAN. Error code: " + Marshal.GetLastWin32Error()); }
			}
		}

		/// <summary>
		/// Blocks the current thread and reads a CAN frame, returning the payload and the ID of the received CAN frame. 
		/// </summary>
		/// <returns>A tuple, with the first element being the ID of the received CAN frame and the second being the payload</returns>
		public Tuple<uint, byte[]> Read()
		{
			if (Extended)
			{
				ExtendedCANFrame Frame = new ExtendedCANFrame();
				read(Socket, ref Frame, Marshal.SizeOf(Frame));
				byte[] Payload = new byte[Frame.DataLength];
				unsafe
				{
					for (int i = 0; i < Frame.DataLength; i++) { Payload[i] = Frame.Data[i]; }
				}
				return new Tuple<uint, byte[]>(Frame.CANID, Payload);
			}
			else
			{
				CANFrame Frame = new CANFrame();
				read(Socket, ref Frame, Marshal.SizeOf(Frame));
				byte[] Payload = new byte[Frame.DataLength];
				unsafe
				{
					for (int i = 0; i < Frame.DataLength; i++) { Payload[i] = Frame.Data[i]; }
				}
				return new Tuple<uint, byte[]>(Frame.CANID, Payload);
			}
		}

		private byte DLCToLen(byte CanDLC)
		{
			byte[] Dlc2Len = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 20, 24, 32, 48, 64 };
			return Dlc2Len[CanDLC & 0x0F];
		}

		/// <summary>
		/// Write a payload with specified ID.
		/// </summary>
		/// <param name="ID">ID of CAN Frame</param>
		/// <param name="Data">Payload of CAN Frame. Must be at most 8 bytes.</param>
		public void Write(uint ID, byte[] Data)
		{
			if (!Extended && Data.Length > 8) { throw new Exception("CAN Data Length must be no more than 8 bytes for non-Extended frames"); }
			else if (Data.Length > 64) { throw new Exception("CAN Data Length must be no more than 64 bytes for Extended frames."); }
			int BytesWritten;
			unsafe
			{
				if (Extended)
				{
					ExtendedCANFrame Frame = new ExtendedCANFrame();
					Frame.CANID = ID;
					Frame.DataLength = DLCToLen(DLCToLen((byte)Data.Length));
					for (int i = 0; i < Data.Length; i++) { Frame.Data[i] = Data[i]; }
					BytesWritten = write(Socket, ref Frame, Marshal.SizeOf(Frame));
				}
				else
				{
					CANFrame Frame = new CANFrame();
					Frame.CANID = ID;
					Frame.DataLength = DLCToLen(DLCToLen((byte)Data.Length));
					for (int i = 0; i < Data.Length; i++) { Frame.Data[i] = Data[i]; }
					BytesWritten = write(Socket, ref Frame, Marshal.SizeOf(Frame));
				}
			}
			if (BytesWritten < 0) { throw new Exception("Failed to write CAN frame. Error code: " + Marshal.GetLastWin32Error()); }
		}

		/// <summary> Cleans up the bus object, freeing resources. </summary> 
		public void Dispose() => close(Socket);
	}
}
