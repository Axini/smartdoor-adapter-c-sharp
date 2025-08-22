using Google.Protobuf;
using Microsoft.Net.Http.Headers;
using Moq;
using SmartdoorAdapter.Adapter;
using SmartdoorAdapter.Proto;
using SmartdoorAdapter.Tests.Util;
using System.Diagnostics;
using System.Net.WebSockets;

namespace SmartdoorAdapter.Tests.IntegrationTests
{
    /// <summary>
    /// Integraton tests between a similated SUT / AMP and the adapter core
    /// </summary>
    [TestClass]
    public class AdapterCoreTests
    {
        /// <summary>
        /// Check if the adapter/broker announces itself with an auth token
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task SuccesfullConnectTest()
        {
            var testAuthToken = "dummy_token";
            var isClientDone = false;
            var coreName = "adapterCoreTest";

            await KestralTestTemplate.RunTestWithCustomClient(
                async (context, serverSocket) =>
                {
                    if (context.Request.Headers.TryGetValue(HeaderNames.Authorization, out var tokenSentByClient))
                    {
                        Assert.AreEqual(tokenSentByClient.First(), testAuthToken);
                    }
                    else
                    {
                        Assert.Fail("missing auth token");
                    }

                    // expect the adapter announcement
                    try
                    {
                        var arraySegment = await serverSocket.ReceiveBinary();
                        var message = Message.Parser.ParseFrom(arraySegment.Array, arraySegment.Offset, arraySegment.Count);

                        Assert.IsNotNull(message);
                        Assert.AreEqual(message.Announcement.Name, coreName);

                        // let the client finish up its test
                        TaskUtil.WaitUntil(() => isClientDone, timeStep: 250);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.Message);
                    }
                },
            (uri) =>
                {

                    var handlerMock = CreateHandlerMock();
                    // not mocking the broker we actually want to model the behaviour of the AMP with these tests
                    var broker = new BrokerConnection(uri, apiKey: testAuthToken, defaultTimeout: 3000);
                    var adapter = new AdapterCore(coreName, broker, handlerMock.Object);

                    try
                    {
                        Assert.IsTrue(adapter.State == AdapterCoreState.Disconnected);

                        _ = adapter.Start();

                        TaskUtil.WaitUntil(() => broker.State == ManagedSocketState.Listening
                            && adapter.State == AdapterCoreState.Announced, timeStep: 250);

                        Assert.IsTrue(adapter.State == AdapterCoreState.Announced);
                        Assert.IsTrue(broker.State == ManagedSocketState.Listening);
                    }
                    catch (AssertFailedException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.ToString());
                    }
                    finally
                    {
                        isClientDone = true;
                    }
                }
            );
        }


        /// <summary>
        /// Check if the handler receives a configuration on startup 
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task StartHandlerTest()
        {
            var testAuthToken = "dummy_token";
            var isClientDone = false;
            var coreName = "adapterCoreTest";
            var ampConfig = AxiniProtobuf.CreateConfiguration([
                                        AxiniProtobuf.CreateItem("key", "WebSocket URL of AMP", "ws://localhost:3001")]);

            await KestralTestTemplate.RunTestWithCustomClient(
                async (context, serverSocket) =>
                {
                    try
                    {
                        // expect the adapter announcement
                        _ = await serverSocket.ReceiveBinary();

                        // give the client a chance to handle the assertions
                        await Task.Delay(250);

                        // reply with a configuration 
                        await serverSocket.SendBinary(AxiniProtobuf.CreateMsgConfiguration(ampConfig).ToByteArray());

                        // wait for ready message
                        var arraySegment = await serverSocket.ReceiveBinary();
                        var readyMessage = Message.Parser.ParseFrom(arraySegment.Array, arraySegment.Offset, arraySegment.Count);

                        Assert.IsNotNull(readyMessage);

                        // let the client finish up its test
                        TaskUtil.WaitUntil(() => isClientDone, timeStep: 2500);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.Message);
                    }
                },
            (uri) =>
            {
                var handlerMock = CreateHandlerMock();

                // not mocking the broker we actually want to model the behaviour of the AMP with these tests
                var broker = new BrokerConnection(uri, apiKey: testAuthToken, defaultTimeout: 3000);
                var adapter = new AdapterCore(coreName, broker, handlerMock.Object);

                try
                {
                    Assert.IsTrue(adapter.State == AdapterCoreState.Disconnected);

                    _ = adapter.Start();

                    // start listening
                    TaskUtil.WaitUntil(() => broker.State == ManagedSocketState.Listening
                        && adapter.State == AdapterCoreState.Announced, timeStep: 250);

                    // make sure the adapter reaches the start method
                    TaskUtil.WaitUntil(() => adapter.State == AdapterCoreState.Ready, timeStep: 250);

                    handlerMock.Verify(handler => handler.Start(), Times.Once());

                    handlerMock.VerifySet(handler => handler.Configuration =
                        It.Is<Configuration>(conf => conf != null
                            && conf.Items.Count == 1
                            && conf.Items[0].Key == ampConfig.Items[0].Key
                            && conf.Items[0].Description == ampConfig.Items[0].Description
                            && conf.Items[0].String == ampConfig.Items[0].String)
                    , Times.Once());
                }
                catch (AssertFailedException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Assert.Fail(ex.ToString());
                }
                finally
                {
                    isClientDone = true;
                }
            }
            );
        }

        /// <summary>
        /// Verify if the adapter core start loop works by iterating multiple times
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task CoreStartLoopTest()
        {
            var testAuthToken = "dummy_token";
            var coreName = "adapterCoreTest";
            var ampConfig = AxiniProtobuf.CreateConfiguration([
                                        AxiniProtobuf.CreateItem("key", "WebSocket URL of AMP", "ws://localhost:3001")]);
            var serverIteration = 0;
            var maxIterations = 3;

            await KestralTestTemplate.RunTestWithCustomClient(
                async (context, serverSocket) =>
                {
                    try
                    {
                        // expect the adapter announcement
                        _ = await serverSocket.ReceiveBinary();

                        // give the client a chance to handle the assertions
                        await Task.Delay(250);

                        // reply with a configuration 
                        await serverSocket.SendBinary(AxiniProtobuf.CreateMsgConfiguration(ampConfig).ToByteArray());

                        // wait for ready message
                        var arraySegment = await serverSocket.ReceiveBinary();

                        var readyMessage = Message.Parser.ParseFrom(arraySegment.Array, arraySegment.Offset, arraySegment.Count);

                        Assert.IsNotNull(readyMessage);

                        // let the client finish up its test
                        await Task.Delay(250);

                        serverIteration++;

                        // server will close after this and the client will try to connect again
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.Message);
                    }
                },
            (uri) =>
            {
                var handlerMock = CreateHandlerMock();
                handlerMock.Setup(m => m.Start()).ReturnsAsync(true);

                // not mocking the broker we actually want to model the behaviour of the AMP with these tests
                var broker = new BrokerConnection(uri, apiKey: testAuthToken, defaultTimeout: 3000);
                var adapter = new AdapterCore(coreName, broker, handlerMock.Object);

                try
                {
                    Assert.IsTrue(adapter.State == AdapterCoreState.Disconnected);

                    _ = adapter.Start(CancellationToken.None);

                    TaskUtil.WaitUntil(() => serverIteration >= maxIterations, 250);

                    handlerMock.Verify(handler => handler.Start(), Times.AtLeast(maxIterations));
                }
                catch (Exception ex)
                {
                    Assert.Fail(ex.ToString());
                }
            }
            );
        }

        /// <summary>
        /// Test the AMP closing while the adapter is connecting to the SUT.
        /// Test adapter goes back into announce / ready state after failure by
        /// checking if the mocked server goes through multiple iterations.
        /// </summary>
        /// <returns></returns>
        // ignored as it takes too long, uncomment [Ignore] to test
        [Ignore]
        [TestMethod]
        public async Task AMPClosedWhileConnectingToSUTTest()
        {
            var testAuthToken = "dummy_token";
            var coreName = "adapterCoreTest";
            var ampConfig = AxiniProtobuf.CreateConfiguration([
                                        AxiniProtobuf.CreateItem("url", "WebSocket URL of AMP", "ws://localhost:3001")]);
            var serverIteration = 0;

            await KestralTestTemplate.RunTestWithCustomClient(
                async (context, serverSocket) =>
                {
                    try
                    {
                        // expect the adapter announcement
                        _ = await serverSocket.ReceiveBinary();

                        // give the client a chance to handle the assertions
                        await Task.Delay(250);

                        // reply with a configuration 
                        await serverSocket.SendBinary(AxiniProtobuf.CreateMsgConfiguration(ampConfig).ToByteArray());

                        // let the client finish the configuration and try to connect to the sut (which
                        // is not responding)
                        await Task.Delay(5000);

                        // close the connection
                        await serverSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing amp", CancellationToken.None);

                        // give the client some time to finish up the tests
                        await Task.Delay(250);

                        serverIteration++;

                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.Message);
                    }
                },
            async (uri) =>
            {
                var handler = new SmartdoorHandler(maxConnectionAttempts: 10);

                // not mocking the broker we actually want to model the behaviour of the AMP with these tests
                var broker = new BrokerConnection(uri,
                    apiKey: testAuthToken,
                    maxConnectionAttempts: 10,
                    defaultTimeout: 5000);

                var adapter = new AdapterCore(coreName, broker, handler);

                try
                {
                    Assert.IsTrue(adapter.State == AdapterCoreState.Disconnected);

                    var cancellationSource = new CancellationTokenSource();

                    _ = adapter.Start(cancellationSource.Token);

                    await Task.Delay(20000);

                    cancellationSource.Cancel();

                    await Task.Delay(250);

                    Assert.IsTrue(serverIteration > 1);
                }
                catch (Exception ex)
                {
                    Assert.Fail(ex.ToString());
                }
            }
            );
        }

        /// <summary>
        /// Send multiple stimuli from the AMP to the Adapter. Verify all have 
        /// corresponding responses.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ParallelReceiveResponseTest()
        {
            var testAuthToken = "dummy_token";
            var coreName = "adapterCoreTest";
            var ampConfig = AxiniProtobuf.CreateConfiguration([
                                        AxiniProtobuf.CreateItem("url", "WebSocket URL of AMP", "ws://localhost:3001")]);

            var maxStimuli = 10;
            var hasClientReceivedAllStimuli = false;

            await KestralTestTemplate.RunTestWithCustomClient(
                async (context, serverSocket) =>
                {
                    try
                    {
                        // expect the adapter announcement
                        _ = await serverSocket.ReceiveBinary();

                        // give the client a chance to handle the assertions
                        await Task.Delay(250);

                        // reply with a configuration 
                        await serverSocket.SendBinary(AxiniProtobuf.CreateMsgConfiguration(ampConfig).ToByteArray());

                        // wait for ready message
                        var arraySegment = await serverSocket.ReceiveBinary();
                        var readyMessage = Message.Parser.ParseFrom(arraySegment.Array, arraySegment.Offset, arraySegment.Count);

                        Assert.IsNotNull(readyMessage);
                        Assert.IsTrue(readyMessage.Ready != null);

                        var exceptionsReceived = new List<Exception>();
                        var responsesReceived = new List<Message>();

                        // start listening on a background thread. 
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                while (true)
                                {
                                    var responseData = await serverSocket.ReceiveBinary(timeOut: 0);
                                    var response = Message.Parser.ParseFrom(responseData.Array, responseData.Offset, responseData.Count);

                                    Debug.WriteLine($"{responsesReceived.Count}: Received response {response?.Label?.Type}: {response?.Label?.Label_}.");

                                    responsesReceived.Add(response!);
                                }
                            }
                           
                            catch (Exception e) when (e is not TaskCanceledException && e is not WebSocketException)
                            {
                                exceptionsReceived.Add(e);  
                            }
                        });

                        // start sending random labels (except reset and responses)
                        var labels = SmartdoorHandler.CreateSupportedLabels()
                                        .Where(l => l.Label_ != SmartdoorHandler.ResetLabel && l.Type != Label.Types.LabelType.Response)
                                        .ToList();
                        var random = new Random(42);
                        var labelsSent = new List<Label>();

                        for (var i = 0; i < maxStimuli; i++)
                        {
                            var label = labels[random.Next(labels.Count)].Clone();
                            labelsSent.Add(label);
                            await serverSocket.SendBinary(AxiniProtobuf.CreateMsgLabel(label).ToByteArray(), 50000);
                            await Task.Delay(random.Next(100, 2000));
                        }

                        // give the client some time to finish up the response handling
                        TaskUtil.WaitUntil(() => hasClientReceivedAllStimuli, 2000);

                        Assert.IsTrue(hasClientReceivedAllStimuli);
                        
                        // 1 for stimulus and 1 for response, so 2 * maxStimuli
                        Assert.IsTrue(responsesReceived.Count == 2 * maxStimuli);
                        Assert.IsTrue(responsesReceived.Count( r => r.Label.Type == Label.Types.LabelType.Stimulus) == maxStimuli);
                        Assert.IsTrue(responsesReceived.Count(r => r.Label.Type == Label.Types.LabelType.Response) == maxStimuli);

                        // close the connection
                        await serverSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing amp", CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.Message);
                    }
                },
            async (uri) =>
            {
                var receivedStimuli = new List<string>();
                var receiveLock = new object();
                var rng = new Random();
                var handlerMock = CreateHandlerMock();
                handlerMock.Setup(m => m.Start()).ReturnsAsync(true);
                handlerMock.Setup(m => m.Stimulate(It.IsAny<string>()))
                    .Callback<string>(async (value) =>
                    {
                        // store the stimili and fake a delayed response
                        var delay = 0;
                        lock (receiveLock) 
                        {
                            receivedStimuli.Add(value);
                            delay = rng.Next(100, 1000);
                        }

                        // wait a random amount of time
                        await Task.Delay(delay);

                        handlerMock.Raise(m => m.OnResponse += null, handlerMock.Object, $"{value}-response");
                    });

                var broker = new BrokerConnection(uri,
                    apiKey: testAuthToken,
                    maxConnectionAttempts: 10,
                    defaultTimeout: 5000);

                var adapter = new AdapterCore(coreName, broker, handlerMock.Object);

                try
                {
                    Assert.IsTrue(adapter.State == AdapterCoreState.Disconnected);

                    var cancellationSource = new CancellationTokenSource();

                    // start the process
                    _ = adapter.Start(cancellationSource.Token);

                    // wait until all stimuli have been received (or a timeout occurs)
                    TaskUtil.WaitUntil(() => receivedStimuli.Count == maxStimuli, timeStep: 2000);

                    Assert.IsTrue(receivedStimuli.Count == maxStimuli);

                    hasClientReceivedAllStimuli = true;
                    cancellationSource.Cancel();

                    await Task.Delay(250);
                }
                catch (Exception ex)
                {
                    Assert.Fail(ex.ToString());
                }
            }
            );
        }

        /// <summary>
        /// Utility method to create a mock of the handler
        /// </summary>
        /// <returns></returns>
        private static Mock<IHandler> CreateHandlerMock()
        {
            var handlerMock = new Mock<IHandler>();
            var supportedLabels = SmartdoorHandler.CreateSupportedLabels();
            var handlerConfig = AxiniProtobuf.CreateConfiguration([
                                    AxiniProtobuf.CreateItem("url", "WebSocket URL of SUT", "ws://localhost:3001")]);

            // handlerMock.SetupAllProperties();
            handlerMock.Setup(m => m.Channel).Returns(SmartdoorHandler.DefaultChannel);
            handlerMock.Setup(m => m.SupportedLabels).Returns(supportedLabels);
            handlerMock.Setup(m => m.Configuration).Returns(handlerConfig);
            handlerMock.Setup(m => m.ToPhysicalLabel(It.IsAny<string>()))
                    .ReturnsAsync((string value) =>
                    {
                        return ByteString.CopyFromUtf8(value);
                    });

            return handlerMock;
        }
    }
}