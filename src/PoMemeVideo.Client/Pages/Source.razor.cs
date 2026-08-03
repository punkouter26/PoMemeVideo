using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PoMemeVideo.Client.Components;

namespace PoMemeVideo.Client.Pages;

public partial class Source
{
    private IBrowserFile? _selectedFile;
    private bool _uploaded;
    private bool _keyframeStripVisible;
    private bool _aggressiveVisuals;
    private bool _initiating;
    private bool _visionInProgress;
    private int _uploadProgress;
    private string _statusMessage = "PENDING";
    private string? _errorMessage;
    private bool _confirmedAggressiveVisuals;

    private Guid _sessionId;
    private string _blobPath = string.Empty;
    private double _videoDurationSeconds;

    private readonly List<VisionLabel> _visionLabels = [];
    private bool _visionAnalysed;
    private string _visionFallbackMessage = "AI VISION: no triggers detected - time-based placement will be used";
    private string _displayActiveModel = "UNKNOWN";

    private bool _isDevelopment;
    private bool _soundLibraryEmpty;
    private string _activeProvider = "BrowserLLM";
    private string _pendingProvider = "BrowserLLM";
    private string _activeBrowserModelId = "smollm2-360m-instruct-onnx";
    private string _pendingBrowserModelId = "smollm2-360m-instruct-onnx";
    private List<LocalModelInfo> _localModels = [];
    // AI Foundry
    private string _activeFoundryDeployment = "gpt-5.4-nano";
    private string _pendingFoundryDeployment = "gpt-5.4-nano";
    private List<string> _foundryDeployments = [];
    // Ollama (dev only)
    private bool _ollamaAvailable;
    private string _activeOllamaModel = "llama3.2";
    private string _pendingOllamaModel = "llama3.2";
    private List<string> _ollamaModels = [];
    private bool _modelDirty;
    private bool _modelApplying;
    private string? _modelMessage;
    // Unified model-selection dropdown (Remote / Ollama / Browser groups).
    private string _pendingModelSelection = "remote:gpt-5.4-nano";
    private string _dropdownHint = "";
    private int CurrentStep => !_uploaded ? 1 : _visionInProgress ? 2 : 3;

    private ElementReference _videoRef;
    private DitheredKeyframeStrip? _keyframeStrip;

    protected override async Task OnInitializedAsync()
    {
        await Task.WhenAll(LoadAiModelStateAsync(), CheckSoundLibraryAsync());
    }

    private async Task CheckSoundLibraryAsync()
    {
        try
        {
            var resp = await Http.GetAsync("/api/memelibrary/sounds?limit=1");
            if (resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadFromJsonAsync<SoundLibraryPageResponse>();
                _soundLibraryEmpty = body is null || body.TotalCount == 0;
            }
        }
        catch
        {
            // Non-critical — page still works without the warning
        }
    }

