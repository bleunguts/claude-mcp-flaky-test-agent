using System.Xml.Linq;

namespace FlakyDetective.McpServer;

public class TrxParser
{
    private const string xmlns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static List<RunResult> ParseResults(string trxPath, int runNumber)
    {
        if (!File.Exists(trxPath))
        {
            throw new FileNotFoundException($"TRX file not found: {trxPath}");
        }

        var results = new List<RunResult>();        
        XDocument.Load(trxPath)
            .Descendants(XName.Get("UnitTestResult", xmlns))
            .ToList()
            .ForEach(x =>
            {
                var testName = x.Attribute("testName")?.Value ?? throw new Exception("Missing testName attribute");
                var outcome = x.Attribute("outcome")?.Value ?? throw new Exception("Missing outcome attribute");
                var duration = x.Attribute("duration")?.Value ?? throw new Exception("Missing duration attribute");
                var error = x.Element(XName.Get("Output",xmlns))?.Element(XName.Get("ErrorInfo",xmlns))?.Element(XName.Get("Message",xmlns))?.Value;
                results.Add(new RunResult
                (
                    TestName: testName,
                    RunNumber: runNumber,
                    Outcome: outcome switch
                    {
                        "Passed" => OutcomeType.Passed,
                        "Failed" => OutcomeType.Failed,
                        "NotExecuted" => OutcomeType.NotExecuted,
                        _ => throw new Exception($"Unknown outcome: {outcome}")
                    },
                    DurationMs: TimeSpan.Parse(duration).TotalMilliseconds,
                    Error: error
                ));
            });
        return results;
    }
}