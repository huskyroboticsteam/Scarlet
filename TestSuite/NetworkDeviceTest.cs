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
				Console.WriteLine($"Received {data.Length} bytes at {time}: {UtilData.ToString(data).Replace('\n', '\0')}");
			});

			//will spam random decimal values and log to the console
			Random rand = new Random();
			for (int i = 0; i < 10; i++) {
				byte[] data = GenerateData(rand);
				Console.WriteLine($"Sending {UtilData.ToString(data)}");
				rover.SendReliable(MessageTypeID.TEST_ID, data);
			}

			Console.WriteLine("Closing NetworkDevices");
			rover.Close();
			baseStation.Close();
			baseStation.Close();
			Console.WriteLine("NetworkDevices closed");
		}

		private static byte[] GenerateData(Random rand) {
			byte[] data = new byte[(int)(rand.NextDouble() * 60)];
			for(int i = 0; i < data.Length / 4; i += 4) {
				BitConverter.GetBytes(rand.Next()).CopyTo(data, i);
			}
			return data;
		}
	}
}
