using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Scarlet.Communications {
	public interface IPacketWriter {
		Packet Packet { get; }
		IPacketWriter Put(bool data);
		IPacketWriter Put(char data);
		IPacketWriter Put(short data);
		IPacketWriter Put(double data);
		IPacketWriter Put(float data);
		IPacketWriter Put(int data);
		IPacketWriter Put(long data);
		IPacketWriter Put(string data);
		IPacketWriter Put(byte[] data);
		IPacketWriter Put(byte data);
	}
}
