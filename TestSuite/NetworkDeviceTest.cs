using System;
using Scarlet.Communications;
using System.Net;
using Scarlet.Utilities;

namespace Scarlet.TestSuite {
	public static class NetworkDeviceTest {
		public static void Start(string[] args) {
			IPEndPoint baseAddress = new IPEndPoint(IPAddress.IPv6Loopback, 0);
			IPEndPoint roverAddress = new IPEndPoint(IPAddress.IPv6Loopback, 4343);
			NetworkDevice rover = NetworkDevice.Start(roverAddress);
			NetworkDevice baseStation = NetworkDevice.Start(baseAddress, roverAddress);

			baseStation.RegisterMessageParser(MessageTypeID.TEST_ID, (time, data) => {
				Console.WriteLine($"Received {data.Length} bytes at {time} of type 43: {UtilData.ToString(data)}");
			});

			//will spam random decimal values and log to the console
			for (int i = 0; i < 10; i++) {
				byte[] data = GenerateData();
				Console.WriteLine($"Sending {UtilData.ToString(data)}");
				rover.SendReliable(MessageTypeID.TEST_ID, data);
				System.Threading.Thread.Sleep(200);
			}

			Console.WriteLine("Closing NetworkDevices");
			rover.Close();
			baseStation.Close();
		}

		private static byte[] GenerateData() {
			byte[] data = new byte[(int)(new Random().NextDouble() * 60)];
			Random rand = new Random();
			for(int i = 0; i < data.Length / 4; i += 4) {
				BitConverter.GetBytes(rand.Next()).CopyTo(data, i);
			}
			return data;
		}
	}
}
