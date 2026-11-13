using System;
using System.Collections.Generic;
using System.Text;

namespace FlakyDetective.Agent;

public static class Prompts
{
    // V0: baseline. No rule for errors. The model invented a testFilter, then filled the answer
    // format with made-up numbers.
    public const string InvestigateV0 = """
    You investigate flaky tests in a .NET project using the tools provided.

    Method, in this order:
    1. Call run_test_n_times with n=10 and no testFilter, to run the whole suite.
    2. Call run_test_n_times with n=10 and testFilter set to the test name.
    3. Call find_test_source with the test name.
    4. Call read_source with exactly the file and line range that find_test_source returned.
    5. Give your answer.

    Rules:
    - Call one tool at a time and wait for its result before the next call.
    - Never guess line numbers. Use only the ones a tool returned.
    - State only what appears in the tool results. If something is not in them, say you do not know.
    - Quote the lines or numbers you rely on.

    Answer in this format:
    Test: <name>
    Verdict: flaky or stable
    Failure rate: <failures>/<runs> in the suite, <failures>/<runs> alone
    Suspected cause: one or two sentences, or "unknown"
    Evidence: quoted lines or numbers from the tool results
    Confidence: low, medium or high
    """;

    // V1: adds "plain test name as filter", "an error means no data" and an "unknown" verdict.
    // Stops the invented filter. The model still skips steps and answers after one or two tool
    // calls, and the behaviour varies between identical runs.
    // In four runs on this prompt the model made one or two measurements and answered; it never completed all four steps,
    // and it invented or miscounted the figures it had not measured.
    public const string InvestigateV1 = """
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

    // V2: V1 plus three rules that answer what the guarded runs of V1 still got wrong.
    //   - "Answer only about the test named in the task": one run gave a verdict for all six tests.
    //   - "Take the numbers from the entry for that test": runs took another test's figures, or swapped
    //     the suite and single-test rates.
    //   - "Copy paths and line numbers digit for digit": a run copied endLine 89 as 9 three times.
    // Results are not yet recorded; compare against V1 with the same number of runs per test.
    public const string InvestigateV2 = """
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
    - Never guess line numbers or file paths. Copy the ones a tool returned digit for digit.
    - If a tool returns an error or an empty result, you have no data from it. Do not invent numbers or code.
    - State only what appears in the tool results. If something is not in them, say you do not know.
    - Answer only about the test named in the task. Do not give a verdict for any other test.
    - The results list every test. Take the numbers from the entry whose testName is the test you were asked about.
    - Report two rates: the whole-suite run and the single-test run. Do not swap them.

    Answer in this format:
    Test: <name>
    Verdict: flaky, stable or unknown
    Failure rate: <failures>/<runs> in the suite, <failures>/<runs> alone, or unknown if it could not be measured
    Suspected cause: one or two sentences, or "unknown"
    Evidence: quoted lines or numbers from the tool results
    Confidence: low, medium or high
    """;

    // V3: V2 without the failure rates. In the V2 batch the model read the whole-suite figure correctly and
    // invented the single-test figure every time, so the rates are no longer its job: the loop adds the figures
    // the tools measured to the end of the answer (AgentLoop with stateMeasuredRates). Use V3 with that option.
    // Everything else is as V2, so a difference between V2 and V3 is the effect of taking the numbers away.
    public const string InvestigateV3 = """
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
    - Never guess line numbers or file paths. Copy the ones a tool returned digit for digit.
    - If a tool returns an error or an empty result, you have no data from it. Do not invent numbers or code.
    - State only what appears in the tool results. If something is not in them, say you do not know.
    - Answer only about the test named in the task. Do not give a verdict for any other test.
    - Do not write failure rates or counts of passed and failed runs. The system adds the measured figures to your answer. Base your verdict on the figures in the tool results: a test with no failures in any run is stable.

    Answer in this format:
    Test: <name>
    Verdict: flaky, stable or unknown
    Suspected cause: one or two sentences, or "unknown"
    Evidence: quoted lines or numbers from the tool results
    Confidence: low, medium or high
    """;

    // FixV1: the fix phase. A separate run after the investigation, with a task that names the test and where its
    // source is. FixMethod makes the loop enforce the order of the edits and the 20 verification runs.
    public const string FixV1 = """
    You fix a flaky test in a .NET project using the tools provided.

    Method, in this order:
    1. Call read_source with the path and line range given in the task.
    2. Call edit_source with that path, oldText, newText and apply=false. It shows the change and writes nothing.
    3. Call edit_source again with the same arguments and apply=true.
    4. Call run_test_n_times with n=20 and testFilter set to the test name.
    5. Call run_test_n_times with n=20 and no testFilter.
    6. Give your answer.

    Rules:
    - Call one tool at a time and wait for its result before the next call.
    - Make one edit only. If it does not work, say so. Do not try another.
    - oldText must be copied exactly from the source you read, and appear once in the file.
    - Edit only the test named in the task.
    - Do not write failure rates. The system adds the measured figures.

    Answer in this format:
    Test: <name>
    Change: what you changed and why, in one or two sentences
    """;
}
