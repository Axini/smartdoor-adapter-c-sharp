using SmartdoorAdapter.Tests.Util;
using System.Net.WebSockets;
using System.Text;

namespace SmartdoorAdapter.Tests.UnitTests
{
    /// <summary>
    /// Test KestralTestTemplate in various scenarios 
    /// </summary>
    [TestClass]
    public class KestralTestTemplateTests
    {
        /// <summary>
        /// Check if sockets open correctly 
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ConnectTest()
        {
            await KestralTestTemplate.RunAsyncTest(
                (context, serverSocket) =>
                {
                    Assert.IsTrue(serverSocket.State == WebSocketState.Open);
                    return Task.CompletedTask;
                },
                (clientSocket) =>
                {
                    Assert.IsTrue(clientSocket.State == WebSocketState.Open);
                    return Task.CompletedTask;
                }
            );
        }

        /// <summary>
        /// Check if send/receive works as expected
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task SendReceiveTest()
        {
            var testText = "hello world";

            await KestralTestTemplate.RunAsyncTest(
                async (context, serverSocket) =>
                {
                    var buffer = new byte[1024];
                    var result = await serverSocket.ReceiveAsync(buffer, CancellationToken.None);

                    Assert.IsTrue(result.MessageType == WebSocketMessageType.Text);
                    Assert.IsTrue(result.EndOfMessage);

                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Assert.AreEqual(testText, text);
                },
                async (clientSocket) =>
                {
                    await clientSocket.SendAsync(
                        new ArraySegment<byte>(Encoding.UTF8.GetBytes(testText)),
                        WebSocketMessageType.Text,
                        true,
                        CancellationToken.None
                    );
                }
            );
        }

        /// <summary>
        /// Test the connect / close handshake
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task ConnectCloseTest()
        {
            var closeText = "normal closure";

            await KestralTestTemplate.RunAsyncTest(
                async (context, serverSocket) =>
                {
                    var buffer = new byte[1024];
                    var result = await serverSocket.ReceiveAsync(buffer, CancellationToken.None);

                    Assert.IsTrue(result.CloseStatus == WebSocketCloseStatus.NormalClosure);
                    Assert.IsTrue(result.MessageType == WebSocketMessageType.Close);
                    Assert.IsTrue(result.Count == 0);
                    Assert.IsTrue(result.CloseStatusDescription != null
                            && result.CloseStatusDescription.Equals(closeText));

                    Assert.IsTrue(serverSocket.State == WebSocketState.CloseReceived);

                    await serverSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, closeText, CancellationToken.None);

                    Assert.IsTrue(serverSocket.State == WebSocketState.Closed);
                },
                async (clientSocket) =>
                {
                    await clientSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, closeText, CancellationToken.None);

                    Assert.IsTrue(clientSocket.State == WebSocketState.Closed);
                }
            );
        }
    }
}
