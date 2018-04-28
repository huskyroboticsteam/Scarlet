using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Scarlet.Communications;
using System.Net;

namespace UnitTest.NetworkTesting {
	[TestClass]
	public class TestNetworkDeviceComm {
		static IPEndPoint baseAddress;
		static IPEndPoint roverAddress;
		static NetworkDevice rover;
		static NetworkDevice baseStation;


		static TestNetworkDeviceComm() {
			baseAddress = new IPEndPoint(IPAddress.IPv6Loopback, 0);
			roverAddress = new IPEndPoint(IPAddress.IPv6Loopback, 4343);
			rover = NetworkDevice.Start(roverAddress);
			baseStation = NetworkDevice.Start(baseAddress, roverAddress);
		}

		[TestMethod]
		public void TestAttemptReregister() {
			Assert.ThrowsException<InvalidOperationException>(() => {
				baseStation.RegisterMessageParser(new MessageTypeID(5), (time, data) => {

				});

				baseStation.RegisterMessageParser(new MessageTypeID(5), (time, data) => {

				});
			});
		}

		[TestMethod]
		public void TestReliableNetworkSendReceive() {
			byte[][] testData = new byte[10][];


			Random rand = new Random();
			for (int i = 0; i < testData.Length; i++) {
				testData[i] = GenerateData(rand);
			}

			int current = 0;

			baseStation.RegisterMessageParser(MessageTypeID.TEST_ID, (time, data) => {
				Console.WriteLine("Received data " + current);
				for(int i = 0; i < testData[current].Length; i++) {
					if(testData[current][i] != data[i]) {
						Assert.Fail("Received data not the same as sent data.");
					}
				}
				current++;
			});

			for (int i = 0; i < testData.Length; i++) {
				Console.WriteLine("sending packet " + i);
				rover.SendReliable(MessageTypeID.TEST_ID, testData[i]);
			}

			Console.WriteLine("Closing NetworkDevices");
			rover.Close();
			baseStation.Close();
		}

		private static byte[] GenerateData(Random rand) {
			byte[] data = new byte[(int)(rand.NextDouble() * 60)];
			for (int i = 0; i < data.Length / 4; i += 4) {
				BitConverter.GetBytes(rand.Next()).CopyTo(data, i);
			}
			return data;
		}
	}
}
