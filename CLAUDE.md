# FlakyLab — MCP + Agentic Flaky Test Detective

## What this is
A C#/.NET agent that hunts down flaky tests. It's built from two parts:
> an MCP server (the tool **producer**) and a hand-coded agentic loop that consumes it as an MCP client (the tool **orchestrator**). Together they investigate flaky tests, propose and verify fixes, and raise PRs.

The aim is to understand agentic architecture from the inside, not just drive AI tools. Favour clear, explicit code whose design I can walk someone through in person over clever abstractions.

## How to work with me
- I'm learning as I build. Work in **bite-sized sessions**: explain the concept briefly first, then build. One topic per session.
- Let me write or approve the key code, especially the agentic loop. Don't silently generate large chunks.
- At the end of each session, update the Session log below (one or two lines) and suggest a commit message.

## Architecture
```
src/FlakyDetective.Agent      -> tool ORCHESTRATOR: Anthropic C# SDK (Messages API) + MCP client, hand-written loop
src/FlakyDetective.McpServer  -> tool PRODUCER: ModelContextProtocol C# SDK, stdio transport, [McpServerTool] handlers
sandbox/FlakyLab              -> NUnit project with deliberately flaky tests: the "crime scene"
```
Planned MCP tools: `get_flaky_candidates` (TeamCity REST), `run_test_n_times`, `read_source`, `git_branch_commit_pr`, `notify`.

## Hard rules
- **Hand-code the loop.** Use `client.Messages.Create` and handle `tool_use` → MCP `CallToolAsync` → `tool_result` → repeat until `end_turn`. Do NOT use `IChatClient` + `UseFunctionInvocation()`, because that automates the exact thing I need to demonstrate.
- The MCP server contains **no LLM calls**. It's deterministic C#.
- The server uses stdio, so **log to stderr only**. stdout is the protocol channel.
- **Do not diagnose or fix the sandbox tests yourself, and don't add comments explaining why they flake.** Finding the causes is the agent's job, and I score it against answers kept outside this repo.
- Guardrails: the agent never pushes to main, never edits files outside `sandbox/`, and caps loop iterations.

## Packages
- `Anthropic`: official C# SDK, v10+ (not the old tryAGI package)
- `ModelContextProtocol` (+ `Microsoft.Extensions.Hosting` for the server)
- NUnit for the sandbox

## API access
Claude Code runs on my Pro login. The Agent project (Session 4+) calls the Claude API directly and needs a Console API key with credits. The key goes in the `ANTHROPIC_API_KEY` env var, never in the repo.

## Session plan
The session plan, status and open design questions live in [ROADMAP.md](ROADMAP.md), which is the single source of truth. Update its status line and table at the end of each session.

## Session log
- Session 1 (complete): architecture agreed; FlakyLabTests.cs written (6 tests, including 1 stable control); sandbox switched from xUnit to NUnit; FlakyDetective.slnx created with sandbox/FlakyLab; 10 consecutive runs gave 1 to 4 failures out of 6, so the sandbox is confirmed flaky. Next: Session 2 (MCP server scaffold + `run_test_n_times`).
