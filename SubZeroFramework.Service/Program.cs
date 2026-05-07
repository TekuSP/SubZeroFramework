using FrameworkDotnet;
using FrameworkDotnet.Interfaces;

using SubZeroFramework.Service;
using SubZeroFramework.Service.Models;
using SubZeroFramework.Services;

namespace SubZeroFramework.Service;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = "SubZeroFrameworkService";
        });
        builder.Services.AddSystemd();

        builder.Services
            .AddOptions<FrameworkServiceOptions>()
            .Bind(builder.Configuration.GetSection("FrameworkService"));

        builder.Services.AddSingleton<IFrameworkSystem, FrameworkSystem>();
        builder.Services.AddSingleton<IFrameworkDataProvider, FrameworkDataProvider>();
        builder.Services.AddHostedService<FrameworkTelemetryWorker>();

        var host = builder.Build();
        await host.RunAsync().ConfigureAwait(false);
    }
}
