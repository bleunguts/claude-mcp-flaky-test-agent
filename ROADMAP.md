# Roadmap

A step-by-step build of an MCP server and a hand-written agentic loop that investigates flaky tests, proposes and verifies fixes, and raises pull requests. The project doubles as a guided tour of agentic architecture: the tool producer (MCP server), the tool orchestrator (the loop), and the guardrails around them are each built in the open, one small session at a time.

**Status:** Session 1 complete, Session 2 next.

## Sessions

| # | Session | API credits? | Status |
|---|---|---|---|
| 1 | Architecture + FlakyLab sandbox | No | Done |
| 2 | MCP server scaffold + `run_test_n_times` tool (`dotnet test --filter`, parse .trx), tested in MCP Inspector | No | Planned |
| 3 | Code analyzer tools (`read_source`, plus a way to find the implementation for a test) | No | Planned |
| 4 | Hand-coded agentic loop: Anthropic C# SDK + MCP client | Yes | Planned |
| 5 | Investigation prompt + first diagnoses, scored against ground truth | Yes | Planned |
| 6 | TeamCity in Docker + `get_flaky_candidates` tool | No | Planned |
| 7 | Fix → verify 10x → git branch/PR tools | Yes | Planned |
| 8 | Notifier, guardrails, audit log, final scorecard | Light | Planned |

Sessions 1–3 and 6 need no API credits. Sessions 4, 5 and 7 call the Claude API directly and need a Console API key with credits, supplied through the `ANTHROPIC_API_KEY` environment variable and never committed.

## How it is built

Sessions are small and cover one topic each. Every change follows the same path: GitHub issue, branch, implementation, pull request. Each session's PR updates the status line and table above.

## Architecture

```
┌──────────────────────────── FlakyDetective.slnx ───────────────────────────┐
│                                                                            │
│  src/FlakyDetective.Agent  (tool ORCHESTRATOR)                             │
│   ├─ AnthropicClient ── Messages API (hand-written loop)                   │
│   └─ McpClient ──stdio──┐                                                  │
│                         ▼                                                  │
│  src/FlakyDetective.McpServer  (tool PRODUCER)                             │
│   [McpServerTool] get_flaky_candidates · run_test_n_times ·                │
│   read_source · git_branch_commit_pr · notify                              │
│        │                 │                                                 │
│        ▼                 ▼                                                 │
│   TeamCity REST     sandbox/FlakyLab (deliberately flaky NUnit tests)      │
└────────────────────────────────────────────────────────────────────────────┘
```

### Key design points

1. **The loop is the only place Claude reasons.** The MCP server is plain deterministic C# with no LLM calls. The agent holds the conversation and sends Claude the tool list. On each `tool_use` it forwards the call to the server with `CallToolAsync`, returns a `tool_result`, and loops until `stop_reason` is `end_turn`.

2. **No shortcut loop.** The official `Anthropic` C# SDK implements `IChatClient`, so MCP tools can be passed straight in, and `.UseFunctionInvocation()` from Microsoft.Extensions.AI would run the loop automatically. That is fine in production, but the point here is to write the loop. The agent uses the raw `client.Messages.Create` and maps `ListToolsAsync()` results to Anthropic tool definitions by hand. Owning the loop means owning the stop conditions and guardrails.

3. **Terminology.** The Claude Agent SDK ships for Python and TypeScript, not C#. In .NET, "agent loop" here means the Anthropic C# SDK plus a hand-written loop.

4. **stdio transport: log to stderr only.** stdout is the MCP protocol channel, so any stray `Console.WriteLine` in the server corrupts it.

### Guardrails

Enforced in the agent and finalised in Session 8:

- Never push to `main`.
- Never edit files outside `sandbox/`.
- Cap loop iterations.

### Packages and toolchain

- .NET SDK 10 (the sandbox targets `net10.0`).
- `Anthropic`: the official C# SDK, v10+.
- `ModelContextProtocol`, plus `Microsoft.Extensions.Hosting` for the server.
- NUnit 4, NUnit3TestAdapter 5 and Microsoft.NET.Test.Sdk 17 for the sandbox.

## Session 1: the sandbox

The agent needs a test bed where the answers are already known. `sandbox/FlakyLab/FlakyLabTests.cs` contains several flaky tests, each with a different root cause, plus a stable control test that the agent must leave alone. The root causes are recorded only in a ground-truth file kept outside the repository and used for scoring, so the tests carry no explanatory comments.

The solution file and the sandbox project are in place. On .NET 10, `dotnet new sln` produces a `.slnx` file by default.

```bash
dotnet new sln -n FlakyDetective
dotnet sln add sandbox/FlakyLab
for i in {1..10}; do dotnet test sandbox/FlakyLab --nologo -v q | tail -1; done
```

**Done when:** the 10 runs show different pass/fail counts. Confirmed: ten consecutive runs gave between 1 and 4 failures out of 6 tests.

## Session 2 design question

One of the flaky tests fails only about 10% of the time. With 5 reruns, the chance that all 5 pass is 0.9⁵ ≈ 59%, so a naive "rerun 5 times" check misses it more often than not.

What should `run_test_n_times` return so Claude can reason about that uncertainty, rather than just receiving a pass count? Consider per-run outcomes, durations, failure messages, and whether Claude can request more runs.
