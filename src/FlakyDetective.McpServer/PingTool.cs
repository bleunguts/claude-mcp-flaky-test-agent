using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace FlakyDetective.McpServer;

[McpServerToolType]
[Description("A tool that pings the server to check if it is alive.")]
public class PingTool
{
    [McpServerTool]
    [Description("Pings the server with a message and returns a pong response.")]
    public static string Ping()
    {
        return "pong";
    }
}
