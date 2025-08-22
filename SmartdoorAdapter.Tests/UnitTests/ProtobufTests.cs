using Google.Protobuf;

using SmartdoorAdapter.Adapter;
using SmartdoorAdapter.Proto;

namespace SmartdoorAdapter.Tests.UnitTests
{
    [TestClass]
    public class ProtobufTests()
    {
        [TestMethod]
        public void ConfigurationMappingTest()
        {
            var ampConfig = AxiniProtobuf.CreateConfiguration([
                                        AxiniProtobuf.CreateItem("key", "WebSocket URL of AMP", "ws://localhost:3001")]);

            var buffer = new ArraySegment<byte>(ampConfig.ToByteArray());

            var parsedConfig = Configuration.Parser.ParseFrom(buffer.Array, buffer.Offset, buffer.Count);

            Assert.IsNotNull(parsedConfig);
            Assert.IsTrue(parsedConfig!.Items != null);
            Assert.IsTrue(parsedConfig!.Items!.Count == ampConfig.Items.Count);
            Assert.IsTrue(parsedConfig!.Items[0].Description == ampConfig.Items[0].Description);
            Assert.IsTrue(parsedConfig!.Items[0].String == ampConfig.Items[0].String);
            Assert.IsTrue(parsedConfig!.Items[0].Key == ampConfig.Items[0].Key);
        }

        [TestMethod]
        public void MessageConfigurationMappingTest()
        {
            var ampConfig = AxiniProtobuf.CreateConfiguration([
                                        AxiniProtobuf.CreateItem("key", "WebSocket URL of AMP", "ws://localhost:3001")]);

            var message = AxiniProtobuf.CreateMsgConfiguration(ampConfig);

            var buffer = new ArraySegment<byte>(message.ToByteArray());

            var parsedConfig = Message.Parser.ParseFrom(buffer.Array, buffer.Offset, buffer.Count);

            Assert.IsNotNull(parsedConfig);
            Assert.IsNotNull(parsedConfig.Configuration);
            Assert.IsNotNull(parsedConfig.Configuration.Items);
            Assert.IsTrue(parsedConfig.Configuration.Items != null);
            Assert.IsTrue(parsedConfig.Configuration.Items.Count == ampConfig.Items.Count);
            Assert.IsTrue(parsedConfig.Configuration.Items[0].Description == ampConfig.Items[0].Description);
            Assert.IsTrue(parsedConfig.Configuration.Items[0].String == ampConfig.Items[0].String);
            Assert.IsTrue(parsedConfig.Configuration.Items[0].Key == ampConfig.Items[0].Key);
        }


        /// <summary>
        /// The timestamp between .net and AMP doesn't seem to match using
        /// timeStamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        /// The timestamp the AMP is using is actually nano secs.
        /// TODO: Update this test
        /// </summary>
        [TestMethod]
        public void MessageResponseLabelTesT()
        {
            var labelText = "foo";

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var fooLabel = new Label()
            {
                Label_ = labelText
            };
            var physicalLabel = ByteString.CopyFromUtf8(labelText);

            var label = AxiniProtobuf.CreateLabel(fooLabel, physicalLabel, (ulong)timestamp);

            Assert.IsTrue(label.Timestamp == (ulong)timestamp);
        }
    }
}
