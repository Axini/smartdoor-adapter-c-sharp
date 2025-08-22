
using log4net.Config;
using Microsoft.Net.Http.Headers;
using SmartdoorAdapter.Adapter;
using SmartdoorAdapter.Tests.Util;
using System.Net.WebSockets;

namespace SmartdoorAdapter.Tests.UnitTests
{
    [TestClass]
    public class ManagedWebSocketTests
    {
        [TestMethod]
        public async Task SuccesfullConnectTest()
        {
            var testAuthToken = "dummy_token";
            await KestralTestTemplate.RunTestWithCustomClient(
                (context, serverSocket) =>
                {
                    if (context.Request.Headers.TryGetValue(HeaderNames.Authorization, out var tokenSentByClient))
                    {
                        Assert.AreEqual(tokenSentByClient.First(), testAuthToken);
                    }
                    else
                    {
                        Assert.Fail("missing auth token");
                    }

                    return Task.CompletedTask;
                },
                async (uri) =>
                {
                    var broker = new ManagedClientWebSocket(uri, authToken: testAuthToken, defaultTimeout: 5000);

                    try
                    {
                        await broker.Connect();
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.ToString());
                    }
                }
            );
        }

        [TestMethod]
        public async Task ConnectToWrongPortTest()
        {
            var testAuthToken = "dummy_token";
            await KestralTestTemplate.RunTestWithCustomClient(
                (context, serverSocket) => Task.CompletedTask,
                async (uri) =>
                {
                    // connect to the wrong port
                    var broker = new ManagedClientWebSocket(new Uri($"ws://{uri.Host}:{uri.Port + 1}"),
                        authToken: testAuthToken,
                        defaultTimeout: 2000);

                    await broker.Connect();

                    // exceeded max connections
                    Assert.IsTrue(broker.State == ManagedSocketState.Error);
                    Assert.IsTrue(broker.SocketState == WebSocketState.Closed);

                    broker.Close();

                    Assert.IsTrue(broker.State == ManagedSocketState.Closed);
                    Assert.IsTrue(broker.SocketState == WebSocketState.Closed);
                }
            );
        }

        [TestMethod]
        public async Task SendReceiveClientToServerTest()
        {
            var testMessage = "foo";
            await KestralTestTemplate.RunTestWithCustomClient(
                async (context, serverSocket) =>
                {
                    var result = await serverSocket.ReceiveText();

                    Assert.IsNotNull(result);
                    Assert.AreEqual(testMessage, result);
                },
                async (uri) =>
                {
                    var broker = new ManagedClientWebSocket(uri);

                    await broker.Connect();
                    await broker.SendText(testMessage);
                }
            );
        }

        [TestMethod]
        public async Task SendReceiveServerToClientTest()
        {
            var testMessage = "foo";
            await KestralTestTemplate.RunTestWithCustomClient(
                async (context, serverSocket) =>
                {
                    await serverSocket.SendText(testMessage);
                },
                async (uri) =>
                {
                    var broker = new ManagedClientWebSocket(uri);
                    var receivedMessage = "";

                    broker.OnTextReceived += (sender, text) => receivedMessage = text;

                    await broker.Connect();
                    var messageType = (await broker.Receive(new byte[128]))!.MessageType;

                    Assert.IsTrue(messageType == WebSocketMessageType.Text);
                    Assert.AreEqual(testMessage, receivedMessage);
                }
            );
        }

