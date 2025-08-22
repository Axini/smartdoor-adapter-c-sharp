using System.Net.WebSockets;

using Google.Protobuf;

using SmartdoorAdapter.Proto;

namespace SmartdoorAdapter.Adapter
{
    /// <summary>
    /// The BrokerConnection component manages the WebSocket connection to AMP. 
    /// It performs callbacks on the AdapterCore to signal when the WebSocket connection 
    /// was opened or closed and when a message is received from AMP. 
    /// (https://course02.axini.com/docs/tech/adapters/plugin_adapters.html#brokerconnection)
    /// </summary>
    public interface IBroker
    {
        /// <summary>
        /// Returns the state of the underlying connection to the AMP
        /// </summary>
        ManagedSocketState State { get; }

        /// <summary>
        /// EventHandler called when the connection to the AMP has closed
        /// </summary>
        event EventHandler<(WebSocketCloseStatus? status, string reason)>? OnClosed;

        /// <summary>
        /// EventHandler called when the broker receives a message from the AMP.
        /// Listen() needs to be invoked.
        /// </summary>
        event EventHandler<Message>? OnMessage;

        /// <summary>
        /// Connects to the AMP 
        /// </summary>
        /// <returns>true, connection was successful, false otherwise.</returns>
        bool Connect();

        /// <summary>
        /// Starts listening for messages (async)
        /// </summary>
        /// <returns></returns>
        Task Listen();

        /// <summary>
        /// Announce the broker with the AMP. After this the broker is available
        /// for test runs on the AMP.
        /// </summary>
        /// <param name="adapterName"></param>
        /// <param name="supportedLabels"></param>
        /// <param name="configuration"></param>
        /// <returns></returns>
        Task SendAnnounceMessage(string adapterName, List<Label> supportedLabels, Configuration configuration);

        /// <summary>
        /// Informs the AMP of an error
        /// </summary>
        /// <param name="errorMessage"></param>
        /// <returns></returns>
        Task SendErrorMessage(string errorMessage);

        /// <summary>
        /// Notifies the AMP the broker is ready to start testing.
        /// </summary>
        /// <returns></returns>
        Task SendReadyMessage();

        /// <summary>
        /// Send the SUT response to the AMP
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="sutMessage"></param>
        /// <returns></returns>
        Task SendResponseMessage(string channel, string sutMessage, ByteString physicalLabel);

        /// <summary>
        /// Send a stimulus to the AMP correlating to a stimulus send to 
        /// the SUT.
        /// </summary>
        /// <param name="label"></param>
        /// <param name="physicalLabel"></param>
        /// <param name="correlationId"></param>
        /// <returns></returns>
        Task SendStimulus(Label label, ByteString physicalLabel, ulong correlationId);

        /// <summary>
        /// Close the broker and connection to the AMP. 
        /// </summary>
        /// <param name="closeStatus"></param>
        /// <param name="message"></param>
        void Close(WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure, string? message = null);
    }
}