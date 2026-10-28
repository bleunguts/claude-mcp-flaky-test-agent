using FlakyDetective.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("Hello, World from FlakyDetective.McpServer!");

if (args.Contains("--try"))
{
    var folder = TestRunner.FindSlnFolderRecursively(AppContext.BaseDirectory);
    Console.WriteLine($"Found sln folder: {folder}");
    await TestRunner.RunTestsAsync(folder, testFilter: null);
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(configure => configure.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

await builder.Build().RunAsync();


