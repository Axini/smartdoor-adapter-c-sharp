using Google.Protobuf;
using SmartdoorAdapter.Proto;

namespace SmartdoorAdapter.Adapter
{
    /// <summary>
    /// The Handler component implements a common interface called by the AdapterCore. These
    /// callbacks handle the domain specific aspects of connecting, stimulating and observing the
    /// system under test.
    /// </summary>

    // Note: even though there is only one implementation, an interface is necessary for testing/mocking.
    public interface IHandler
    {
        event EventHandler<string>? OnResponse;
        event EventHandler<Exception>? OnError;

        /// <summary>
        /// Determines how often the handler tries to connect to the SUT before giving up.
        /// When set 0 or less, there is no limit. The default should be 1.
        /// </summary>
        int MaxConnectionAttempts
        {
            get;
            set;
        }

        /// <summary>
        /// The current channel used to communicate with the SUT
        /// </summary>
        string Channel
        {
            get;
        }

        /// <summary>
        /// The labels supported by the plugin adapter.
        /// </summary>
        /// <returns></returns>
        List<Label> SupportedLabels
        {
            get;
        }

        /// <summary>
        /// Current configuration set in the handler. This should
        /// by default return a configuration.
        /// </summary>
        Configuration? Configuration
        {
            get;
            set;
        }

        /// <summary>
        /// Prepare to start testing.
        /// </summary>
        Task<bool> Start();

        /// <summary>
        /// Stop testing.
        /// </summary>
        void Stop();

        /// <summary>
        /// Prepare for the next test case.
        /// </summary>
        Task Reset();

        /// <summary>
        /// Stimulate the System Under Test and return the physical label.
        /// </summary>
        /// <param name="stimulus"></param>
        /// <returns></returns>
        Task Stimulate(string stimulus);

        /// <summary>
        /// Translate the given message into a physical label
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        Task<ByteString> ToPhysicalLabel(string message);
    }
}
