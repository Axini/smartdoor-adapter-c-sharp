
using SmartdoorAdapter.Adapter;
using SmartdoorAdapter.Tests.Util;

namespace SmartdoorAdapter.Tests.IntegrationTests
{
    [TestClass]
    public class ConnectTest
    {
        public static readonly string AdapterName = "SmartDoorIntegrationTest";

        /// <summary>
        /// Test the connection against the actual axini amp.
        /// The user should set env variables AXINI_AUTH_TOKEN and AXINI_AMP_HOST
        /// prior to running this test
        /// </summary>
        [TestMethod]
        [Ignore] // ignore as this requires the user to run the sut, disable the igore when specifically testing this
        public void BrokerConnectDisconnectTest()
        {
            // user should set env variables AXINI_AUTH_TOKEN and AXINI_AMP_HOST
            var authToken = Environment.GetEnvironmentVariable(EnvNames.ApiKey);

            Assert.IsTrue(!string.IsNullOrEmpty(authToken));

            var hostName = Environment.GetEnvironmentVariable(EnvNames.HostName);

            Assert.IsTrue(!string.IsNullOrEmpty(hostName));

            var broker = new BrokerConnection(new Uri(hostName), authToken);
            var adapter = new AdapterCore(AdapterName, broker, new SmartdoorHandler());

            _ = Task.Run(async () =>
            {
                try
                {
                    await adapter.Start();
                }
                catch (Exception ex)
                {
                    Assert.Fail(ex.ToString());
                }
            });

            TaskUtil.WaitUntil(() => broker.State == ManagedSocketState.Listening, timeStep: 1000);

            Assert.IsTrue(broker.State == ManagedSocketState.Listening);
            Assert.IsTrue(adapter.State == AdapterCoreState.Announced);

            adapter.Stop();

            Assert.IsTrue(broker.State == ManagedSocketState.Closed);
            Assert.IsTrue(adapter.State == AdapterCoreState.Disconnected);
        }

        /// <summary>
        /// This should connect to the standalone sut, IFF the SUT is running
        /// If it works, this test will (obviously) succeed but one will see in the standalone's
        /// sut output something like:
        ///
        /// 2024-11-01 22:13:40.447 [INFO] Client connected
        /// 2024-11-01 22:13:40.448 [INFO]SmartDoor created
        /// 2024-11-01 22:13:40.466 [INFO] Received message: RESET
        /// 2024-11-01 22:13:40.466 [INFO]
        /// Resetting SUT
        /// 2024-11-01 22:13:40.466 [INFO] Sending message:  RESET_PERFORMED
        /// 2024-11-01 22:13:56.748 [INFO]Client disconnected
        ///
        /// </summary>
        /// <returns></returns>
        [Ignore] // ignore as this requires the user to run the sut, disable the igore when specifically testing this
        [TestMethod]
        public void HandlerStartStopTest()
        {
            var handler = new SmartdoorHandler();

            Assert.IsFalse(handler.IsListening);
            Assert.IsFalse(handler.IsResetReceived);

            handler.Start().Wait();

            TaskUtil.WaitUntil(() => handler.IsListening && handler.IsResetReceived, timeStep: 500);

            Assert.IsTrue(handler.IsListening);
            Assert.IsTrue(handler.IsResetReceived);

            handler.Stop();

            TaskUtil.WaitUntil(() => !handler.IsListening, timeStep: 1000);
        }
    }
}