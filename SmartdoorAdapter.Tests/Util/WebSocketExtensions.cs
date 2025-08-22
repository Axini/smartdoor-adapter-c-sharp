using System.Net.WebSockets;
using System.Text;

namespace SmartdoorAdapter.Tests.Util
{
    public static class WebSocketExtensions
    {
        public static async Task<string> ReceiveText(this WebSocket socket, int timeOut = 5000)
        {
            var buffer = new byte[1024];

            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), new CancellationTokenSource(timeOut).Token);

            return Encoding.UTF8.GetString(buffer, 0, result.Count);
        }

        public static async Task<ArraySegment<byte>> ReceiveBinary(this WebSocket socket, int timeOut = 5000)
        {
            var buffer = new byte[1024];

            var result = timeOut <= 0
                ? await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None)
                : await socket.ReceiveAsync(new ArraySegment<byte>(buffer), new CancellationTokenSource(timeOut).Token);

            return new ArraySegment<byte>(buffer, 0, result.Count);
        }

        public static async Task SendText(this WebSocket socket, string text, int timeOut = 5000)
        {
            await socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, new CancellationTokenSource(timeOut).Token);
        }

        public static async Task SendBinary(this WebSocket socket, byte[] message, int timeOut = 5000)
        {
            await socket.SendAsync(message, WebSocketMessageType.Binary, true, new CancellationTokenSource(timeOut).Token);
        }

    }
}
