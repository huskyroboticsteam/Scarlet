using System;
using Scarlet.Communications;
using System.Net;
using Scarlet.Utilities;

namespace Scarlet.TestSuite {
	public static class NetworkDeviceTest {
		public static void Start(string[] args) {
			NetworkDevice server = NetworkDevice.Start(
				new IPEndPoint(IPAddress.Loopback, 4343)
			);

			NetworkDevice client = NetworkDevice.Start(
				new IPEndPoint(IPAddress.Loopback, 20020),
				new IPEndPoint(IPAddress.Loopback, 4343)
			);

			server.RegisterMessageParser(MessageTypeID.TEST_ID, (time, data) => {
				Console.WriteLine($"Received {data.Length} bytes at {time} of type 43: {UtilData.ToString(data)}");
			});

			//will spam random decimal values and log to the console
			for (int i = 0; i < 10; i++) {
				client.SendReliable(MessageTypeID.TEST_ID,
					UtilData.ToBytes(new Random().NextDouble().ToString().Substring(3, (int)(new Random().NextDouble() * 10))));
				System.Threading.Thread.Sleep(100);
			}
		}
	}
}
