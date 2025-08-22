﻿﻿﻿using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;

using Microsoft.Net.Http.Headers;

using log4net;

using gg.core.util;

namespace SmartdoorAdapter.Adapter
{
    /// <summary>
    /// Defines the state the ManagedClientWebSocket can be in
    /// </summary>
    public enum ManagedSocketState
    {
        NotConnected,
        Connecting,
        Connected,
        ConnectionCancelled,
        Listening,
        Closing,
        Closed,
        Error
    }

    public static class ManagedSocketStateExtensions
    {
        public static bool CanBeClosed(this ManagedSocketState state)
        {
            return state == ManagedSocketState.Connecting
                || state == ManagedSocketState.Connected
                || state == ManagedSocketState.Listening
                || state == ManagedSocketState.Error
                || state == ManagedSocketState.ConnectionCancelled;
        }

        public static bool IsConnected(this ManagedSocketState state)
        {
            return state == ManagedSocketState.Connected
                || state == ManagedSocketState.Listening;
        }
    }

    /// <summary>
    /// This class provides a simplified interface to the standard ClientWebSocket (at the cost of reduced
    /// flexibility). The ManagedClientWebSocket extends the functionality of the standard ClientWebSocket
    /// with the following functionalities:
    ///
    /// * iterative connection with customizable backoff times
    /// * asynchronous listening on a connection
    /// * events on receiving binary or text data
    /// * event for closing
    /// * unlike the ClientWebSocket, this implementation can be closed and
    ///   opened again as needed.
    /// * context related exception handling, only raising exceptions when the behaviour of the underlying
    ///   socket is an error which cannot be handled.
    ///
    /// </summary>
    public class ManagedClientWebSocket : IDisposable
    {
        /// <summary>
        /// Standard result returned in case a 'Receive' operation is canceled.
        /// </summary>
        public static readonly WebSocketReceiveResult OperationCanceledResult =
            new(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure,
                            "Receive canceled because the socket was closed.");

        private static readonly ILog log = LogManager.GetLogger(typeof(ManagedClientWebSocket));

        private static readonly IEnumerable<ManagedSocketState> CloseableStates = [
            ManagedSocketState.Connecting,
            ManagedSocketState.Connected,
            ManagedSocketState.Listening,
            ManagedSocketState.Error,
            ManagedSocketState.ConnectionCancelled
        ];

        /// <summary>
        /// Maximum number of attempts during the Close state to cancel a connection before giving up.
        /// Apparently this takes quite some time.
        /// </summary>
        private static readonly int MaxCancelationAttempts = 25;

        /// <summary>
        /// Lock object used to prevent connecting interfering with closing
        /// </summary>
        private readonly object stateLock = new();

        /// <summary>
        /// (Standard) Socket used to connect to a webserver
        /// </summary>
        private ClientWebSocket? client;

        /// <summary>
        /// Last known state of the client captured after the client has been disposed.
        /// </summary>
        private WebSocketState closingState = WebSocketState.None;

        /// <summary>
        /// Current uri to connect to
        /// </summary>
        private Uri? socketUri;

        /// <summary>
        /// Setting whether or not the client should connect response details
        /// See ClientWebSocket.CollectHttpResponseDetails
        /// </summary>
        private readonly bool collectResponseDetails = true;

        /// <summary>
        /// Connection timeout in ms
        /// </summary>
        private int timeoutMs;

        /// <summary>
        /// Size of the receive buffer
        /// </summary>
        private readonly int bufferSize;

        /// <summary>
        /// Tokensource which can be used to cancel the connection
        /// </summary>
        private CancellationTokenSource? connectCancellationSource;
        private CancellationToken connectCancellationToken;

        /// <summary>
        /// Event callback when binary data has been received after connecting.
        /// </summary>
        public EventHandler<ArraySegment<byte>>? OnBinaryReceived;

        /// <summary>
        /// Event callback when text has been received after connecting.
        /// </summary>
        public EventHandler<string>? OnTextReceived;

        /// <summary>
        /// Event callback when closing is complete.
        /// </summary>
        public EventHandler<(WebSocketCloseStatus? status, string reason)>? OnClosed;

        /// <summary>
        /// Auth token used during the connection
        /// </summary>
        public string apiKey { get; set; }

        /// <summary>
        /// Name of the socket (mostly used for debugging)
        /// </summary>
        public string Name { get; set; } = "Managed socket";