        /// <summary>
        /// Test the close handshake initiated by the client
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ClientCloseConnectionTest()
        {
            await KestralTestTemplate.RunTestWithCustomClient(
                async (context, serverSocket) =>
                {
                    var buffer = new byte[1024];
                    var result = await serverSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    Assert.IsTrue(result.MessageType == WebSocketMessageType.Close);

                    await serverSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, CancellationToken.None);

                    Assert.IsTrue(serverSocket.State == WebSocketState.Closed);

                    // wait for the client to receive the closed
                    await Task.Delay(2000);
                },
                async (uri) =>
                {
                    var broker = new ManagedClientWebSocket(uri);

                    await broker.Connect();

                    broker.Close(WebSocketCloseStatus.NormalClosure, "message");

                    Assert.IsTrue(broker.State == ManagedSocketState.Closed);

                }
            );
        }

        /// <summary>
        /// Test the close initiated by the server
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ServerCloseConnectionTest()
        {
            var closeMessage = "closeMessage";

            await KestralTestTemplate.RunTestWithCustomClient(
                async (context, serverSocket) =>
                {
                    await serverSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, closeMessage, CancellationToken.None);

                    Assert.IsTrue(serverSocket.State == WebSocketState.Closed);
                },
                async (uri) =>
                {
                    var broker = new ManagedClientWebSocket(uri);

                    await broker.Connect();
                    var result = await broker.Receive(new byte[1024]);

                    Assert.IsTrue(result != null);
                    Assert.IsTrue(result.CloseStatusDescription == closeMessage);
                    Assert.IsTrue(result.MessageType == WebSocketMessageType.Close);

                    // no need to wait for the server or confirm closure?
                    Assert.IsTrue(broker.State == ManagedSocketState.Closed);
                }
            );
        }

        /// <summary>
        /// Test the behaviour of the client when the server closes the 
        /// connection while the client is listening.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ServerCloseWhileClientIsListeningTest()
        {
            var closeMessage = "closeMessage";
            var isClientListening = false;

            await KestralTestTemplate.RunTestWithCustomClient(

                serverTests: async (context, serverSocket) =>
                {
                    // give the client some time to start listening
                    TaskUtil.WaitUntil(() => isClientListening);

                    await serverSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, closeMessage, CancellationToken.None);

                    Assert.IsTrue(serverSocket.State == WebSocketState.Closed);
                },

                clientTests: async (uri) =>
                {
                    var isClosedCalled = false;
                    var broker = new ManagedClientWebSocket(uri);

                    broker.OnClosed += (sender, message) => isClosedCalled = true;

                    await broker.Connect();

                    try
                    {
                        _ = broker.Listen();
                        isClientListening = true;
                        TaskUtil.WaitUntil(() => isClosedCalled);

                        Assert.IsTrue(isClosedCalled);
                        Assert.IsTrue(broker.State == ManagedSocketState.Closed);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.ToString());
                    }
                }
            );
        }

        /// <summary>
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public void ClientAbortConnectionTest()
        {
            XmlConfigurator.Configure(new FileInfo("log4net.config"));

            var broker = new ManagedClientWebSocket(new Uri("ws://localhost:4321/bs"))
            {
                ConnectionTimeout = 1000,
                MaxConnectionAttempts = -1
            };

            for (var i = 0; i < 2; i++)
            {
                _ = broker.Connect(backoffTimeMs: 250);

                TaskUtil.WaitUntil(() => broker.State == ManagedSocketState.Connecting, timeStep: 250);

                Assert.IsTrue(broker.State == ManagedSocketState.Connecting);

                // wait for until the connection process to start...
                Task.Delay(1000).Wait();

                broker.Close();

                Assert.IsTrue(broker.State == ManagedSocketState.Closed);
            }
        }


        /// <summary>
        /// Test the behaviour of the client when the server drops the connection.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ServerDropsWhileClientIsListeningTest()
        {
            var isClientListening = false;
            var hasClientStoppedListening = false;

            await KestralTestTemplate.RunTestWithCustomClient(

                serverTests: (context, serverSocket) =>
                {
                    // give the client some time to start listening
                    TaskUtil.WaitUntil(() => isClientListening, timeStep: 100);

                    // exit without closing
                    return Task.CompletedTask;
                },

                clientTests: async (uri) =>
                {
                    var broker = new ManagedClientWebSocket(uri);
                    await broker.Connect();

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            isClientListening = true;
                            await broker.Listen();
                            hasClientStoppedListening = true;
                        }
                        catch (Exception ex)
                        {
                            Assert.Fail(ex.ToString());
                        }
                    });

                    // give the client some time to start listening
                    Task.Delay(100).Wait();

                    TaskUtil.WaitUntil(() => hasClientStoppedListening);

                    // check if the waituntil didn't just timeout but the predicate is true
                    Assert.IsTrue(hasClientStoppedListening);

                    // broker has closed 'gracefully' 
                    Assert.IsTrue(broker.State == ManagedSocketState.Closed);

                    // was the listen operation aborted ?
                    Assert.IsTrue(broker.SocketState == WebSocketState.Aborted);
                }
            );
        }


        /// <summary>
        /// Test the behaviour of the client when the server drops the connection.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task RestartWhileClientIsReceivingTest()
        {
            var isClientComplete = false;

            await KestralTestTemplate.RunTestWithCustomClient(

                serverTests: (context, serverSocket) =>
                {
                    // give the client some time to start listening
                    TaskUtil.WaitUntil(() => isClientComplete, timeStep: 2500);

                    // exit without closing
                    return Task.CompletedTask;
                },

                clientTests: async (uri) =>
                {
                    var isOperationCanceled = false;
                    var client = new ManagedClientWebSocket(uri);

                    await client.Connect();

                    // start receiving
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // receive should close
                            var result = await client.Receive(new byte[256]);

                            // should have been aborted due to a close
                            Assert.IsTrue(result == ManagedClientWebSocket.OperationCanceledResult);
                            isOperationCanceled = true;
                        }
                        catch (Exception ex)
                        {
                            Assert.Fail(ex.ToString());
                        }
                    });

                    client.Close();

                    // give the client some stop cancelling the receive
                    Task.Delay(2500).Wait();
                    isClientComplete = true;

                    Assert.IsTrue(isOperationCanceled);
                    Assert.IsTrue(client.State == ManagedSocketState.Closed);
                }
            );
        }
    }
}