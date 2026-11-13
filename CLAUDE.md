# FlakyLab — MCP + Agentic Flaky Test Detective

## What this is
A C#/.NET agent that hunts down flaky tests. It's built from two parts:
> an MCP server (the tool **producer**) and a hand-coded agentic loop that consumes it as an MCP client (the tool **orchestrator**). Together they investigate flaky tests and propose fixes that they verify. A human decides what happens to a fix; the agent raises no PRs.

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
MCP tools: `run_test_n_times`, `read_source` and `find_test_source` exist, as does `edit_source` (sandbox-only, dry-run by default); `notify` (Session 7) is planned; `get_flaky_candidates` (CI history) is optional, Session 8.

## Hard rules
- **Hand-code the loop.** Handle `tool_use` → MCP `CallToolAsync` → `tool_result` → repeat until `end_turn`. A real model adapter uses `client.Messages.Create`. Do NOT use `IChatClient` + `UseFunctionInvocation()`, because that automates the exact thing I need to demonstrate. The loop runs behind the `IModel` interface: a scripted stub, a local Ollama model (live in region 7 of `Program.cs`), or Claude through the Anthropic SDK (`AnthropicModel`, which uses `client.Messages.Create`; region 8, commented out). The message types mirror the Anthropic Messages API.
- The MCP server contains **no LLM calls**. It's deterministic C#.
- The server uses stdio, so **log to stderr only**. stdout is the protocol channel.
- **Do not diagnose or fix the sandbox tests yourself, and don't add comments explaining why they flake.** Finding the causes is the agent's job, and I score it against answers kept outside this repo.
- Guardrails: the agent never pushes to main, never edits files outside `sandbox/`, and caps loop iterations.

## Packages
- `Anthropic`: official C# SDK, v10+ (not the old tryAGI package)
- `ModelContextProtocol` (+ `Microsoft.Extensions.Hosting` for the server)
- NUnit for the sandbox

## API access
Claude Code runs on my Pro login. I am not paying for the API, so day-to-day the Agent project uses a free local model through Ollama (`qwen2.5:3b`). `AnthropicModel` exists for a real deployment but has not been run against the real API; it needs a Console API key with credits in the `ANTHROPIC_API_KEY` env var, never in the repo (default model `claude-opus-5-5`).

## Session plan
The session plan, status and open design questions live in [ROADMAP.md](ROADMAP.md), which is the single source of truth. Update its status line and table at the end of each session.