        /// <summary>
        /// Gets/sets the uri to connect to. If the socket is connected, connecting
        /// or listening this will raise a warning as changing the uri at that point
        /// won't affect the connection.
        /// </summary>
        public Uri? ConnectionUri
        {
            get => socketUri;
            set
            {
                if (State != ManagedSocketState.NotConnected && State != ManagedSocketState.Closed)
                {
                    log.Warn("Changing the Uri only has effect prior to connecting.");
                }

                socketUri = value;
            }
        }

        /// <summary>
        /// Current state
        /// </summary>
        public ManagedSocketState State
        {
            get;
            private set;
        } = ManagedSocketState.NotConnected;

        /// <summary>
        /// State of the underlying ClientWebSocket
        /// </summary>
        public WebSocketState SocketState =>
            client == null
            ? closingState
            : client.State;

        /// <summary>
        /// Connection timeout used when connecting. If the connection is not
        /// complete within the duration of this timeout, the connection will
        /// be terminated and restarted.
        /// </summary>
        public int ConnectionTimeout
        {
            get => timeoutMs;
            set => timeoutMs = value;
        }

        /// <summary>
        /// Maximum number of attempts this class will try to connect.
        /// Set to 0 or less to have no limit on connection attempts.
        /// The default is 1
        /// </summary>
        public int MaxConnectionAttempts
        {
            get;
            set;
        } = 1;

        #region --- Con/Destructors -----------------------------------------------------------------------------------

        public ManagedClientWebSocket(Uri? uri = null, string? authToken = null,
            int defaultBufferSize = 2048,
            int defaultTimeout = 5000,
            bool collectHttpResponseDetails = true)
        {
            ConnectionUri = uri;

            bufferSize = defaultBufferSize;
            timeoutMs = defaultTimeout;
            apiKey = authToken ?? string.Empty;
            collectResponseDetails = collectHttpResponseDetails;
        }

        ~ManagedClientWebSocket()
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
            if (disposing && client != null)
            {
                if (client.State == WebSocketState.Open)
                {
                    try
                    {
                        Close(WebSocketCloseStatus.NormalClosure, "Client disposed");
                    }
                    catch (Exception ex)
                    {
                        // log the exception but ignore it otherwise
                        log.Error($"Exception while closing during Dispose(), {ex}");
                    }
                    finally
                    {
                        if (client != null)
                        {
                            client.Dispose();
                            client = null;
                        }
                    }
                }
            }
        }

        #endregion

        #region --- Public methods ------------------------------------------------------------------------------------

        /// <summary>
        /// Connect to the ConnectionUri. If the server doesn't reply within the ConnectionTimeout, this will
        /// retry until the MaxConnectionAttempts has been exceeded. If the connection succeeds the State will
        /// be set to Connected. If the connection fails, the State and logs will reflect the cause.
        /// </summary>
        /// <param name="backoffTimeMs"></param>
        /// <returns>This ManagedClientWebSocket</returns>
        public async Task<ManagedClientWebSocket> Connect(int backoffTimeMs = 2000)
        {
            Contract.Requires(ConnectionUri != null);
            Contract.Requires(State == ManagedSocketState.NotConnected || State == ManagedSocketState.Closed);

            var attempt = 0;
            var backoffTime = backoffTimeMs;

            TransitionToState(
                        expectedStates: [ManagedSocketState.NotConnected, ManagedSocketState.Closed],
                        newState: ManagedSocketState.Connecting,
                        transitionAction: () =>
                        {
                            closingState = WebSocketState.None;

                            connectCancellationSource = new CancellationTokenSource();
                            connectCancellationToken = connectCancellationSource.Token;
                        });

            var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(connectCancellationToken);
            timeoutTokenSource.CancelAfter(timeoutMs);

            while (State == ManagedSocketState.Connecting
                && (MaxConnectionAttempts <= 0 || attempt < MaxConnectionAttempts))
            {
                try
                {
                    TransitionToState(
                        expectedStates: [ManagedSocketState.Connecting],
                        newState: ManagedSocketState.Connected,
                        transitionAction: () =>
                        {
                            // if the client was already created, dispose it.
                            client?.Dispose();
                            client = CreateSocket(apiKey, collectResponseDetails);

                            client!.ConnectAsync(ConnectionUri!, timeoutTokenSource.Token)
                                .ConfigureAwait(false)
                                .GetAwaiter()
                                .GetResult();
                        }
                    );
                }
                catch (Exception ex)
                {
                    if (ex is TaskCanceledException && connectCancellationToken.IsCancellationRequested)
                    {
                        TransitionToState([ManagedSocketState.Connecting], ManagedSocketState.ConnectionCancelled);
                    }
                    else
                    {
                        TransitionToState([ManagedSocketState.Connecting],
                            HandleConnectionException(log, State, ex, ++attempt, MaxConnectionAttempts, backoffTime));

                        if (State == ManagedSocketState.Connecting)
                        {
                            try
                            {
                                // Pass cancellation token to allow interruption during close
                                // increase backoff time until 5 x the original time. Advanced heuristics at play here...
                                backoffTime = Math.Min(backoffTime + backoffTimeMs, 5 * backoffTimeMs);
                                await Task.Delay(backoffTime, connectCancellationToken);
                            }
                            catch (TaskCanceledException)
                            {
                                // If the delay is canceled, break out of the loop
                                TransitionToState([ManagedSocketState.Connecting], ManagedSocketState.ConnectionCancelled);
                            }
                        }
                    }
                }
            }

            return this;
        }

