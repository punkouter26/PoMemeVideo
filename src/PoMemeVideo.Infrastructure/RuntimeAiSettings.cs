namespace PoMemeVideo.Infrastructure;

/// <summary>
/// Runtime-mutable AI backend selection. Registered as a singleton so any
/// endpoint can read or write the active provider/model without restarting.
/// </summary>
public sealed class RuntimeAiSettings
{
    public static readonly string[] LocalModels =
    [
        "gemma3:1b",
        "llama3.2:1b",
        "qwen2.5:0.5b",
        "smollm2:360m",
    ];

    /// <summary>
    /// Hugging Face model ID served by Transformers.js in the browser.
    /// Must be an ONNX-quantised model published under the onnx-community or Xenova org.
    /// </summary>
    public const string BrowserLLMModel = "onnx-community/SmolLM2-360M-Instruct";

    /// <summary>"AzureOpenAI", "Ollama", or "BrowserLLM".</summary>
    public string Provider { get; set; } = "AzureOpenAI";

    /// <summary>Active Ollama model tag (only used when Provider == "Ollama").</summary>
    public string OllamaModel { get; set; } = "gemma3:1b";
}
