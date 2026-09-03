namespace PoMemeVideo.Shared.Domain;

/// <summary>
/// Runtime-mutable AI backend selection. Registered as a singleton so any
/// endpoint can read or write the active provider/model without restarting.
/// </summary>
public sealed class RuntimeAiSettings
{
    public const string DefaultBrowserLLMModel = "smollm2-360m-instruct-onnx";

    /// <summary>Valid provider identifiers.</summary>
    public static readonly IReadOnlySet<string> ValidProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AzureOpenAI", "AiFoundry", "BrowserLLM",
    };

    public static readonly IReadOnlyDictionary<string, string> LocalModelDisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["smollm2-360m-instruct-onnx"] = "SmolLM2 360M",
            ["qwen2.5-0.5b-instruct"] = "Qwen 2.5 0.5B",
            ["qwen2.5-1.5b-instruct"] = "Qwen 2.5 1.5B",
            ["phi-1_5-dev"] = "Phi 1.5",
            ["gemma-2-2b-jpn-it"] = "Gemma 2 2B",
            ["gemma-4-e2b-it-onnx"] = "Gemma 4 E2B",
        };

    /// <summary>"AzureOpenAI", "AiFoundry", or "BrowserLLM".</summary>
    public string Provider { get; set; } = "BrowserLLM";

    /// <summary>Local BrowserLLM model id, mapped to /models/{id} static files.</summary>
    public string BrowserLLMModel { get; set; } = DefaultBrowserLLMModel;

    /// <summary>
    /// Azure AI Foundry deployment name (e.g. "gpt-5.4-nano").
    /// Can be changed at runtime without restart.
    /// </summary>
    public string AiFoundryDeployment { get; set; } = "gpt-5.4-nano";
}
