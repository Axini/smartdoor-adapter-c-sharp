using SmartdoorAdapter.Adapter;
using SmartdoorAdapter.Tests.Util;

namespace SmartdoorAdapter.Tests.UnitTests
{
    [TestClass]
    public class SmartdoorHandlerTests
    {
        /// <summary>
        /// Checks if the handler sends a reset on start
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task StartTest()
        {
            var isServerDone = false;

            await KestralTestTemplate.RunTestWithCustomClient(

                // server is playing the role of SUT
                async (context, serverSocket) =>
                {
                    // expect the reset message
                    try
                    {
                        var text = await serverSocket.ReceiveText();

                        Assert.IsNotNull(text);
                        Assert.AreEqual(text, SmartdoorHandler.ResetText);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.Message);
                    }
                    finally
                    {
                        isServerDone = true;
                    }
                },
            (uri) =>
            {
                var handler = new SmartdoorHandler()
                {
                    Configuration = AxiniProtobuf.CreateConfiguration([
                                        AxiniProtobuf.CreateItem("url", "WebSocket URL of SUT", uri.AbsoluteUri)])
                };

                try
                {
                    _ = handler.Start();

                    // let the server finish up its test
                    TaskUtil.WaitUntil(() => isServerDone, timeStep: 100);
                }
                catch (Exception ex)
                {
                    Assert.Fail(ex.Message);
                }
            }
            );
        }

        /// <summary>
        /// Stop the handler while it's trying to connect. This should not result in any exceptions
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task StopHandlerWhileStartingTest()
        {
            var isClientDone = false;
            await KestralTestTemplate.RunTestWithCustomClient(

                (context, serverSocket) =>
                {
                    TaskUtil.WaitUntil(() => isClientDone, timeStep: 20000);
                    return Task.CompletedTask;
                },
                async (uri) =>
                {
                    var handler = new SmartdoorHandler()
                    {
                        Configuration = AxiniProtobuf.CreateConfiguration([
                                            AxiniProtobuf.CreateItem("url", "WebSocket URL of SUT", uri.AbsoluteUri + "/does_not_exist")]),
                        MaxConnectionAttempts = 10,
                        ConnectTimeout = 200
                    };

                    try
                    {
                        // this should fail to connect multiple times
                        _ = handler.Start();

                        // give some time to fail the connection
                        await Task.Delay(1000);

                        // stop the handler. This should cancel the connection attempts, without exceptions
                        handler.Stop();

                        // give some time to stop
                        await Task.Delay(1500);
                    }
                    catch (Exception ex)
                    {
                        Assert.Fail(ex.Message);
                    }
                    finally
                    {
                        isClientDone = true;
                    }
                }
            );
        }
    }
}