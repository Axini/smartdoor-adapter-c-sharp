using log4net;

namespace SmartdoorAdapter.Tests.Util
{
    public class Log4NetLoggerFactory : ILoggerFactory
    {
        private readonly ILog _log;

        public Log4NetLoggerFactory()
        {
            _log = LogManager.GetLogger(typeof(Log4NetLoggerFactory));
            log4net.Config.XmlConfigurator.Configure(new FileInfo("log4net.config"));
        }

        public void AddProvider(ILoggerProvider provider)
        {
            // Add provider if needed
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new Log4NetLogger(_log);
        }

        public void Dispose()
        {
            // Dispose if needed
        }
    }

    public class Log4NetLogger(ILog log) : ILogger
    {
        private readonly ILog _log = log;

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NoOpDispose.Instance;

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var message = formatter(state, exception);

            switch (logLevel)
            {
                case LogLevel.Critical:
                case LogLevel.Error:
                    _log.Error(message);
                    break;
                case LogLevel.Warning:
                    _log.Warn(message);
                    break;
                case LogLevel.Information:
                    _log.Info(message);
                    break;
                case LogLevel.Debug:
                case LogLevel.Trace:
                    _log.Debug(message);
                    break;
                default:
                    _log.Debug(message);
                    break;
            }
        }
    }
}