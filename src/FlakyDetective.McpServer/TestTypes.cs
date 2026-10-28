using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace FlakyDetective.McpServer;

enum OutcomeType { NotExecuted, Passed, Failed }
record RunResult(string TestName, int RunNumber, OutcomeType Outcome, double DurationMs, string? Error);
record BatchRunResult(int Requested, int Completed, string? Error, string? TestFilter, List<RunResult> Runs, List<TestSummary> TestSummaries);
record TestSummary(string TestName, int Runs, int Passed, int Failed, double FailureRate);
