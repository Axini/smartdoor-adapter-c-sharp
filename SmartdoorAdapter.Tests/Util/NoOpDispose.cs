namespace SmartdoorAdapter.Tests.Util
{
    public class NoOpDispose : IDisposable
    {
        public static readonly NoOpDispose Instance = new();

        private NoOpDispose() { }

        public void Dispose() { }
    }
}