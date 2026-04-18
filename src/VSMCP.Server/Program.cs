using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VSMCP.Server;

var config = VsmcpConfig.Load();
var builder = Host.CreateApplicationBuilder(args);

// MCP uses stdio; logs must not pollute stdout. Route logs to stderr only.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(ParseLogLevel(config.LogLevel));

if (config.FileLoggingEnabled)
{
    builder.Logging.AddProvider(new RollingFileLoggerProvider(
        config.ResolveLogDirectory(),
        ParseLogLevel(config.LogLevel)));
}

builder.Services.AddSingleton(config);
builder.Services.AddSingleton<VsConnection>();
builder.Services.AddSingleton<ProfilerHost>();
builder.Services.AddSingleton<CountersSubscriptionHost>();
builder.Services.AddSingleton<TraceHost>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

if (config.LoadError is not null)
    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("VSMCP.Server")
        .LogWarning("{err}", config.LoadError);

await app.RunAsync();

static LogLevel ParseLogLevel(string level) => level?.ToLowerInvariant() switch
{
    "trace" => LogLevel.Trace,
    "debug" => LogLevel.Debug,
    "info" or "information" => LogLevel.Information,
    "warn" or "warning" => LogLevel.Warning,
    "error" => LogLevel.Error,
    "critical" => LogLevel.Critical,
    "none" or "off" => LogLevel.None,
    _ => LogLevel.Warning,
};