    private async Task OnFileAccepted(IBrowserFile file)
    {
        _selectedFile = file;
        _errorMessage = null;
        _uploadProgress = 0;
        _statusMessage = "REQUESTING SAS TOKEN...";
        _visionFallbackMessage = "AI VISION: no triggers detected - time-based placement will be used";
        StateHasChanged();

        try
        {
            var sasResponse = await Http.PostAsJsonAsync("/api/ingestion/sas", new
            {
                fileName = file.Name,
                fileSizeBytes = file.Size,
            });

            if (!sasResponse.IsSuccessStatusCode)
            {
                ErrorResponse? err = null;
                try { err = await sasResponse.Content.ReadFromJsonAsync<ErrorResponse>(); } catch { }
                _errorMessage = $"SAS ERROR: {err?.Message ?? sasResponse.ReasonPhrase} ({(int)sasResponse.StatusCode})";
                _statusMessage = "FAILED";
                return;
            }

            var sas = await sasResponse.Content.ReadFromJsonAsync<SasTokenResponse>()
                ?? throw new InvalidOperationException("Empty SAS response");

            _sessionId = sas.SessionId;
            _blobPath = ExtractBlobPath(sas.SasUrl);
            _statusMessage = "UPLOADING...";
            StateHasChanged();

            var progress = new Progress<int>(p =>
            {
                _uploadProgress = p;
                InvokeAsync(StateHasChanged);
            });

            await BlobUpload.UploadAsync(sas.SasUrl, file, progress);
            _statusMessage = "EXTRACTING DURATION...";
            StateHasChanged();

            _videoDurationSeconds = await LoadVideoDurationAsync(file);

            _statusMessage = "CONFIRMING SESSION...";
            StateHasChanged();

            var confirmResponse = await Http.PostAsJsonAsync("/api/ingestion/sessions", new
            {
                sessionId = _sessionId,
                blobPath = _blobPath,
                videoDurationSeconds = _videoDurationSeconds,
                aggressiveVisuals = _aggressiveVisuals,
            });

            if (!confirmResponse.IsSuccessStatusCode)
            {
                _errorMessage = await BuildErrorMessageAsync(confirmResponse, "SESSION CONFIRM FAILED");
                _statusMessage = "FAILED";
                return;
            }

            _uploaded = true;
            _confirmedAggressiveVisuals = _aggressiveVisuals;
            _statusMessage = "UPLOAD COMPLETE";
            _keyframeStripVisible = true;
            StateHasChanged();

            if (_keyframeStrip is not null)
                await _keyframeStrip.GenerateAsync();

            _visionInProgress = true;
            _statusMessage = "ANALYSING VIDEO WITH AI...";
            StateHasChanged();

            try
            {
                var rawFrames = await JS.InvokeAsync<string[]>(
                    "canvasDither.captureRawFrames",
                    "ascii-file-input",
                    _videoRef,
                    3);

                if (rawFrames.Length > 0)
                {
                    var frameResponse = await Http.PostAsJsonAsync(
                        $"/api/ingestion/sessions/{_sessionId}/frames",
                        new { frames = rawFrames });

                    if (frameResponse.IsSuccessStatusCode)
                    {
                        var result = await frameResponse.Content.ReadFromJsonAsync<FrameUploadResult>();
                        _visionLabels.Clear();
                        if (result?.VisionLabels is { Length: > 0 } labels)
                            _visionLabels.AddRange(labels.Select(l => new VisionLabel(l.TimestampSeconds, l.Label)));

                        _visionAnalysed = true;
                        _visionFallbackMessage = BuildVisionFallbackMessage(result, rawFrames.Length);
                        _statusMessage = _visionLabels.Count > 0
                            ? $"READY - {_visionLabels.Count} SCENE(S) IDENTIFIED"
                            : "READY - NO AI TRIGGERS FOUND (time-based placement will be used)";
                    }
                    else
                    {
                        _statusMessage = await BuildErrorMessageAsync(frameResponse, "READY (frame analysis failed - AI will use fallback)");
                    }
                }
                else
                {
                    _statusMessage = "READY (no frames extracted)";
                }
            }
            catch
            {
                _statusMessage = "UPLOAD COMPLETE (frame capture failed - AI will use fallback)";
            }
            finally
            {
                _visionInProgress = false;
            }
        }
        catch (Exception ex)
        {
            var msg = ex.Message;
            _errorMessage = IsNetworkOrCorsError(msg)
                ? "UPLOAD FAILED — CORS not configured. Ensure Azurite is running, then restart the API. " +
                  $"(Detail: {msg})"
                : $"ERROR: {msg}";
            _statusMessage = "FAILED";
        }
        finally
        {
            StateHasChanged();
        }
    }

    private static string BuildVisionFallbackMessage(FrameUploadResult? result, int capturedFrames)
    {
        var diagnostics = result?.VisionDiagnostics;
        var labelsDetected = diagnostics?.LabelsDetected ?? result?.VisionLabels?.Length ?? 0;
        if (labelsDetected > 0)
            return "AI VISION: semantic triggers detected";

        var framesStored = diagnostics?.FramesStored ?? result?.FramesStored ?? 0;
        var reason = !string.IsNullOrWhiteSpace(diagnostics?.AnalysisError)
            ? $"analysis failed ({diagnostics.AnalysisError})"
            : framesStored == 0
                ? "no frames extracted"
                : "no triggers detected";

        return $"AI VISION: {reason} (captured={capturedFrames}, stored={framesStored}, labels={labelsDetected}) - time-based placement will be used";
    }

    private async Task OnInitiate()
    {
        if (_activeProvider == "BrowserLLM" && !_localModels.Any(model => model.Available))
        {
            if (_pendingProvider != "BrowserLLM")
            {
                // User has selected a valid provider but hasn't applied it yet — apply automatically.
                await ApplyModelAsync();
                if (_activeProvider == "BrowserLLM")
                {
                    // Apply failed; error already set by ApplyModelAsync via _modelMessage, surface it here too.
                    _errorMessage = _modelMessage ?? "MODEL SWITCH FAILED. Please try again.";
                    _statusMessage = "READY";
                    return;
                }
            }
            else
            {
                _errorMessage = "LOCAL AI MODEL FILES ARE MISSING. Select Remote AI, then click Apply Model before creating a video.";
                _statusMessage = "READY";
                return;
            }
        }

        _initiating = true;
        _errorMessage = null;
        StateHasChanged();

        try
        {
            _statusMessage = "SAVING SESSION OPTIONS...";

            if (_uploaded && _confirmedAggressiveVisuals != _aggressiveVisuals)
            {
                var response = await Http.PutAsJsonAsync($"/api/ingestion/sessions/{_sessionId}/options", new
                {
                    aggressiveVisuals = _aggressiveVisuals,
                });

                if (!response.IsSuccessStatusCode)
                {
                    _errorMessage = await BuildErrorMessageAsync(response, "FAILED TO SAVE SESSION OPTIONS");
                    _statusMessage = "READY";
                    return;
                }

                _confirmedAggressiveVisuals = _aggressiveVisuals;
            }

            Nav.NavigateTo($"/engine/{_sessionId}");
        }
        finally
        {
            _initiating = false;
            StateHasChanged();
        }
    }

