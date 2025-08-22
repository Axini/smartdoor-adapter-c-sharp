
using gg.core.util;

using log4net;

using SmartdoorAdapter.Proto;
using System.Net.WebSockets;

namespace SmartdoorAdapter.Adapter
{
    /// <summary>
    /// Describes the different states the adapter can be in.
    /// See https://course02.axini.com/docs/tech/adapters/plugin_adapters.html#index-3
    /// for the more information and state transitions.
    /// </summary>
    public enum AdapterCoreState
    {
        /// <summary>
        /// Initial state, no connection has been started.
        /// </summary>
        Disconnected,

        /// <summary>
        /// Adapter is connected to the AMP
        /// </summary>
        Connected,

        /// <summary>
        /// Adapter is announced and available on the AMP
        /// </summary>
        Announced,

        /// <summary>
        /// Adapter has received a test configuraiton from the AMP
        /// </summary>
        Configured,

        /// <summary>
        /// Adapter is ready to begin testing
        /// </summary>
        Ready,

        /// <summary>
        /// The adapter has encountered an error
        /// </summary>
        Error,

        /// <summary>
        /// Adapter core is disposed after use. This is for internal bookkeeping
        /// only.
        /// </summary>
        Disposed
    }

    /// <summary>
    /// The adapter core contains the core of the adapter functionality. It provides
    /// a service to orchestrate between the AMP and the SUT via a IBroker (AMP connection) and
    /// IHandler (SUT connection) respectively.
    /// See https://course02.axini.com/docs/tech/adapters/plugin_adapters.html#index-3
    /// and https://course02.axini.com/docs/tech/adapters/plugin_adapters.html#adaptercore for more information.
    /// </summary>
    public class AdapterCore : IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(AdapterCore));

        /// <summary>
        /// Name as announced on the AMP. This name will show up in the adapters to chose from
        /// </summary>
        private readonly string name;

        /// <summary>
        /// Broker object handling the connection to the AMP.
        /// </summary>
        private readonly IBroker broker;

        /// <summary>
        /// Handler object handling the connection to the SUT
        /// </summary>
        private readonly IHandler handler;

        /// <summary>
        /// Current state of the adapter core.
        /// </summary>
        public AdapterCoreState State
        {
            get;
            private set;
        }

        #region --- Con/Destructors -----------------------------------------------------------------------------------

        /// <summary>
        /// Creates the core of the adapter with the given name, broker and handler.
        /// </summary>
        /// <param name="adapterName">A non null or empty name used to identify this adapter on the AMP</param>
        /// <param name="ampConnection">A non null connection to the AMP. Should not be connected yet. The
        /// adapter assumes no ownership over the object.</param>
        /// <param name="sutHandler">A non null connection to the SUT. Should not be connected yet. The
        /// adapter assumes no ownership over the object.</param>
        public AdapterCore(string adapterName, IBroker ampConnection, IHandler sutHandler)
        {
            Contract.RequiresNotNull(sutHandler);
            Contract.RequiresNotNull(ampConnection);
            Contract.RequiresNotNullOrEmpty(adapterName);

            name = adapterName;
            broker = ampConnection!;
            handler = sutHandler!;

            State = AdapterCoreState.Disconnected;

            broker.OnMessage += HandleAMPMessage;
            broker.OnClosed += HandleAMPClose;

            handler.OnError += HandleSutError;
            handler.OnResponse += HandleSutResponse;
        }

        ~AdapterCore()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool isDisposing)
        {
            if (isDisposing && State != AdapterCoreState.Disposed)
            {
                // The assumption is that the core doesn't necessarily own
                // the broker & handler as their creation / configuration happens
                // outside the core's control. So the dispose is limited
                // to unsubscribing from their event handlers.
                broker.OnMessage -= HandleAMPMessage;
                broker.OnClosed -= HandleAMPClose;

                handler.OnError -= HandleSutError;
                handler.OnResponse -= HandleSutResponse;

                State = AdapterCoreState.Disposed;
            }
        }

        #endregion

        #region --- Public methods ------------------------------------------------------------------------------------

        /// <summary>
        /// Runs a single iteration / test then closes the core.
        /// Requires the adapter to be disconnected at the start and will
        /// leave the adapter disconnected at the end.
        /// </summary>
        /// <returns></returns>
        public async Task Start()
        {
            await AdapterCoreIteration();
        }

        /// <summary>
        /// Runs the tests in a loop until the cancelToken is invoked
        /// and the underlying task is canceled.
        /// Requires the adapter to be disconnected at the start and will
        /// leave the adapter disconnected at the end.
        /// </summary>
        /// <param name="cancelToken"></param>
        /// <returns></returns>
        public async Task Start(CancellationToken cancelToken)
        {
            await Task.Run(async () =>
            {
                try
                {
                    while (!cancelToken.IsCancellationRequested)
                    {
                        await AdapterCoreIteration();

                        // delay to allow the async processes to finish any
                        // reporting
                        await Task.Delay(250);
                    }
                }
                catch (TaskCanceledException tce)
                {
                    if (!cancelToken.IsCancellationRequested)
                    {
                        log.Error($"Adapter mainloop was unexpectingly cancelled: {tce}.");
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"Unexpected exception in AdapterCore's mainloop: {ex}.");
                    throw;
                }
            },
            cancelToken);
        }

        /// <summary>
        ///
        /// </summary>
        public void Stop()
        {
            handler.Stop();
            broker.Close();

            State = AdapterCoreState.Disconnected;
        }

        #endregion

        #region --- Event handlers ------------------------------------------------------------------------------------

        /// <summary>
        /// Parse the ByteBuffer message from AMP to a Protobuf message and call
        /// the appropriate method of this AdapterCore.
        /// </summary>
        /// <param name="bytes"></param>
        private void HandleAMPMessage(object? _, Message message)
        {
            switch (message!.TypeCase)
            {
                case Message.TypeOneofCase.Configuration:
                    {
                        // A test configuration is send from the AMP indicating the test is
                        // about to begin. This will try to connect to the SUT and
                        // if succesful, report ready back to the AMP
                        log.Info("Configuration received from AMP");
                        HandleConfiguration(message.Configuration);
                        break;
                    }
                case Message.TypeOneofCase.Label:
                    {
                        var label = message.Label;
                        log.Info($"Label received from AMP: {label.Label_}.");
                        HandleLabel(label, label.CorrelationId);
                        break;
                    }
                case Message.TypeOneofCase.Error:
                    {
                        log.Info($"Error received from AMP: {message.Error.Message}.");
                        HandleBrokerError(message.Error.Message);
                        break;
                    }
                case Message.TypeOneofCase.Reset:
                    {
                        log.Info("Reset received from AMP.");
                        HandleReset();
                        break;
                    }

                case Message.TypeOneofCase.Announcement:
                case Message.TypeOneofCase.Ready:
                    {
                        log.Error($"Message type '{message.TypeCase}' not supported.");
                        break;
                    }
                default:
                    {
                        log.Error($"Unknown message type '{message.TypeCase}'.");
                        break;
                    }
            }
        }

        private async Task AnnounceAdapterWithAMP()
        {
            if (State == AdapterCoreState.Disconnected)
            {
                State = AdapterCoreState.Connected;

                log.Info("Announcing...");

                try
                {
                    await broker.SendAnnounceMessage(name, handler.SupportedLabels, handler.Configuration!);

                    log.Info("Adapter is Announced to AMP.");
                    State = AdapterCoreState.Announced;
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to send Annoucement to AMP: {ex.Message}.");
                    State = AdapterCoreState.Error;
                    broker.Close();
                }
            }
            else
            {
                var message = "Connection openend while already connected.";
                log.Error(message);
                SendError(message);
            }
        }

        /// <summary>
        /// BrokerConnection: connection is closed, stop the handler.
        /// </summary>
        /// <param name="code"></param>
        /// <param name="reason"></param>
        /// <param name="remote"></param>
        private void HandleAMPClose(object? _, (WebSocketCloseStatus? status, string reason) closeResult)
        {
            State = AdapterCoreState.Disconnected;

            int statusCode = closeResult.status.HasValue ? (int)closeResult.status.Value : -1;
            string message = $"Connection closed with code: {statusCode}, with reason: {closeResult.reason}.";

            // Code 1006 indicates abnormal closure (not defined in .NET enum but valid per RFC 6455)
            if (statusCode == 1006)
            {
                message += " The server may not be reachable.";
            }

            log.Info(message);

            // The SUT connection will be automatically closed by the WebSocket framework
            // when the AMP connection is closed. No need to explicitly call handler.Stop().
        }

        /// <summary>
        /// Configuration received from AMP.
        /// * configure the handler,
        /// * start the handler,
        /// * send ready to AMP (should be done by handler).
        /// </summary>
        /// <param name="configuration"></param>
        private async void HandleConfiguration(Configuration configuration)
        {
            switch (State)
            {
                case AdapterCoreState.Announced:
                    {
                        handler.Configuration = configuration;
                        State = AdapterCoreState.Configured;

                        log.Debug("Connecting to the SUT.");

                        try
                        {
                            // start the SUT handler
                            if (await handler.Start())
                            {
                                log.Info("Sending ready to AMP.");

                                await broker.SendReadyMessage();

                                State = AdapterCoreState.Ready;
                            }
                            else
                            {
                                // Cannot connect to SUT, maybe the SUT hasn't been started
                                // or the configuration is wrong
                                log.Error("Failed to connect to SUT.");

                                // check if the broker is still ok, if so let the AMP
                                // know what went wrong
                                if (broker.State.IsConnected())
                                {
                                    await broker.SendErrorMessage("Failed to connect to SUT.");
                                    broker.Close();
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            log.Error($"Exception while connecting to the SUT, {e}.");

                            // check if the broker is still ok, if so let the AMP
                            // know what went wrong
                            if (broker.State.IsConnected())
                            {
                                await broker.SendErrorMessage($"Failed to connect to SUT, exception: {e}.");
                                broker.Close();
                            }
                        }

                        break;
                    }

                case AdapterCoreState.Connected:
                    {
                        var message = "Configuration received from AMP while not yet announced.";
                        log.Error(message);
                        SendError(message);
                        break;
                    }

                default:
                    {
                        var message = "Configuration received from AMP while already configured.";
                        log.Error(message);
                        SendError(message);
                        break;
                    }
            }
        }

        /// <summary>
        /// Label (stimulus) received from AMP.
        /// * make handler offer the stimulus to the SUT,
        /// * acknowledge the actual stimulus to AMP.
        /// </summary>
        /// <param name="label"></param>
        /// <param name="correlationId"></param>
        private async void HandleLabel(Label label, ulong correlationId)
        {
            if (State == AdapterCoreState.Ready)
            {
                try
                {
                    // We do not check that the label is a stimulus.
                    var sutMessage = label.ToSutMessage();
                    var physicalLabel = await handler.ToPhysicalLabel(sutMessage);

                    log.Info($"Sending stimulus back to AMP: '{label.Label_}'.");
                    await broker.SendStimulus(label, physicalLabel, correlationId);
                    await handler.Stimulate(sutMessage);
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to handle stimulus with label: {label.Label_}, exception: {ex.Message}.");
                    State = AdapterCoreState.Error;
                    broker.Close();
                }
            }
            else
            {
                var message = "Label received from AMP while not ready.";
                log.Error(message);
                SendError(message);
            }
        }

        // Reset message received from AMP.
        private async void HandleReset()
        {
            if (State == AdapterCoreState.Ready)
            {
                await handler.Reset();

                log.Info("Sending ready to AMP.");

                await broker.SendReadyMessage();
            }
            else
            {
                var message = "Reset received from AMP while not ready.";
                log.Error(message);
                SendError(message);
            }
        }

        private void HandleSutError(object? _, Exception e)
        {
            State = AdapterCoreState.Error;

            log.Error($"Error message received from handler({handler.GetType().Name}): {e.Message}.");

            broker.Close(WebSocketCloseStatus.NormalClosure, e.Message);
            handler.Stop();
        }

        private async void HandleSutResponse(object? _, string responseText)
        {
            var physicalLabel = await handler.ToPhysicalLabel(responseText);
            log.Info($"Sending response to AMP: '{responseText.ToLower()}'.");
            await broker.SendResponseMessage(handler.Channel, responseText, physicalLabel);
        }

        /// <summary>
        /// Error message received from AMP.
        /// * close the connection to AMP
        /// </summary>
        /// <param name="message"></param>
        private void HandleBrokerError(string message)
        {
            State = AdapterCoreState.Error;

            log.Error($"Error message received from AMP: {message}.");

            broker.Close(WebSocketCloseStatus.NormalClosure, message);
            handler.Stop();
        }

        #endregion

        #region --- Private methods -----------------------------------------------------------------------------------

        private async void SendError(string errorMessage)
        {
            log.Error("Sending Error to AMP and closing the connection.");

            try
            {
                await broker.SendErrorMessage(errorMessage);
                broker.Close(WebSocketCloseStatus.NormalClosure, errorMessage);
                handler.Stop();
            }
            catch (Exception ex)
            {
                log.Error($"Exception while sending error to AMP: {ex.Message}.");
            }
        }

        private async Task AdapterCoreIteration()
        {
            if (State == AdapterCoreState.Disconnected)
            {
                log.Info("Connecting to AMP.");

                if (broker.Connect())
                {
                    await AnnounceAdapterWithAMP();
                    await broker.Listen();
                }
                // in case of failure there is no need to log the connection failure
                // as the underlying connection code creates plenty of logs.

                // the following shouldn't be strictly necessary as the AMP closes
                // the connection at the end of the test.
                if (broker.State == ManagedSocketState.Connected)
                {
                    broker.Close();
                    handler.Stop();
                }

                State = AdapterCoreState.Disconnected;

                log.Debug("AdapterCoreIteration complete.");
            }
            else
            {
                var message = "Adapter started while already connected.";
                log.Error(message);
                SendError(message);
            }
        }

        #endregion
    }
}
