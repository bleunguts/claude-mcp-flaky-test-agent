using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace FlakyDetective.McpServer;

public enum OutcomeType { NotExecuted, Passed, Failed }
public record RunResult(string TestName, int RunNumber, OutcomeType Outcome, double DurationMs, string? Error);
public record BatchRunResult(int Requested, int Completed, string? Error, string? TestFilter, List<RunResult> Runs, List<TestSummary> TestSummaries);
public record TestSummary(string TestName, int Runs, int Passed, int Failed, double FailureRate);
public record ProcessResult(int ExitCode, string? Stderr, string? PathToTrxFile);