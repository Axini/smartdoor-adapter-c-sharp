using Google.Protobuf;

using gg.core.util;

using SmartdoorAdapter.Proto;

using static SmartdoorAdapter.Proto.Label.Types;
using static SmartdoorAdapter.Proto.Configuration.Types;

namespace SmartdoorAdapter.Adapter
{
    /// <summary>
    /// Utility class to create protobuf messages.
    /// </summary>
    public static class AxiniProtobuf
    {
        /// <summary>
        /// Create a message signalling an error
        /// </summary>
        /// <param name="message">A non empty string</param>
        /// <returns></returns>
        public static Message CreateMsgError(string message)
        {
            Contract.RequiresNotNullOrEmpty(message);

            return new Message
            {
                Error = new Message.Types.Error
                {
                    Message = message
                }
            };
        }

        /// <summary>
        /// Creates a message to announce a broker with AMP
        /// (ie register with service).
        /// </summary>
        /// <param name="name">Non empty name used as a display name on AMP.</param>
        /// <param name="supportedLabels">List of labels supported by the handler.</param>
        /// <param name="configuration">Current configuration of the handler</param>
        /// <returns></returns>
        public static Message CreateMsgAnnouncement(string name,
            List<Label> supportedLabels, Configuration configuration)
        {
            Contract.RequiresNotNullOrEmpty(name);
            Contract.RequiresNotNullOrEmpty(supportedLabels);
            Contract.RequiresNotNull(configuration);

            var announcement = new Announcement()
            {
                Name = name,
                Configuration = configuration,
            };

            announcement.Labels.AddRange(supportedLabels);

            return new Message()
            {
                Announcement = announcement
            };
        }

        /// <summary>
        /// Creates an int parameter
        /// </summary>
        /// <param name="name">Non empty parameter name.</param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static Parameter CreateParameter(string name, int value)
        {
            Contract.RequiresNotNullOrEmpty(name);

            return new Parameter()
            {
                Name = name,
                Value = new Parameter.Types.Value()
                {
                    Integer = value
                }
            };
        }

        /// <summary>
        /// Create a stimulus for the SUT
        /// </summary>
        /// <param name="name">Non empty string of the label</param>
        /// <param name="channel">Non empty string of the channel</param>
        /// <returns></returns>
        public static Label CreateStimulus(string name, string channel)
        {
            Contract.RequiresNotNullOrEmpty(name);
            Contract.RequiresNotNullOrEmpty(channel);

            return new Label()
            {
                Type = LabelType.Stimulus,
                Label_ = name,
                Channel = channel,
            };
        }

        /// <summary>
        /// Create a stimulus
        /// </summary>
        /// <param name="name">Non empty name</param>
        /// <param name="channel">Non empty channel</param>
        /// <param name="labelParameters"></param>
        /// <returns></returns>
        public static Label CreateStimulus(string name, string channel, List<Parameter> labelParameters)
        {
            var result = CreateStimulus(name, channel);

            result.Parameters.Add(labelParameters);

            return result;
        }

        /// <summary>
        /// Create a response Label with *no* parameters. 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="channel"></param>
        /// <returns></returns>
        public static Label CreateResponse(string name, string channel)
        {
            return new Label()
            {
                Label_ = name,
                Channel = channel,
                Type = LabelType.Response
            };
        }

        /// <summary>
        /// Create a Label with the given properties based on a clone of the label 
        /// parameter.
        /// </summary>
        /// <param name="label">Non-null label which will be cloned</param>
        /// <param name="physicalLabel">Non null physical label</param>
        /// <param name="timestamp">Timestamp of creation</param>
        /// <param name="correlationId">An id to correlate further communication</param>
        /// <returns></returns>
        public static Label CreateLabel(Label label, ByteString physicalLabel,
                                   ulong timestamp, ulong correlationId)
        {
            Contract.RequiresNotNull(label);
            Contract.RequiresNotNull(physicalLabel);

            var result = CreateLabel(label, physicalLabel, timestamp);

            result.CorrelationId = correlationId;

            return result;
        }

        /// <summary>
        /// Create a message with type label
        /// </summary>
        /// <param name="label">Non-null label which will be used as message type</param>
        public static Message CreateMsgLabel(Label label)
        {
            Contract.RequiresNotNull(label);

            return new Message()
            {
                Label = label
            };
        }

        /// <summary>
        /// Create a clone of the given protobuf Label and sets its physicalLabel and timestamp. 
        /// </summary>
        /// <param name="label"></param>
        /// <param name="physicalLabel"></param>
        /// <param name="timestamp"></param>
        /// <returns></returns>
        public static Label CreateLabel(Label label, ByteString physicalLabel,
                                        ulong timestamp)
        {
            Contract.RequiresNotNull(label);
            Contract.RequiresNotNull(physicalLabel);

            var result = label.Clone();

            result.Timestamp = timestamp;
            result.PhysicalLabel = physicalLabel;

            return result;
        }

        /// <summary>
        /// Create a configuration with the given item
        /// </summary>
        /// <param name="items">Non empty list of configuration items</param>
        /// <returns></returns>
        public static Configuration CreateConfiguration(List<Configuration.Types.Item> items)
        {
            Contract.RequiresNotNullOrEmpty(items);

            var config = new Configuration();

            config.Items.AddRange(items);

            return config;
        }

        /// <summary>
        /// Create a (key,value) configuration item
        /// </summary>
        /// <param name="key">Non empty key to identify the value</param>
        /// <param name="description">A description of the key value pair</param>
        /// <param name="value">Value associated with the key</param>
        /// <returns></returns>
        public static Item CreateItem(string key, string description,
                                                string value)
        {
            Contract.RequiresNotNullOrEmpty(key);
            Contract.RequiresNotNull(description, "Description cannot be null");
            Contract.RequiresNotNull(value, "Value cannot be null");

            return new Item()
            {
                Key = key,
                Description = description,
                String = value
            };
        }

        /// <summary>
        /// Create a message indicating the adapter is ready for testing.
        /// </summary>
        /// <returns></returns>
        public static Message CreateMsgReady()
        {
            return new Message()
            {
                Ready = new Message.Types.Ready()
            };
        }

        /// <summary>
        /// Create a message containing a configuration
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static Message CreateMsgConfiguration(Configuration config)
        {
            Contract.RequiresNotNull(config);

            return new Message()
            {
                Configuration = config
            };
        }

        /// <summary>
        /// Returns the value associated with the given key in the config
        /// </summary>
        /// <param name="config"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static string GetString(this Configuration config, string key)
        {
            Contract.RequiresNotNullOrEmpty(key);

            return config.Items.First(item => item.Key == key && item.HasString).String;
        }

        /// <summary>
        /// Map the label to a SUT msssage string
        /// </summary>
        /// <param name="label"></param>
        /// <returns></returns>
        public static string ToSutMessage(this Label label)
        {
            var name = label.Label_;
            var sutLabel = name != null ? name.ToUpper() : "NULL label";

            return name switch
            {
                "lock" or "unlock" => sutLabel + ":" + label.Parameters[0].Value.Integer,
                _ => sutLabel,// This allows to send bad weather stimuli to the SUT.
            };
        }
    }
}