## Session log
- Session 1 (complete): architecture agreed; FlakyLabTests.cs written (6 tests, including 1 stable control); sandbox switched from xUnit to NUnit; FlakyDetective.slnx created with sandbox/FlakyLab; 10 consecutive runs gave 1 to 4 failures out of 6, so the sandbox is confirmed flaky.
- Session 2 (complete): `FlakyDetective.McpServer` (stdio, stderr logging) with `ping` and `run_test_n_times` (sequential `dotnet test` runs, `.trx` parsing, per-run rows plus per-test summaries). Verified in MCP Inspector, including empty-filter handling. The sandbox had to be built beforehand (`--no-build`); since #40 the tool builds it once per call, then runs with `--no-build`.
- Session 3 (complete): `read_source` and `find_test_source` tools behind `PathGuard` (root = solution folder computed server-side, deny list for `.git`, `.claude`, `.env*`, `GROUND_TRUTH*`, build output; text/source extensions only). Verified in MCP Inspector. `find_test_source` is text-based, not Roslyn.
- Session 4 (complete): `FlakyDetective.Agent` is an MCP client (stdio, launches the built server dll) with a hand-written loop behind `IModel`, an iteration cap and step logging. Runs on a scripted stub model, so no API credits. Verified with six scripts: normal run, runaway (stops at the cap), unknown tool, denied file, path escape, bad argument.
- Ollama adapter (complete): `OllamaModel : IModel` talks to a local Ollama server (`/api/chat`, tools, `num_ctx` 8192), live in region 7 of `Program.cs`. Stop reason comes from the presence of tool calls, not `done_reason`. With `qwen2.5:3b` the loop ran end to end and the model chained `find_test_source` then `read_source` itself. Errors just end the program.
- Anthropic adapter (complete, not run against the real API): `AnthropicModel : IModel` on the official SDK (default model `claude-opus-5-5`), with thinking blocks round-tripped and readable 401/400 errors; region 8 of `Program.cs` runs the loop with it (commented out, needs credits). Checked by serialising the request and by calls with an invalid key and no key.
- Session 5 (complete): system prompt carried through `IModel` and the loop; prompt versions V0 and V1 in `Prompts.cs`; the loop prints which tools were really called. Live runs on `qwen2.5:3b` never completed the four-step method and fabricated evidence, so the loop now enforces it: one tool per turn, no answer before the required steps have worked and the answer has a `Verdict:` line (nudges, then an `INCOMPLETE` flag), a report-only grounding check (`UNGROUNDED`, including failed runs listed in the answer that no tool reported), and a `MEASURED` line built from the tools' structured results to check the model's stated failure rates against. `run_test_n_times` returns only non-passing runs. Guardrails verified with scripted models (region 9). First scored batch (6 runs, prompt V1): 12/30; process fixed, content not (see the pilot scorecard in ROADMAP.md). Prompt V2, a retry nudge that quotes the exact read arguments, and an 800-token reply cap are in. V2 batch (10 runs): 24/50 (48%) - every run finished the method and none timed out, the suite rate on the flaky test was right 5/5, but the single-test rate was invented every time and 4 of 5 control runs wrongly said flaky; no cause matched the key. Then added a verdict-consistency check (`INCONSISTENT`, report only) and `stateMeasuredRates` (the loop appends the measured figures; prompt V3 stops asking the model for rates), checked with a scripted scenario and a replay of the saved logs. V3 batch (10 runs): 29/50 with loop-stated rates, 23/50 on the model's own output; the check flagged all 4 wrong verdicts.
- Session 6 (complete): the PR tool was dropped (a human decides what happens to a fix). `edit_source` writes to `sandbox/` only (`PathGuard.TryResolveForWrite`: `.cs` files, one exact match, dry-run by default, the model's line breaks converted to the file's own ending). `Verification.Check` decides from the tools' results whether a fix is proven: an applied edit, then 20 runs in both scopes after the last one, zero failures of the target. The loop prints `UNVERIFIED` when an applied edit is not proven (report only; live regions of `Program.cs` do not pass `targetTest` yet). Two test projects under `tests/` (McpServer 50, Agent 49 tests). An applied edit changes the test bed: restore with `git checkout -- sandbox/`.
- Fix phase, #40 (complete): `FixV1` prompt and `FixMethod.NextMissingStep` make a second loop run follow dry-run edit, one applied edit, 20 + 20 runs. `run_test_n_times` now builds the sandbox once per call, because `--no-build` would have measured the old code after an edit. `edit_source` uses the file's own line ending (the sandbox is LF). Region 11 of `Program.cs` breaks the control `Mid_is_positive` on purpose and a scripted model repairs it: 0/20 and 0/20, no `UNVERIFIED`, sandbox restored. Region 10 (live `qwen2.5:3b`): first try, control intact, it invented the `oldText`, `edit_source` refused three times, nothing written, run flagged `INCOMPLETE`; second try, control broken by hand, it copied the one-line edit exactly, applied it, and the loop's nudges got it through 20 + 20 runs: 0/20 and 0/20, no `UNVERIFIED`, sandbox identical to the original. This repaired a deliberate break, not a flaky test, and the model's own write-up misstated the other tests' results.
- Plan change (Nov 2026): the CI session (originally TeamCity in Docker) moved to the end as an optional Session 8, using a local history folder first and GitHub Actions second. Docker and TeamCity were dropped: heavy, and no help with learning the agent loop. The order is now Session 6 fix, verify and PR tools (#35), Session 7 notifier, guardrails review, audit log and final scorecard (#36), Session 8 optional CI candidates (#34, with a list of improvements to consider).
