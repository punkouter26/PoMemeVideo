namespace PoMemeVideo.Shared.Domain;

/// <summary>
/// Runtime-mutable AI deployment selection. Registered as a singleton so the config endpoint
/// can change which Foundry deployment the director calls without restarting.
///
/// This used to carry a <c>Provider</c> string switching between AzureOpenAI, AiFoundry and
/// BrowserLLM, plus a catalogue of downloadable in-browser ONNX models. There is one director
/// now, so the only thing still worth changing at runtime is which deployment it targets.
/// </summary>
public sealed class RuntimeAiSettings
{
    /// <summary>
    /// Azure AI Foundry deployment name (e.g. "gpt-5.4-nano").
    /// Can be changed at runtime without restart.
    /// </summary>
    public string AiFoundryDeployment { get; set; } = "gpt-5.4-nano";
}
