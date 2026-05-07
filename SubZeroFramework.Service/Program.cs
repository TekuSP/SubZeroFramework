using FrameworkDotnet;
using FrameworkDotnet.Interfaces;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Server.Kestrel.Core;

using SubZeroFramework.Service;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Service.Services;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var socketPath = SubZeroFramework.Service.Services.FrameworkGrpcSocketPath.GetPath();

        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "SubZeroFrameworkService";
        });
        builder.Services.AddSystemd();

        builder.WebHost.ConfigureKestrel(serverOptions =>
        {
            if (File.Exists(socketPath))
            {
                File.Delete(socketPath);
            }

            serverOptions.ListenUnixSocket(socketPath, listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http2;
            });
        });

        builder.Services
            .AddOptions<FrameworkServiceOptions>()
            .Bind(builder.Configuration.GetSection("FrameworkService"));

        builder.Services.AddGrpc();
        builder.Services.AddSingleton<IFrameworkSystem, FrameworkSystem>();
        builder.Services.AddSingleton<IFrameworkDataProvider, FrameworkDataProvider>();
        builder.Services.AddHostedService<FrameworkTelemetryWorker>();

        var app = builder.Build();
        app.MapGrpcService<FrameworkStatusGrpcService>();
        await app.RunAsync().ConfigureAwait(false);
    }
}