    private async Task<double> LoadVideoDurationAsync(IBrowserFile file)
    {
        try
        {
            return await JS.InvokeAsync<double>("canvasDither.getFileDuration", "ascii-file-input", _videoRef);
        }
        catch
        {
            return file.Size / (1024.0 * 1024.0);
        }
    }

    private static string ExtractBlobPath(string sasUrl)
    {
        var uri = new Uri(sasUrl);
        var path = uri.AbsolutePath.TrimStart('/');
        const string azuritePrefix = "devstoreaccount1/";
        if (path.StartsWith(azuritePrefix, StringComparison.OrdinalIgnoreCase))
            path = path[azuritePrefix.Length..];
        return path;
    }

    private static string ProgressBar(int percent)
    {
        const int width = 20;
        var filled = (int)(percent / 100.0 * width);
        return new string('█', filled) + new string('░', width - filled);
    }

    private string GetStepClass(int step)
    {
        if (step < CurrentStep)
            return "is-complete";

        return step == CurrentStep ? "is-active" : "is-pending";
    }

    private async Task LoadAiModelStateAsync()
    {
        try
        {
            var ai = await Http.GetFromJsonAsync<AiModelResponse>("/api/config/ai-model");
            if (ai is null)
                return;

            _activeProvider = ai.Provider;
            _pendingProvider = ai.Provider;
            _isDevelopment = ai.IsDevelopment;
            _localModels = ai.LocalModels?.ToList() ?? [];
            _activeBrowserModelId = ai.BrowserLLMModel ?? (_localModels.FirstOrDefault()?.Id ?? _activeBrowserModelId);
            _pendingBrowserModelId = _activeBrowserModelId;

            _activeFoundryDeployment = ai.AiFoundryDeployment ?? "gpt-5.4-nano";
            _pendingFoundryDeployment = _activeFoundryDeployment;
            _foundryDeployments = ai.AiFoundryDeployments?.ToList() ?? ["gpt-5.4-nano"];

            _ollamaAvailable = ai.OllamaAvailable;
            _activeOllamaModel = ai.OllamaModel ?? "llama3.2";
            _pendingOllamaModel = _activeOllamaModel;
            _ollamaModels = ai.OllamaModels?.ToList() ?? [];

            // Seed the unified dropdown selection from the active provider/model.
            _pendingModelSelection = ai.Provider switch
            {
                "AzureOpenAI" => $"remote:{_activeFoundryDeployment}",
                "AiFoundry" => $"remote:{_activeFoundryDeployment}",
                "Ollama" => $"ollama:{_activeOllamaModel}",
                "BrowserLLM" => $"browser:{_activeBrowserModelId}",
                _ => $"remote:{_activeFoundryDeployment}",
            };
            UpdateDropdownHint();

            if (_activeProvider == "BrowserLLM" && !_localModels.Any(model => model.Available))
            {
                // Keep active provider unchanged until user applies, but preselect a viable option.
                _pendingProvider = "AzureOpenAI";
                _modelMessage = "LOCAL MODELS NOT FOUND. Remote AI preselected - click Apply Model to continue.";
            }

            _displayActiveModel = ComputeDisplayName(ai.Provider);
            RecomputeModelDirty();
        }
        catch
        {
            _displayActiveModel = "UNKNOWN";
        }
    }

    private string ComputeDisplayName(string provider) => provider switch
    {
        "AzureOpenAI" => "Azure OpenAI · GPT-5.4 Nano",
        "AiFoundry" => $"AI Foundry · {_activeFoundryDeployment}",
        "Ollama" => $"Ollama · {_activeOllamaModel}",
        "BrowserLLM" => _localModels.Count > 0
                             ? (_localModels.FirstOrDefault(m => m.Id == _activeBrowserModelId)?.Label ?? _activeBrowserModelId)
                             : "No local models downloaded",
        _ => provider,
    };

    private void RecomputeModelDirty()
    {
        _modelDirty = _pendingProvider != _activeProvider
                      || _pendingBrowserModelId != _activeBrowserModelId
                      || _pendingFoundryDeployment != _activeFoundryDeployment
                      || _pendingOllamaModel != _activeOllamaModel;
    }

