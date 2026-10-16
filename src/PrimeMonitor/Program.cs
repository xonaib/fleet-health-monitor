using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrimeMonitor.Monitor;
using PrimeMonitor.Shared;
using PrimeMonitor.Worker;

const string Usage = "Usage: PrimeMonitor monitor | PrimeMonitor worker --id <machineId>";

var mode = args.FirstOrDefault();

// Content root is the output folder so appsettings.json is found whatever directory `dotnet run` is started from.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Skip(1).ToArray(),
    ContentRootPath = AppContext.BaseDirectory,
});
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss.fff ";
});
builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection("Monitor"));

switch (mode)
{
    case "monitor":
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<MachineRegistry>();
        builder.Services.AddSingleton<Alerts>();
        // FullMode.Wait so TryWrite returns false when full. DropWrite would also return true while silently
        // discarding the item, and an unchecked prime must be reported, not lost.
        builder.Services.AddSingleton(sp => Channel.CreateBounded<PrimeMessage>(
            new BoundedChannelOptions(sp.GetRequiredService<IOptions<MonitorOptions>>().Value.QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
            }));
        builder.Services.AddSingleton<ConnectionHandler>();
        builder.Services.AddHostedService<VerifierPool>();
        builder.Services.AddHostedService<SilenceWatchdog>();
        builder.Services.AddHostedService<StatusReporter>();
        builder.Services.AddHostedService<MonitorRunner>();
        break;
    case "worker":
        var machineId = builder.Configuration["id"];
        if (string.IsNullOrWhiteSpace(machineId))
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }
        builder.Services.AddSingleton(new WorkerSettings(machineId));
        builder.Services.AddHostedService<WorkerRunner>();
        break;
    default:
        Console.Error.WriteLine(Usage);
        return 1;
}

await builder.Build().RunAsync();
return 0;