        /// <summary>
        /// Start listening to the connection for incoming messages until the state changes
        /// from listening to anything else.
        /// </summary>
        /// <returns></returns>
        public async Task Listen()
        {
            byte[] buffer = new byte[bufferSize];

            TransitionToState([ManagedSocketState.Connected], ManagedSocketState.Listening);

            while (State == ManagedSocketState.Listening)
            {
                try
                {
                    await Receive(buffer!);
                }
                catch (Exception)
                {
                    // Receive should have dealt with the majority of the exceptios. We log the error
                    // here to make sure the logs reflect the socket was listening.
                    log.Error($"Listening canceled due to an exception while receiving.");
                    throw;
                }
            }
        }

        /// <summary>
        /// One-off send of a text message
        /// </summary>
        /// <param name="message"></param>
        /// <param name="timeoutMs"></param>
        /// <returns></returns>
        public async Task SendText(string message, int timeoutMs = 2000)
        {
            Contract.Requires(client != null);
            Contract.Requires(!string.IsNullOrEmpty(message));
            Contract.Requires(State.IsConnected());

            async Task sendOperation() =>
                await client!.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, new CancellationTokenSource(timeoutMs).Token);

            await InvokeSocketOperation("SendText", sendOperation);
        }

        public async Task SendBinary(byte[] message, int timeoutMs = 2000)
        {
            Contract.Requires(client != null);
            Contract.Requires(message != null && message.Length > 0);
            Contract.Requires(State.IsConnected());

            async Task sendOperation() =>
                    await client!.SendAsync(message!, WebSocketMessageType.Binary, true, new CancellationTokenSource(timeoutMs).Token);

            await InvokeSocketOperation("SendBinary", sendOperation);
        }

        /// <summary>
        /// Receive data from the connection in the given buffer. If execution succeeds either OnBinaryReceived,
        /// OnTextReceived will be called. Additionally the WebSocketReceiveResult will be returned for further
        /// inspection if needed. Should the operation be canceled due to the socket being closed, this
        /// method will consume and handle those exceptions accordingly.
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns>The received result or OperationCanceledResult in case the operation was canceled.</returns>
        public async Task<WebSocketReceiveResult?> Receive(byte[] buffer)
        {
            Contract.Requires(client != null);
            Contract.Requires(buffer != null && buffer.Length > 0);

            async Task<WebSocketReceiveResult> receiveOperation()
            {
                var result = await client!.ReceiveAsync(new ArraySegment<byte>(buffer!), CancellationToken.None);

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Binary:
                        OnBinaryReceived?.Invoke(this, new ArraySegment<byte>(buffer!, 0, result.Count));
                        break;

                    case WebSocketMessageType.Text:
                        OnTextReceived?.Invoke(this, Encoding.UTF8.GetString(buffer!, 0, result.Count));
                        break;

                    case WebSocketMessageType.Close:
                        Close();
                        break;

                    default:
                        log.Warn($"MessageType: {result.MessageType} is not implemented.");
                        break;
                }

                return result;
            }

            return await InvokeSocketOperation("Receive", OperationCanceledResult, receiveOperation);
        }

        public void Close(WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure,
                            string? message = null,
                            int timeoutMs = 2000)
        {

            if (State == ManagedSocketState.Connecting)
            {
                CancelConnectionAttempt();
            }

            // need a lock as close might be called twice by the caller
            TransitionToState(CloseableStates, ManagedSocketState.Closed,
                () =>
                {
                    State = ManagedSocketState.Closing;

                    try
                    {
                        // allow close to be called on NotConnected, so check if the client exists
                        if (client != null)
                        {
                            var description = string.IsNullOrEmpty(client.CloseStatusDescription)
                                    ? "Reason is unknown."
                                    : client.CloseStatusDescription;

                            CloseClient(client, closeStatus, message, timeoutMs);

                            OnClosed?.Invoke(this, (client.CloseStatus, description));
                        }
                    }
                    catch (Exception e)
                    {
                        log.Error($"Closing was failed to exception: {e}");
                    }
                    finally
                    {
                        closingState = client == null ? WebSocketState.None : client.State;
                        client?.Dispose();
                        client = null;
                    }
                });
        }

        #endregion

        #region --- Private methods -----------------------------------------------------------------------------------

        private static void CloseClient(ClientWebSocket client,
            WebSocketCloseStatus status,
            string? message, int timeout)
        {
            if (client!.State == WebSocketState.Open)
            {
                client.CloseAsync(status, message, new CancellationTokenSource(timeout).Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            else if (client.State == WebSocketState.CloseReceived)
            {
                client.CloseOutputAsync(status, message, new CancellationTokenSource(timeout).Token)
                    .ConfigureAwait(false)
                    .GetAwaiter()
                    .GetResult();
            }
            else if (client.State == WebSocketState.Connecting)
            {
                client.Abort();
            }
        }

        private static bool IsRecoverable(SocketError errorCode) =>

            errorCode == SocketError.ConnectionRefused
                || errorCode == SocketError.HostUnreachable
                || errorCode == SocketError.HostNotFound
                || errorCode == SocketError.HostDown;

        private static ClientWebSocket CreateSocket(string? authToken = null, bool collectHttpResponseDetails = true)
        {
            var socket = new ClientWebSocket();

            if (authToken != null)
            {
                socket!.Options.SetRequestHeader(HeaderNames.Authorization, authToken);
            }

            socket.Options.CollectHttpResponseDetails = collectHttpResponseDetails;

            return socket;
        }

        private static ManagedSocketState HandleConnectionException(
            ILog log,
            ManagedSocketState currentState,
            Exception ex,
            int attempt,
            int maxAttempts,
            int backoffTime)
        {
            // still connecting or did we cancel ?
            if (currentState == ManagedSocketState.Connecting)
            {
                var nextState = GetNextConnectionState(currentState, ex, attempt, maxAttempts);

                if (nextState == ManagedSocketState.Error)
                {
                    // exceeded attempts is a 'normal' exit case
                    if (!HasExceedAttempts(attempt, maxAttempts))
                    {
                        // no need to log, GetNextConnectionState logs all details
                        throw ex;
                    }

                    return nextState;
                }
                else
                {
                    LogConnectionAttempt(log, attempt, maxAttempts, backoffTime);
                }

                return nextState;
            }

            return currentState;
        }

        private static ManagedSocketState GetNextConnectionState(ManagedSocketState currentState, Exception ex, int attempt, int maxAttempts)
        {
            // Close() has been called while connecting
            if (currentState == ManagedSocketState.Closed || currentState == ManagedSocketState.Closing)
            {
                log.Warn("Client closed while connecting.");
                return currentState;
            }
            else
            {
                if (HasExceedAttempts(attempt, maxAttempts))
                {
                    log.Error($"Could not connect with {maxAttempts} attempts. Exception {ex}.");
                    return ManagedSocketState.Error;
                }
                else if (ex is WebSocketException wex)
                {
                    var errorCode = GetSocketErrorCode(wex);

                    // see if the error code is recoverable at a later time... perhaps
                    if (!errorCode.HasValue || !IsRecoverable(errorCode.Value))
                    {
                        log.Error($"Can't recover from {errorCode}, exception {ex}.");
                        return ManagedSocketState.Error;
                    }
                }
                // canceled means no response before a timeout occured
                else if (ex is not TaskCanceledException)
                {
                    log.Error($"Unexpected exception while connecting: {ex}.");
                    return ManagedSocketState.Error;
                }
            }

            // continue connecting, exception was TaskCanceledException
            return ManagedSocketState.Connecting;
        }

        private static bool HasExceedAttempts(int attempt, int maxAttempts) =>
            maxAttempts > 0 && attempt >= maxAttempts;

        private static void LogConnectionAttempt(ILog log, int attempt, int maxAttempts, int backoffTime)
        {
            if (maxAttempts <= 0)
            {
                log.Info($"Retrying in {backoffTime / 1000f} seconds...");
            }
            else if (attempt < maxAttempts)
            {
                log.Info($"Attempt {attempt}/{maxAttempts}. Retrying in {backoffTime / 1000f} seconds...");
            }
            else
            {
                log.Error($"MaxConnectionAttempts reached, aborting connection attempt.");
            }
        }

        private bool TransitionToState(
            IEnumerable<ManagedSocketState> expectedStates,
            ManagedSocketState newState,
            Action? transitionAction = null,
            bool logTransition = true)
        {

            if (logTransition)
            {
                log.Debug($"{Name} waiting state transition lock.");
            }

            var lockTimeout = TimeSpan.FromSeconds(10); // Longer timeout for network operations
            if (Monitor.TryEnter(stateLock, lockTimeout))
            {
                try
                {
                    var expectedStatesText = string.Join(", ", expectedStates);

                    if (expectedStates.Any(s => State == s))
                    {
                        if (logTransition)
                        {
                            log.Debug($"{Name} moving from [{expectedStatesText}] to {newState}.");
                        }

                        transitionAction?.Invoke();
                        State = newState;

                        if (logTransition)
                        {
                            log.Debug($"{Name} moved from [{expectedStatesText}] to {newState}.");
                        }

                        return true;
                    }
                    else
                    {
                        log.Warn($"{Name} was not in the expected state {expectedStatesText} and CANNOT move to {newState}.");
                        return false;
                    }
                }
                finally
                {
                    Monitor.Exit(stateLock);
                }
            }
            else
            {
                log.Error($"{Name} failed to acquire state transition lock within {lockTimeout.TotalSeconds} seconds. Potential deadlock detected.");
                return false;
            }
        }

        private void CancelConnectionAttempt()
        {
            if (connectCancellationSource != null)
            {
                var attempt = 0;
                log.Info($"{Name}: Cancelling connection...");
                connectCancellationSource.Cancel();

                while (State == ManagedSocketState.Connecting)
                {
                    if (attempt < MaxCancelationAttempts)
                    {
                        Task.Delay(1000).ConfigureAwait(false).GetAwaiter().GetResult();
                        attempt++;
                    }
                    else
                    {
                        log.Info($"{Name}: Was not able to cancel connecting state...");
                        throw new InvalidOperationException($"{Name}: Was not able to cancel connecting state in {MaxCancelationAttempts} attempts.");
                    }
                }

                log.Info($"{Name}: Cancellation confirmed, current state = {State}.");
            }
            else
            {
                log.Warn($"{Name}: State is connecting but no connectCancellationSource is defined.");
            }
        }

        private static SocketError? GetSocketErrorCode(Exception exception)
        {
            var current = exception;
            while (current != null)
            {
                if (current is SocketException socketException)
                {
                    return socketException.SocketErrorCode;
                }

                current = current.InnerException;
            }

            return null;
        }

        /// <summary>
        /// Invokes the given operation and deals with all the expected exceptions the operation can cause.
        /// Refer to the code for the specifics.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="operationName"></param>
        /// <param name="operation"></param>
        /// <param name="operationCanceledResult"></param>
        /// <returns></returns>
        private async Task<T> InvokeSocketOperation<T>(string operationName, T operationCanceledResult, Func<Task<T>> operation) where T : class
        {
            try
            {
                return await operation();
            }
            catch (Exception exception)
            {
                HandleOperationException(exception, operationName);
                return operationCanceledResult;
            }
        }

        private async Task InvokeSocketOperation(string operationName, Func<Task> operation)
        {
            try
            {
                await operation();
            }
            catch (Exception exception)
            {
                HandleOperationException(exception, operationName);
            }
        }

        private void HandleOperationException(Exception e, string operationName)
        {
            if (e is OperationCanceledException ocex)
            {
                if (State == ManagedSocketState.Closed || State == ManagedSocketState.Closing)
                {
                    log.Warn($"{operationName} canceled due to the socket being closed.");
                }
                else
                {
                    log.Error($"{operationName} canceled. Exception {ocex}");
                    TransitionToState([ManagedSocketState.Connected, ManagedSocketState.Listening],
                        ManagedSocketState.Error);
                    throw ocex;
                }
            }
            else if (e is WebSocketException wex)
            {
                if (State == ManagedSocketState.Closed || State == ManagedSocketState.Closing)
                {
                    log.Warn("{operationName} canceled, client closed.");
                }
                else if (wex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                {
                    log.Warn($"{operationName}: The connection got closed unexpectedly.");
                    Close();
                }
                else
                {
                    // leave it up to the caller on how to deal with this
                    log.Error($"Receive received an exception {wex}.");
                    TransitionToState([ManagedSocketState.Connected, ManagedSocketState.Listening],
                        ManagedSocketState.Error);
                    throw wex;
                }
            }
            else
            {
                log.Error($"Receive received an exception {e}.");
                throw e;
            }
        }

        #endregion
    }
}
