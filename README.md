# claude-mcp-flaky-test-agent

An AI agent that hunts down flaky tests: a hand-coded agentic loop calling tools over the Model Context Protocol, built in C#/.NET.

![Anthropic SDK](https://img.shields.io/badge/Anthropic_SDK-D97757?style=for-the-badge&logo=anthropic&logoColor=white)
![MCP](https://img.shields.io/badge/Model_Context_Protocol-000000?style=for-the-badge&logo=modelcontextprotocol&logoColor=white)
![Ollama](https://img.shields.io/badge/Ollama-000000?style=for-the-badge&logo=ollama&logoColor=white)
![Qwen](https://img.shields.io/badge/Qwen-615CED?style=for-the-badge&logo=qwen&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)

- **McpServer**: an MCP server (tool producer) exposing test-running, code-reading, git/PR and notification tools
- **Agent**: a hand-written agentic loop on the Anthropic C# SDK that consumes those tools as an MCP client
- **sandbox/FlakyLab**: NUnit tests with deliberately flaky behaviour for the agent to diagnose

See [ROADMAP.md](ROADMAP.md) for the session plan and current status.
