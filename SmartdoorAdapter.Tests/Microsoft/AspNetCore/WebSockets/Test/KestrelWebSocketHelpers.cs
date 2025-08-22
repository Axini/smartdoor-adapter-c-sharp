// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebSockets;
using System.Net;
using System.Runtime.ExceptionServices;

namespace SmartdoorAdapter.Tests.Microsoft.AspNetCore.WebSockets.Test;

public class KestrelWebSocketHelpers
{
    /// <summary>
    /// Create a server for testing. Based on: 
    /// https://github.com/dotnet/aspnetcore/blob/397ef0a70f9b20a7d00d9248b420e119c95d2064/src/Middleware/WebSockets/test/UnitTests/KestrelWebSocketHelpers.cs
    /// </summary>
    /// <param name="loggerFactory"></param>
    /// <param name="port"></param>
    /// <param name="app"></param>
    /// <param name="configure"></param>
    /// <returns></returns>
    public static IAsyncDisposable CreateServer(ILoggerFactory loggerFactory, out int port, Func<HttpContext, Task> app, Action<WebSocketOptions>? configure = null)
    {
        ILogger log = loggerFactory.CreateLogger(typeof(KestrelWebSocketHelpers));

        Exception? exceptionFromApp = null;
        configure ??= (o => { });


        var configBuilder = new ConfigurationBuilder();
        configBuilder.AddInMemoryCollection();
        var config = configBuilder.Build();

        var host = new HostBuilder()
            .ConfigureWebHost(webHostBuilder =>
            {
                webHostBuilder
                .ConfigureServices(s =>
                {
                    s.AddWebSockets(configure);
                    s.AddSingleton(loggerFactory);
                })
                .UseConfiguration(config)
                .UseKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 0);
                })
                .Configure(StartupTestApp);
            }).ConfigureHostOptions(o =>
            {
                o.ShutdownTimeout = TimeSpan.FromSeconds(30);
            }).Build();

        host.Start();

        var server = host.Services!.GetService<IServer>();
        var addressFeature = server!.Features.Get<IServerAddressesFeature>();
        var uri = new Uri(addressFeature!.Addresses.First());

        port = uri.Port;

        return new Disposable(async () =>
        {
            await host.StopAsync();
            host.Dispose();
            if (exceptionFromApp is not null)
            {
                ExceptionDispatchInfo.Throw(exceptionFromApp);
            }
        });

        void StartupTestApp(IApplicationBuilder builder)
        {
            builder.Use(async (ct, next) =>
            {
                try
                {
                    // Kestrel does not return proper error responses:
                    // https://github.com/aspnet/KestrelHttpServer/issues/43
                    await next(ct);
                }
                catch (Exception ex)
                {
                    // capture the exception from the app, we'll throw this at the end of the test when the server is disposed
                    exceptionFromApp = ex;
                    if (ct.Response.HasStarted)
                    {
                        throw;
                    }

                    ct.Response.StatusCode = 500;
                    ct.Response.Headers.Clear();
                    await ct.Response.WriteAsync(ex.ToString());
                }
            });
            builder.UseWebSockets();
            builder.Run(c => app(c));
        }
    }

    private class Disposable(Func<ValueTask> dispose) : IAsyncDisposable
    {
        private readonly Func<ValueTask> _dispose = dispose;

        public ValueTask DisposeAsync()
        {
            return _dispose();
        }
    }
}
