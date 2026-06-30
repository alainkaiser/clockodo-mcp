using Clockodo.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton(ClockodoOptions.FromEnvironment());
builder.Services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
})
{
    Timeout = TimeSpan.FromSeconds(100)
});
builder.Services.AddSingleton<ClockodoClient>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ClockodoTools>();

await builder.Build().RunAsync();