    private void OnModelSelectionChanged()
    {
        // The dropdown uses "kind:name" tokens so the optgroup value is unambiguous.
        if (string.IsNullOrWhiteSpace(_pendingModelSelection)) return;
        var sep = _pendingModelSelection.IndexOf(':');
        if (sep <= 0) return;
        var kind = _pendingModelSelection[..sep];
        var name = _pendingModelSelection[(sep + 1)..];
        switch (kind)
        {
            case "remote":
                _pendingProvider = "AzureOpenAI";
                _pendingFoundryDeployment = name;
                _dropdownHint = $"Remote AI Foundry deployment → {name}";
                break;
            case "ollama":
                _pendingProvider = "Ollama";
                _pendingOllamaModel = name;
                _dropdownHint = $"Ollama model → {name}";
                break;
            case "browser":
                _pendingProvider = "BrowserLLM";
                _pendingBrowserModelId = name;
                _dropdownHint = $"Browser WebGPU model → {name}";
                break;
        }
        RecomputeModelDirty();
    }

    private void UpdateDropdownHint()
    {
        _dropdownHint = _pendingProvider switch
        {
            "AzureOpenAI" => $"☁ AI Foundry · {_pendingFoundryDeployment}",
            "AiFoundry" => $"☁ AI Foundry · {_pendingFoundryDeployment}",
            "Ollama" => $"🦙 Ollama · {_pendingOllamaModel}",
            "BrowserLLM" => $"⚡ Browser · {(string.IsNullOrEmpty(_pendingBrowserModelId) ? "(no model)" : _pendingBrowserModelId)}",
            _ => "",
        };
    }

    private async Task ApplyModelAsync()
    {
        _modelApplying = true;
        _modelMessage = null;
        StateHasChanged();

        try
        {
            var body = new
            {
                provider = _pendingProvider,
                browserLLMModel = _pendingBrowserModelId,
                aiFoundryDeployment = _pendingFoundryDeployment,
                ollamaModel = _pendingOllamaModel,
            };
            var response = await Http.PutAsJsonAsync("/api/config/ai-model", body);

            if (!response.IsSuccessStatusCode)
            {
                var msg = await BuildErrorMessageAsync(response, "MODEL SWITCH FAILED");
                _modelMessage = msg;
                return;
            }

            _activeProvider = _pendingProvider;
            _activeBrowserModelId = _pendingBrowserModelId;
            _activeFoundryDeployment = _pendingFoundryDeployment;
            _activeOllamaModel = _pendingOllamaModel;
            _modelDirty = false;
            _displayActiveModel = ComputeDisplayName(_activeProvider);
            _modelMessage = $"MODEL ACTIVE: {_displayActiveModel}";
            await NavRefresh.NotifyAiChangedAsync();
        }
        catch (Exception ex)
        {
            _modelMessage = $"MODEL SWITCH FAILED: {ex.Message}";
        }
        finally
        {
            _modelApplying = false;
            StateHasChanged();
        }
    }

    private static async Task<string> BuildErrorMessageAsync(HttpResponseMessage response, string fallback)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(body))
                return $"{fallback}: HTTP {(int)response.StatusCode} {body}";
        }
        catch
        {
        }

        return $"{fallback}: HTTP {(int)response.StatusCode}";
    }

    private sealed record VisionLabel(double TimestampSeconds, string Label);
    private sealed record AiModelResponse(
        string Provider,
        string? BrowserLLMModel,
        LocalModelInfo[]? LocalModels,
        string? AiFoundryDeployment,
        string[]? AiFoundryDeployments,
        bool OllamaAvailable,
        string? OllamaModel,
        string[]? OllamaModels,
        bool IsDevelopment);
    private sealed record LocalModelInfo(string Id, string Label, bool Available);
    private sealed record FrameUploadResult(int FramesStored, VisionLabelItem[]? VisionLabels, VisionDiagnostics? VisionDiagnostics);
    private sealed record VisionDiagnostics(int FramesReceived, int FramesStored, bool AnalysisAttempted, string? AnalysisError, int LabelsDetected, string PlacementMode);
    private sealed record VisionLabelItem(double TimestampSeconds, string Label);
    private sealed record SasTokenResponse(Guid SessionId, string SasUrl, DateTimeOffset ExpiresAt);
    private sealed record ErrorResponse(string Error, string Message);
    private sealed record SoundLibraryPageResponse(int TotalCount);

    private static bool IsNetworkOrCorsError(string message) =>
        message.Contains("fetch", StringComparison.OrdinalIgnoreCase)
        || message.Contains("NetworkError", StringComparison.OrdinalIgnoreCase)
        || message.Contains("CORS", StringComparison.OrdinalIgnoreCase)
        || message.Contains("network", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Failed to", StringComparison.OrdinalIgnoreCase);
}
