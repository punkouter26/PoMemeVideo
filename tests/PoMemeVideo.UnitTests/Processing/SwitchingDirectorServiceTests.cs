namespace PoMemeVideo.UnitTests.Processing;

/// <summary>
/// Pins the runtime provider-selection rule. <c>RuntimeAiSettings.Provider</c> is mutable at
/// runtime through <c>PUT /api/config/ai-model</c> and can be restored from a persisted settings
/// file, so this is reachable with any string a caller has ever been able to save — including
/// values written by an older build.
/// </summary>
public sealed class SwitchingDirectorServiceTests
{
    // The Backend enum is internal, so the expected value travels as its name — an InlineData
    // argument has to be at least as accessible as the test method itself.
    [Theory]
    [InlineData("AzureOpenAI", "AzureOpenAi")]
    [InlineData("AiFoundry", "AiFoundry")]
    [InlineData("BrowserLLM", "BrowserLlm")]
    public void SelectBackend_KnownProvider_SelectsItsDirector(string provider, string expected)
    {
        Assert.Equal(expected, SwitchingDirectorService.SelectBackend(provider).ToString());
    }

    [Theory]
    [InlineData("azureopenai", "AzureOpenAi")]
    [InlineData("AZUREOPENAI", "AzureOpenAi")]
    [InlineData("browserllm", "BrowserLlm")]
    [InlineData("BROWSERLLM", "BrowserLlm")]
    public void SelectBackend_IsCaseInsensitive(string provider, string expected)
    {
        // RuntimeAiSettings.ValidProviders compares case-insensitively, so dispatch must agree —
        // otherwise a provider that validates fine silently routes somewhere else.
        Assert.Equal(expected, SwitchingDirectorService.SelectBackend(provider).ToString());
    }

    [Theory]
    [InlineData("Ollama")]   // removed provider, may still sit in a persisted settings file
    [InlineData("nonsense")]
    [InlineData("")]
    [InlineData(null)]
    public void SelectBackend_UnknownProvider_FallsBackToFoundryRatherThanThrowing(string? provider)
    {
        Assert.Equal("AiFoundry", SwitchingDirectorService.SelectBackend(provider).ToString());
    }
}

/// <summary>
/// The provider allow-list is the contract enforced by <c>PUT /api/config/ai-model</c>.
/// </summary>
public sealed class RuntimeAiSettingsTests
{
    [Fact]
    public void Defaults_AreTheBrowserModelAndTheNanoDeployment()
    {
        var settings = new RuntimeAiSettings();

        Assert.Equal("BrowserLLM", settings.Provider);
        Assert.Equal(RuntimeAiSettings.DefaultBrowserLLMModel, settings.BrowserLLMModel);
        Assert.Equal("gpt-5.4-nano", settings.AiFoundryDeployment);
    }

    [Fact]
    public void DefaultProvider_IsAccepted_ByItsOwnAllowList()
    {
        Assert.Contains(new RuntimeAiSettings().Provider, RuntimeAiSettings.ValidProviders);
    }

    [Fact]
    public void DefaultBrowserModel_IsInTheDisplayNameCatalogue()
    {
        // The GET endpoint falls back to the first catalogue entry when the active model is not
        // a known id, so a default that is missing here silently changes what the UI shows.
        Assert.Contains(RuntimeAiSettings.DefaultBrowserLLMModel, RuntimeAiSettings.LocalModelDisplayNames.Keys);
    }

    [Theory]
    [InlineData("AzureOpenAI")]
    [InlineData("AiFoundry")]
    [InlineData("BrowserLLM")]
    [InlineData("aifoundry")]
    [InlineData("browserllm")]
    public void ValidProviders_AcceptsSupportedProvidersCaseInsensitively(string provider)
    {
        Assert.Contains(provider, RuntimeAiSettings.ValidProviders);
    }

    [Fact]
    public void ValidProviders_RejectsOllama()
    {
        // A settings file persisted by an older build must not be able to re-enable a provider
        // whose implementation no longer exists.
        Assert.DoesNotContain("Ollama", RuntimeAiSettings.ValidProviders);
    }

    [Fact]
    public void EveryValidProvider_DispatchesToADistinctBackend()
    {
        // Guards the pairing between the allow-list and the dispatch switch: a provider that
        // validates but shares AiFoundry's fallback branch would be accepted and then ignored.
        var backends = RuntimeAiSettings.ValidProviders
            .Select(SwitchingDirectorService.SelectBackend)
            .ToArray();

        Assert.Equal(RuntimeAiSettings.ValidProviders.Count, backends.Distinct().Count());
    }
}
