using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("Hello, World from FlakyDetective.McpServer!");

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(configure => configure.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

await builder.Build().RunAsync();

