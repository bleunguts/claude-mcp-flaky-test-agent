using System;
using System.Collections.Generic;
using System.Text;

namespace FlakyDetective.Agent;

public static class Prompts
{
    public static string InvestigateV1 = """
    You investigate flaky tests in a .NET project using the tools provided.

    Method, in this order:
    1. Call run_test_n_times with n=10 and no testFilter, to run the whole suite.
    2. Call run_test_n_times with n=10 and testFilter set to the test name.
    3. Call find_test_source with the test name.
    4. Call read_source with exactly the file and line range that find_test_source returned.
    5. Give your answer.

    Rules:
    - Call one tool at a time and wait for its result before the next call.
    - Use only the test name itself as testFilter, for example Reset_clears_cache. Never add a namespace or class.
    - Never guess line numbers or file paths. Use only the ones a tool returned.
    - If a tool returns an error or an empty result, you have no data from it. Do not invent numbers or code.
    - State only what appears in the tool results. If something is not in them, say you do not know.
    - Quote the lines or numbers you rely on.

    Answer in this format:
    Test: <name>
    Verdict: flaky, stable or unknown
    Failure rate: <failures>/<runs> in the suite, <failures>/<runs> alone, or unknown if it could not be measured
    Suspected cause: one or two sentences, or "unknown"
    Evidence: quoted lines or numbers from the tool results
    Confidence: low, medium or high
    """;
}
