using log4net;
using SmartdoorAdapter.Tests.Microsoft.AspNetCore.WebSockets.Test;
using System.Net.WebSockets;

namespace SmartdoorAdapter.Tests.Util
{
    public static class KestralTestTemplate
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(KestralTestTemplate));

        private static readonly string WsLocalHost = "ws://localhost:";

        public static async Task RunAsyncTest(
            Func<HttpContext, WebSocket, Task> serverTests,
            Func<WebSocket, Task> clientTests
        )
        {
            try
            {
                await using var server = KestrelWebSocketHelpers.CreateServer(
                    new Log4NetLoggerFactory(),
                    out var port,
                    async context =>
                    {
                        Assert.IsTrue(context.WebSockets.IsWebSocketRequest);
                        var serverSocket = await context.WebSockets.AcceptWebSocketAsync();

                        await serverTests(context, serverSocket);

                        log.Debug("Server test completed");
                    }
                );

                using var clientSocket = new ClientWebSocket();

                var uri = new Uri($"{WsLocalHost}{port}/");
                await clientSocket.ConnectAsync(uri, CancellationToken.None);
                await clientTests(clientSocket);

                log.Debug("Client test completed");
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.ToString());
            }
        }

        // TODO: check if the tests need to return a task
        public static async Task RunTestWithCustomClient(Func<HttpContext, WebSocket, Task> serverTests, Func<Uri, Task> clientTests)
        {
            try
            {
                await using var server = KestrelWebSocketHelpers.CreateServer(
                    new Log4NetLoggerFactory(),
                    out var port,
                    async context =>
                    {
                        Assert.IsTrue(context.WebSockets.IsWebSocketRequest);
                        var serverSocket = await context.WebSockets.AcceptWebSocketAsync();

                        await serverTests(context, serverSocket);

                        log.Debug("Server test completed");
                    }
                );

                await clientTests(new Uri($"{WsLocalHost}{port}/"));
                log.Debug("Client test completed");
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.ToString());
            }
        }

        public static async Task RunTestWithCustomClient(Func<HttpContext, WebSocket, Task> serverTests, Action<Uri> clientTests)
        {
            try
            {
                await using var server = KestrelWebSocketHelpers.CreateServer(
                    new Log4NetLoggerFactory(),
                    out var port,
                    async context =>
                    {
                        Assert.IsTrue(context.WebSockets.IsWebSocketRequest);
                        var serverSocket = await context.WebSockets.AcceptWebSocketAsync();

                        await serverTests(context, serverSocket);

                        log.Debug("Server test completed");
                    }
                );

                clientTests(new Uri($"{WsLocalHost}{port}/"));
                log.Debug("Client test completed");
            }
            catch (Exception ex)
            {
                Assert.Fail(ex.ToString());
            }
        }
    }
}
