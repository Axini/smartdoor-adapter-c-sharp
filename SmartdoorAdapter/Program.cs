
using log4net;
using log4net.Config;
using SmartdoorAdapter.Adapter;
using SmartdoorAdapter.Tests.IntegrationTests;
using System.Reflection;

// read the config from the full path
var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly()!);
var logConfigPath = Path.Combine(AppContext.BaseDirectory, "log4net.config");
XmlConfigurator.Configure(logRepository, new FileInfo(logConfigPath));

Console.WriteLine($"Smartdoor adapter 0.8.\n------------------------\n Logpath: {logConfigPath}.");

/// Based on https://github.com/Axini/smartdoor-adapter-java/tree/master
if (args.Length == 3)
{
    await StartApp(args[0], args[1], args[2]);
}
else
{
    var apiKey = Environment.GetEnvironmentVariable(EnvNames.ApiKey);
    var hostName = Environment.GetEnvironmentVariable(EnvNames.HostName);
    // provide a default name in case there is no env value set as this
    // is less of an issue if missing.
    var adapterName = Environment.GetEnvironmentVariable(EnvNames.AdapterName)
                        ?? ".net smartdoor adapter";

    if (apiKey != null && hostName != null)
    {
        await StartApp(adapterName, hostName, apiKey);
    }
    else
    {
        Console.WriteLine($"usage: SmartdoorAdapter <name> <url> <apikey>, or define the env variables {EnvNames.ApiKey} and {EnvNames.HostName}.");
    }
}

static async Task StartApp(string name, string uri, string apiKey)
{
    var log = LogManager.GetLogger("main");

    log.Info($"name: {name}, url: {uri}, api key: {apiKey}");

    using SmartdoorHandler handler = new(maxConnectionAttempts: 10);
    using BrokerConnection broker = new(new Uri(uri), apiKey: apiKey, maxConnectionAttempts: -1);

    var adapter = new AdapterCore(name, broker, handler);

    await adapter.Start(CancellationToken.None);
}
