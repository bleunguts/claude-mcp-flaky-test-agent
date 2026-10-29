using FlakyDetective.McpServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

Console.WriteLine("Hello, World from FlakyDetective.McpServer!");

if (args.Contains("--try"))
{
    var results = await TestRunner.RunBatch(5, null); 
    
    Console.WriteLine($"Requested: {results.Requested}, Completed: {results.Completed}, Error: {results.Error}");
    results.TestSummaries.ForEach(summary =>
    {
        Console.WriteLine($"Test: {summary.TestName}, Runs: {summary.Runs}, Passed: {summary.Passed}, Failed: {summary.Failed}, FailureRate: {summary.FailureRate}");
    });        
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(configure => configure.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

await builder.Build().RunAsync();


