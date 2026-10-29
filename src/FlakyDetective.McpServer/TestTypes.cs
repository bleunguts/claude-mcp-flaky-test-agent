using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace FlakyDetective.McpServer;

public enum OutcomeType { NotExecuted, Passed, Failed }
public record RunResult(string TestName, int RunNumber, OutcomeType Outcome, double DurationMs, string? Error);
public record BatchRunResult(int Requested, int Completed, string? Error, string? TestFilter, List<RunResult> Runs, List<TestSummary> TestSummaries);
public record TestSummary(string TestName, int Runs, int Passed, int Failed, double FailureRate);
public record ProcessResult(int ExitCode, string stdout, string? Stderr, string? PathToTrxFile);
public record ReadSourceResult(
    string Path,        // normalised, relative to the solution root, forward slashes
    string Content,     // the lines returned, each prefixed with its line number, e.g. "12: var x = 1;"
    int StartLine,      // first line actually returned (1-based)
    int EndLine,        // last line actually returned
    int TotalLines,     // lines in the whole file
    bool Truncated,     // true if the size limit cut the result short
    string? Error);     // null on success

public record TestLocation(string File, int StartLine, int EndLine);
public record FindTestResult(string TestName, List<TestLocation> Matches, string? Error);
