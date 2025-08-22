﻿﻿﻿﻿using System.Text;

using Google.Protobuf;

using log4net;

using gg.core.util;

using SmartdoorAdapter.Proto;

namespace SmartdoorAdapter.Adapter
{
    public class SmartdoorHandler : IHandler, IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(SmartdoorHandler));

        public static readonly string DefaultChannel = "door";
        private static readonly string PasscodeParameterName = "passcode";

        private static readonly string[] Stimuli = ["open", "close"];
        private static readonly string[] StimuliPasscode = ["lock", "unlock"];
        private static readonly string[] Responses = [
            "opened",
            "closed",
            "locked",
            "unlocked",
            "invalid_command",
            "invalid_passcode",
            "incorrect_passcode",
            "shut_off"
        ];

        public static readonly string ResetLabel = "reset";

        public static readonly string ResetText = "RESET";
        public static readonly byte[] ResetBuffer = Encoding.UTF8.GetBytes(ResetText);
        public static readonly string ResetPerformed = "RESET_PERFORMED";

        private readonly static Configuration defaultConfiguration = AxiniProtobuf.CreateConfiguration([
                AxiniProtobuf.CreateItem("url", "WebSocket URL of SmartDoor SUT", "ws://localhost:3001")]);

        private readonly static List<Label> supportedLabels = CreateSupportedLabels();

        private readonly ManagedClientWebSocket connection;
        private readonly CancellationTokenSource listenCancelToken = new();

        // Message queue to ensure responses are processed in order
        private readonly Queue<string> responseQueue = new Queue<string>();
        private readonly object queueLock = new object();
        private bool isProcessingQueue = false;

        public event EventHandler<string>? OnResponse;
        public event EventHandler<Exception>? OnError;

        public List<Label> SupportedLabels => supportedLabels;

        public Configuration? Configuration { get; set; } = defaultConfiguration;

        public string Channel => DefaultChannel;

        public bool IsListening => connection != null && connection.State == ManagedSocketState.Listening;

        public bool IsResetReceived { get; private set; } = false;

        public int MaxConnectionAttempts { get; set; } = 1;

        public int ConnectTimeout { get; set; } = 5000;

        #region --- Con/Destructors -----------------------------------------------------------------------------------

        /// <summary>
        /// Creates a new SmartdoorHandler
        /// </summary>
        /// <param name="maxConnectionAttempts">Maximum number of attempts to connect to the SUT. Defaults
        /// to 1. Set to 0 or less for no limit.</param>
        public SmartdoorHandler(int maxConnectionAttempts = 1)
        {
            connection = new ManagedClientWebSocket();

            connection.OnTextReceived += (_, text) => HandleResponse(text);
            connection.OnClosed += (_, closeInfo) =>
            {
                var code = closeInfo.status.HasValue ? ((int)closeInfo.status.Value).ToString() : "1000";
                log.Info($"Disconnected from SUT: {connection.ConnectionUri}; code: {code} {closeInfo.reason}");
            };

            MaxConnectionAttempts = maxConnectionAttempts;
        }

        ~SmartdoorHandler()
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

        public async Task<bool> Start()
        {
            Contract.Requires(Configuration != null);

            try
            {
                // Check if connection is already in a non-initial state and close it
                if (connection.State != ManagedSocketState.NotConnected)
                {
                    log.Info("Closing existing connection to SUT before starting a new one");
                    connection.Close(message: "Restarting connection");

                    // Wait a moment for the connection to fully close
                    await Task.Delay(100);
                }

                connection.ConnectionUri = new Uri(Configuration!.GetString("url"));
                connection.MaxConnectionAttempts = MaxConnectionAttempts;
                connection.ConnectionTimeout = ConnectTimeout;
                connection.Name = "Handler connection";

                log.Info($"Trying to connect to SUT @{connection.ConnectionUri}");

                await connection!.Connect();

                // If the SUT is not up / available the AMP may close the connection to the adapter.
                // The adapter will close this handler. This is expected behavior so no
                // exception will be thrown and we have to check the state at this point. The adapter
                // has to check if everything has connected itself.
                if (connection.State == ManagedSocketState.Connected)
                {
                    await InitializeSutConnection();
                    return true;
                }
                else
                {
                    log.Error($"Couldn't start SmartdoorHandler as the connection couldn't be completed (connection state = {connection.State}).");
                    return false;
                }
            }
            catch (Exception e)
            {
                log.Error($"exception: {e}");
                throw;
            }
        }

        public async Task Reset()
        {
            IsResetReceived = false;

            if (connection.State == ManagedSocketState.Listening)
            {
                await InitializeSutConnection();
            }
            else
            {
                await Start();
            }
        }

        public void Stop()
        {
            connection.Close(message: "Stop testing.");
        }

        /// <summary>
        /// Stimulate the System Under Test with the stimulus.
        ///  Return the physical label.
        /// </summary>
        /// <param name="stimulus"></param>
        /// <returns></returns>
        public async Task Stimulate(string sutMessage)
        {
            log.Info($"Sending stimulus to SUT: {sutMessage}.");

            await connection!.SendText(sutMessage);
        }

        public async Task<ByteString> ToPhysicalLabel(string message)
        {
            Contract.RequiresNotNull(message);

            return await Task.Run(() => ByteString.CopyFromUtf8(message));
        }

    /// <summary>
    /// Creates a set of labels supported by the SUT
    /// </summary>
    /// <returns>A non empty set of labels used by the Smartdoor SUT.</returns>
    public static List<Label> CreateSupportedLabels()
        {
            // extra stimulus to reset the SUT
            List<Label> labels = [AxiniProtobuf.CreateStimulus(ResetLabel, DefaultChannel)];

            labels.AddRange(Stimuli.Select(s => AxiniProtobuf.CreateStimulus(s, DefaultChannel)));
            labels.AddRange(StimuliPasscode.Select(s =>
                AxiniProtobuf.CreateStimulus(s, DefaultChannel, [AxiniProtobuf.CreateParameter(PasscodeParameterName, 0)])));
            labels.AddRange(Responses.Select(s => AxiniProtobuf.CreateResponse(s, DefaultChannel)));

            return labels;
        }

        #endregion

        #region --- Private methods -----------------------------------------------------------------------------------

        private async Task InitializeSutConnection()
        {
            await connection.SendText(ResetText);

            log.Info($"Sent '{ResetText}' to SUT");

            if (connection.State == ManagedSocketState.Connected)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await connection!.Listen();
                    }
                    catch (Exception e)
                    {
                        OnError?.Invoke(this, e);
                    }
                });
            }
        }

        private void HandleResponse(string text)
        {
            log.Info($"received from SUT: {text}");

            // ignore the reset
            if (text != ResetPerformed)
            {
                lock (queueLock)
                {
                    responseQueue.Enqueue(text);
                    if (!isProcessingQueue)
                    {
                        isProcessingQueue = true;
                        _ = Task.Run(ProcessResponseQueue);
                    }
                }
            }
            else
            {
                IsResetReceived = true;
            }
        }

        private async Task ProcessResponseQueue()
        {
            while (true)
            {
                string response;
                lock (queueLock)
                {
                    if (responseQueue.Count == 0)
                    {
                        isProcessingQueue = false;
                        return;
                    }
                    response = responseQueue.Dequeue();
                }

                // Process the response in order
                OnResponse?.Invoke(this, response);

                // Small delay to allow other operations
                await Task.Delay(10);
            }
        }

        #endregion
    }
}
