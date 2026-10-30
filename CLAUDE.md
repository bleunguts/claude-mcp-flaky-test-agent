# FlakyLab — MCP + Agentic Flaky Test Detective

## What this is
A C#/.NET agent that hunts down flaky tests. It's built from two parts:
> an MCP server (the tool **producer**) and a hand-coded agentic loop that consumes it as an MCP client (the tool **orchestrator**). Together they investigate flaky tests, propose and verify fixes, and raise PRs.

The aim is to understand agentic architecture from the inside, not just drive AI tools. Favour clear, explicit code whose design I can walk someone through in person over clever abstractions.

## How to work with me
- I'm learning as I build. Work in **bite-sized sessions**: explain the concept briefly first, then build. One topic per session.
- Let me write or approve the key code, especially the agentic loop. Don't silently generate large chunks.
- Follow the shared workflow in `~/.claude/CLAUDE.md` (issue -> `feature/<n>-name` branch -> PR with `Closes #N`). Each PR updates the status table in ROADMAP.md.
- At the end of each session, update the Session log below (one or two lines) and suggest a commit message.

## Architecture
```
src/FlakyDetective.Agent      -> tool ORCHESTRATOR: Anthropic C# SDK (Messages API) + MCP client, hand-written loop
src/FlakyDetective.McpServer  -> tool PRODUCER: ModelContextProtocol C# SDK, stdio transport, [McpServerTool] handlers
sandbox/FlakyLab              -> NUnit project with deliberately flaky tests: the "crime scene"
```
Planned MCP tools: `get_flaky_candidates` (TeamCity REST), `run_test_n_times`, `read_source`, `git_branch_commit_pr`, `notify`.

## Hard rules
- **Hand-code the loop.** Handle `tool_use` → MCP `CallToolAsync` → `tool_result` → repeat until `end_turn`. A real model adapter uses `client.Messages.Create`. Do NOT use `IChatClient` + `UseFunctionInvocation()`, because that automates the exact thing I need to demonstrate. The loop runs behind the `IModel` interface: a scripted stub (default) or a local Ollama model (`--ollama`); the message types mirror the Anthropic Messages API, and a Claude API adapter can be added later.
- The MCP server contains **no LLM calls**. It's deterministic C#.
- The server uses stdio, so **log to stderr only**. stdout is the protocol channel.
- **Do not diagnose or fix the sandbox tests yourself, and don't add comments explaining why they flake.** Finding the causes is the agent's job, and I score it against answers kept outside this repo.
- Guardrails: the agent never pushes to main, never edits files outside `sandbox/`, and caps loop iterations.

## Packages
- `Anthropic`: official C# SDK, v10+ (not the old tryAGI package)
- `ModelContextProtocol` (+ `Microsoft.Extensions.Hosting` for the server)
- NUnit for the sandbox

## API access
Claude Code runs on my Pro login. I am not paying for the API, so the Agent project uses a free local model through Ollama (`OLLAMA_MODEL`, default `qwen2.5:3b`). If a Claude API adapter is ever added it needs a Console API key with credits in the `ANTHROPIC_API_KEY` env var, never in the repo.

## Session plan
The session plan, status and open design questions live in [ROADMAP.md](ROADMAP.md), which is the single source of truth. Update its status line and table at the end of each session.

## Session log
- Session 1 (complete): architecture agreed; FlakyLabTests.cs written (6 tests, including 1 stable control); sandbox switched from xUnit to NUnit; FlakyDetective.slnx created with sandbox/FlakyLab; 10 consecutive runs gave 1 to 4 failures out of 6, so the sandbox is confirmed flaky.
- Session 2 (complete): `FlakyDetective.McpServer` (stdio, stderr logging) with `ping` and `run_test_n_times` (sequential `dotnet test` runs, `.trx` parsing, per-run rows plus per-test summaries). Verified in MCP Inspector, including empty-filter handling. The sandbox must be built beforehand (`--no-build`).
- Session 3 (complete): `read_source` and `find_test_source` tools behind `PathGuard` (root = solution folder computed server-side, deny list for `.git`, `.claude`, `.env*`, `GROUND_TRUTH*`, build output; text/source extensions only). Verified in MCP Inspector. `find_test_source` is text-based, not Roslyn.
- Session 4 (complete): `FlakyDetective.Agent` is an MCP client (stdio, launches the built server dll) with a hand-written loop behind `IModel`, an iteration cap and step logging. Runs on a scripted stub model, so no API credits. Verified with six scripts: normal run, runaway (stops at the cap), unknown tool, denied file, path escape, bad argument.
- Ollama adapter (complete): `OllamaModel : IModel` talks to a local Ollama server (`/api/chat`, tools, `num_ctx` 8192), selected with `--ollama`. Stop reason comes from the presence of tool calls, not `done_reason`. With `qwen2.5:3b` the loop ran end to end and the model chained `find_test_source` then `read_source` itself. Errors just end the program. Next: Session 5 (investigation prompt and diagnoses, scored against the private ground truth).
