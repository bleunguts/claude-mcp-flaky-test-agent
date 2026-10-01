# claude-mcp-flaky-test-agent

An agent that investigates flaky tests, built in C#/.NET.

- **McpServer**: an MCP server (tool producer) exposing test-running, code-reading, git/PR and notification tools
- **Agent**: a hand-written agentic loop on the Anthropic C# SDK that consumes those tools as an MCP client
- **sandbox/FlakyLab**: nUnit tests with deliberately flaky behaviour for the agent to diagnose

Work in progress.
