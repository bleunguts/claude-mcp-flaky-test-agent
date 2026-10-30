using Anthropic;

namespace FlakyDetective.Agent;

// Chooses which IModel the live run uses, so AgentLoop never changes.
//
//   --model scripted     the canned demo script, no LLM involved
//   --model ollama       a local Ollama model (default); model name from OLLAMA_MODEL, default qwen2.5:3b
//   --model anthropic    Claude through the Anthropic API; needs ANTHROPIC_API_KEY with credits;
//                        model name from ANTHROPIC_MODEL, default AnthropicModel.DefaultModel
//
// Shortcuts: --scripted, --ollama, --anthropic.
public static class ModelSelection
{
    public const string DefaultChoice = "ollama";

    public static IModel Create(string[] args)
    {
        var choice = ChoiceFrom(args);

        switch (choice)
        {
            case "scripted":
                return DemoScripts.Demo();

            case "ollama":
                var http = new HttpClient { BaseAddress = new Uri("http://localhost:11434"), Timeout = TimeSpan.FromMinutes(5) };
                var ollamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "qwen2.5:3b";
                return new OllamaModel(http, ollamaModel, 8192);

            case "anthropic":
                var anthropicModel = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL") ?? AnthropicModel.DefaultModel;
                return new AnthropicModel(new AnthropicClient(), anthropicModel);

            default:
                throw new ArgumentException($"Unknown model '{choice}'. Use scripted, ollama or anthropic.");
        }
    }

    public static string ChoiceFrom(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--model" && i + 1 < args.Length)
            {
                return args[i + 1].ToLowerInvariant();
            }

            if (args[i].StartsWith("--model=", StringComparison.Ordinal))
            {
                return args[i]["--model=".Length..].ToLowerInvariant();
            }

            if (args[i] is "--scripted" or "--ollama" or "--anthropic")
            {
                return args[i][2..];
            }
        }

        return DefaultChoice;
    }
}
