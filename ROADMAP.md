# Roadmap

A step-by-step build of an MCP server and a hand-written agentic loop that investigates flaky tests, proposes and verifies fixes, and raises pull requests. The project doubles as a guided tour of agentic architecture: the tool producer (MCP server), the tool orchestrator (the loop), and the guardrails around them are each built in the open, one small session at a time.

**Status:** Sessions 1 to 4 complete, Session 5 next.

## Sessions

| # | Session | API credits? | Status |
|---|---|---|---|
| 1 | Architecture + FlakyLab sandbox | No | Done |
| 2 | MCP server scaffold + `run_test_n_times` tool (`dotnet test --filter`, parse .trx), tested in MCP Inspector | No | Done |
| 3 | Code analyzer tools (`read_source`, `find_test_source`) | No | Done |
| 4 | Hand-coded agentic loop: MCP client + loop, driven by a scripted stub model | No | Done |
| 5 | Investigation prompt + first diagnoses, scored against ground truth | Yes | Planned |
| 6 | TeamCity in Docker + `get_flaky_candidates` tool | No | Planned |
| 7 | Fix → verify 10x → git branch/PR tools | Yes | Planned |
| 8 | Notifier, guardrails, audit log, final scorecard | Light | Planned |

Sessions 1–4 and 6 need no API credits (Session 4 runs on a scripted stub model). Sessions 5 and 7 need a real model: either the Claude API, with a Console API key supplied through the `ANTHROPIC_API_KEY` environment variable and never committed, or a local model behind the same `IModel` interface.

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
│   read_source · find_test_source · git_branch_commit_pr · notify           │
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

## Session 2: the `run_test_n_times` tool

`FlakyDetective.McpServer` runs over stdio and logs to stderr only. It exposes two tools: `ping` (a wiring check) and `run_test_n_times(n, testFilter?)`. The second runs `dotnet test` n times in sequence, reads each `.trx` file, and returns a structured result:

- `Runs`: one row per test per run, with outcome, duration in milliseconds and the error message for failures.
- `TestSummaries`: one entry per test with runs, passed, failed and failure rate.
- `Requested` and `Completed`, so partial data is visible.
- `TestFilter`, where null means the whole suite, so the scope of a result is explicit. A test can behave differently alone and in the suite.
- A batch-level `Error` for tool failures (build problems, a filter that matches nothing), kept separate from failing tests.

The design question was how to let Claude reason about uncertainty. A test that fails about 10% of the time passes 5 reruns about 59% of the time (0.9⁵), and batches of 10 runs vary noticeably: the same test gave 1 failure in one batch and 3 in the next. The result therefore carries run counts and rates, so the caller can decide to ask for more runs.

Known limitation: the tool uses `--no-build`, so the sandbox must be built beforehand (stated in the tool description). A stale build produces results for old code.

## Session 3: reading the code

Two read-only tools let Claude look at the code behind a flaky test. A tool is a permission boundary: Claude can only read what the server chooses to expose, so the boundary is enforced in the server, not left to the caller.

- `read_source(path, startLine?, endLine?)` returns a file's lines prefixed with line numbers, plus the range returned, the total line count and a `Truncated` flag. At most 300 lines come back per call; when `Truncated` is true, the caller asks again from `EndLine + 1`.
- `find_test_source(testName)` finds a test method by exact name and returns the file and the 1-based first and last line, attributes included, ready to pass to `read_source`. Several matches are all returned; no match is an error.
- Failures in both tools come back as a normal result with an `Error` field, the same shape as a success, never as an exception.

The boundary (`PathGuard`):

- The root is the folder containing `FlakyDetective.slnx`, computed by the server. Paths are relative to it and no tool takes a root parameter.
- Paths are fully resolved before they are checked, so `..` segments, absolute paths, mixed separators and sibling folders with a matching prefix are all refused.
- Denied at any depth: `.git`, `.claude`, `bin`, `obj`, `TestResults`, files starting with `.env` or `GROUND_TRUTH`.
- Only text and source extensions are readable.

Known limitations:

- Symbolic links inside the solution are not resolved, so a link pointing outside the root would pass the check.
- `find_test_source` uses text matching, not a parser. It finds methods declared as `void` or `Task`, and counts braces inside strings and comments. Roslyn would remove these limits.

## Session 4: the agentic loop

`FlakyDetective.Agent` is the other half of the system: an MCP client that launches the server over stdio, lists its tools, and runs a hand-written loop.

The loop:

1. Send the message history and the tool definitions to the model.
2. If the reply's stop reason is not `tool_use`, stop: the reply is the answer.
3. Otherwise run every `tool_use` block through the MCP client's `CallToolAsync` and send all the results back in one user message of `tool_result` blocks, each carrying the id of the request it answers.
4. Repeat, up to an iteration cap.

Design points:

- **The model sits behind `IModel`.** The loop only needs "history and tools in, response out", so it does not care whether the answer comes from a script, a local model or Claude. The message and block types (`text`, `tool_use`, `tool_result`, stop reason) mirror the Anthropic Messages API, so a real adapter is a translation.
- **Session 4 runs on a scripted stub model**, so it needs no API credits. A stub cannot reason: it replays a fixed sequence (`ping`, then `find_test_source`, then `read_source` using the lines found, then done). That shows the loop mechanics honestly but not diagnosis, which starts in Session 5.
- **Two kinds of tool failure** both reach the model. A tool's own refusal (a denied path) comes back as normal text with an `error` field. A call the SDK rejects or that throws becomes a `tool_result` with `is_error` set. Neither crashes the loop.
- **The iteration cap is a guardrail.** The loop result says whether it stopped normally or hit the cap.
- **Every step is logged** to the console: iteration, what the model asked for, and each (truncated) result.

Verified with scripts: the normal run (4 iterations), a runaway model (stops at the cap), an unknown tool, a denied file, a path escape and a bad argument. All end sensibly and none leak file contents.

Known limitations:

- Tool errors for rejected arguments are generic ("An error occurred invoking 'read_source'"), so a real model has little to go on.
- The stub never produces a `tool_use` reply without tool blocks; a real model could.
- A real model adapter (Claude API or a local model) is not yet written.
