﻿using System.Net.WebSockets;

using Google.Protobuf;

using log4net;

using gg.core.util;

using SmartdoorAdapter.Proto;

namespace SmartdoorAdapter.Adapter
{
    public class BrokerConnection : IDisposable, IBroker
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(BrokerConnection));

        private readonly ManagedClientWebSocket connection;

        public event EventHandler<Message>? OnMessage;
        public event EventHandler<(WebSocketCloseStatus? status, string reason)>? OnClosed;

        public ManagedSocketState State => connection.State;

        #region --- Con/Destructors -----------------------------------------------------------------------------------

        /// <summary>
        /// Creates a new BrokerConnection 
        /// </summary>
        /// <param name="uri">Non null uri of the AMP.</param>
        /// <param name="apiKey">Optional basic auth token used in the Authority header.</param>
        /// <param name="maxConnectionAttempts">Maximum number of attempts to connect to the AMP before aborting. Set to 
        /// 0 or less to have no limit. The default is 1.</param>
        /// <param name="defaultTimeout">Default connection timeout in ms. Defaults to 2000ms</param>
        public BrokerConnection(Uri uri, string? apiKey = null, int maxConnectionAttempts = 1, int defaultTimeout = 2000)
        {
            Contract.RequiresNotNull(uri);

            connection = new ManagedClientWebSocket(uri, apiKey, defaultTimeout: defaultTimeout)
            {
                MaxConnectionAttempts = maxConnectionAttempts
            };

            connection.OnBinaryReceived += HandleBinaryMessage;
            connection.OnTextReceived += (sender, text) => log.Error($"Text message received from AMP: {text}.");
            connection.OnClosed += (_, context) => OnClosed?.Invoke(this, context);
            connection.Name = "Broker connection";
        }

        ~BrokerConnection()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && connection != null)
            {
                connection.Dispose();
            }
        }

        #endregion

        #region --- Public methods ------------------------------------------------------------------------------------

        public bool Connect()
        {
            var result = connection.Connect().ConfigureAwait(false).GetAwaiter().GetResult();

            return result != null && result.State == ManagedSocketState.Connected;
        }

        public void Close(WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure, string? message = null)
        {
            connection.Close(closeStatus, message);
        }

        public async Task SendAnnounceMessage(
                string adapterName,
                List<Label> supportedLabels,
                Configuration configuration)
        {
            Contract.RequiresNotNullOrEmpty(adapterName);
            Contract.RequiresNotNullOrEmpty(supportedLabels);
            Contract.RequiresNotNull(configuration);

            await Send(AxiniProtobuf.CreateMsgAnnouncement(adapterName,
                                                supportedLabels, configuration!));
        }

        public async Task Send(Message message)
        {
            Contract.RequiresNotNull(message);

            await connection.SendBinary(message.ToByteArray());
        }

        public async Task SendReadyMessage()
        {
            await Send(AxiniProtobuf.CreateMsgReady());
        }

        public async Task SendStimulus(Label label, ByteString physicalLabel, ulong correlationId)
        {
            var ampLabel = AxiniProtobuf.CreateLabel(label, physicalLabel,
                                                        UnixTimeNano(), correlationId);

            var message = AxiniProtobuf.CreateMsgLabel(ampLabel);

            await connection.SendBinary(message.ToByteArray());
        }

        public async Task SendErrorMessage(string errorMessage)
        {
            await Send(AxiniProtobuf.CreateMsgError(errorMessage));
        }

        public async Task SendResponseMessage(string channel, string sutMessage, ByteString physicalLabel)
        {
            var label = AxiniProtobuf.CreateResponse(sutMessage.ToLower(), channel);
            var timeStamp = UnixTimeNano();
            var responseLabel = AxiniProtobuf.CreateLabel(label, physicalLabel, timeStamp);

            await Send(AxiniProtobuf.CreateMsgLabel(responseLabel));
        }

        public async Task Listen()
        {
            try
            {
                await connection.Listen();
            }
            catch (WebSocketException e)
            {
                if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    // normal behaviour
                    log.Info($"Broker connection closed while listening.");
                }
                else
                {
                    throw;
                }
            }
            catch (Exception e)
            {
                log.Error($"Broker threw an exception while listening {e}");
                throw;
            }
        }

        #endregion

        #region --- Private methods -----------------------------------------------------------------------------------

        private void HandleBinaryMessage(object? _, ArraySegment<byte> buffer)
        {
            if (TryParseMessage(buffer, out var message))
            {
                OnMessage?.Invoke(this, message!);
            }
            else
            {
                log.Error("InvalidProtocolBufferException: Failed to parse message from AMP");
            }
        }

        private static bool TryParseMessage(ArraySegment<byte> buffer, out Message? result)
        {
            try
            {
                result = Message.Parser.ParseFrom(buffer.Array, buffer.Offset, buffer.Count);
                return true;
            }
            catch (InvalidProtocolBufferException e)
            {
                result = null;
                log.Error($"Failed to parse message: {e}");
                return false;
            }
        }

        private static ulong UnixTimeNano()
        {
            DateTime epochStart = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (ulong)((DateTime.UtcNow - epochStart).Ticks * 100);
        }

        #endregion
    }
}
