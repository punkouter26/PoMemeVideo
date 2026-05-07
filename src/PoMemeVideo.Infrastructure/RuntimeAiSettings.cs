namespace PoMemeVideo.Infrastructure;

/// <summary>
/// Runtime-mutable AI backend selection. Registered as a singleton so any
/// endpoint can read or write the active provider/model without restarting.
/// </summary>
public sealed class RuntimeAiSettings
{
    public const string DefaultBrowserLLMModel = "smollm2-360m-instruct-onnx";

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

    /// <summary>"AzureOpenAI" or "BrowserLLM".</summary>
    public string Provider { get; set; } = "BrowserLLM";

    /// <summary>
    /// Local BrowserLLM model id, mapped to /models/{id} static files.
    /// </summary>
    public string BrowserLLMModel { get; set; } = DefaultBrowserLLMModel;
}
