using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using PoMemeVideo.Client.Components;
using PoMemeVideo.Client.Services;

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
    private string _displayActiveModel = "Azure OpenAI · GPT-5.4 Nano";

    private bool _isDevelopment;
    private bool _soundLibraryEmpty;
    private string _activeProvider = "AiFoundry";
    private string _pendingProvider = "AiFoundry";
    // BrowserLLM (WebGPU / ONNX, local to the browser)
    private const string DefaultBrowserModel = "smollm2-360m-instruct-onnx";
    private string _activeBrowserModelId = DefaultBrowserModel;
    private string _pendingBrowserModelId = DefaultBrowserModel;
    private List<LocalModelInfo> _localModels = [];
    // AI Foundry
    private const string DefaultDeployment = "gpt-5.4-nano";
    private string _activeFoundryDeployment = DefaultDeployment;
    private string _pendingFoundryDeployment = DefaultDeployment;
    // Seeded, not empty: the server enumerates deployments from ARM and that call is slow on a
    // cold start, so the first render happens before it returns. An empty list renders a
    // <select> with no <option>s — a blank control with no explanation.
    private List<string> _foundryDeployments = [DefaultDeployment];
    private bool _modelDirty;
    private bool _modelApplying;
    private string? _modelMessage;
    private string _pendingModelSelection = $"remote:{DefaultDeployment}";
    private string _dropdownHint = "";
    private int CurrentStep => !_uploaded ? 1 : _visionInProgress ? 2 : 3;

    private ElementReference _videoRef;
    private DitheredKeyframeStrip? _keyframeStrip;
    [Inject] private Vibe3DService Vibe3D { get; set; } = default!;

    private string _aspectRatio = "original";
    private string _memePersona = "Standard";
    private double _trimStart;
    private double _trimEnd;

    private void SetAspectRatio(string ratio)
    {
        _aspectRatio = ratio;
    }

    private void OnPersonaChanged()
    {
    }

    private void OnTrimChanged()
    {
        if (_trimEnd <= _trimStart)
            _trimEnd = Math.Min(_videoDurationSeconds, _trimStart + 1.0);
    }

    protected override async Task OnInitializedAsync()
    {
        await Vibe3D.SetAuroraStateAsync("idle");
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
        await Vibe3D.SetAuroraStateAsync("analyzing");

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
            _trimStart = 0;
            _trimEnd = _videoDurationSeconds;

            _statusMessage = "CONFIRMING SESSION...";
            StateHasChanged();

            var confirmResponse = await Http.PostAsJsonAsync("/api/ingestion/sessions", new
            {
                sessionId = _sessionId,
                blobPath = _blobPath,
                videoDurationSeconds = _videoDurationSeconds,
                aggressiveVisuals = _aggressiveVisuals,
                trimStartSeconds = (double?)null,
                trimDurationSeconds = (double?)null,
                memePersona = _memePersona,
                aspectRatio = _aspectRatio,
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
                // canvas-dither.js no longer tears the video down inside generateDitheredFrames
                // because captureRawFrames reuses the same <video> element. Release the blob:
                // URL and detach the source here so the page can be torn down cleanly.
                await ReleaseVideoElementAsync();
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
        _initiating = true;
        _errorMessage = null;
        StateHasChanged();

        try
        {
            _statusMessage = "SAVING SESSION OPTIONS...";

            if (_uploaded)
            {
                var confirmResponse = await Http.PostAsJsonAsync("/api/ingestion/sessions", new
                {
                    sessionId = _sessionId,
                    blobPath = _blobPath,
                    videoDurationSeconds = _videoDurationSeconds,
                    aggressiveVisuals = _aggressiveVisuals,
                    trimStartSeconds = _trimStart > 0 ? (double?)_trimStart : null,
                    trimDurationSeconds = (_trimEnd > _trimStart && _trimEnd < _videoDurationSeconds) ? (double?)(_trimEnd - _trimStart) : null,
                    memePersona = _memePersona,
                    aspectRatio = _aspectRatio,
                });

                if (!confirmResponse.IsSuccessStatusCode)
                {
                    _errorMessage = await BuildErrorMessageAsync(confirmResponse, "FAILED TO SAVE SESSION OPTIONS");
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

    private async Task ReleaseVideoElementAsync()
    {
        // canvas-dither.js leaves the <video> element loaded between the keyframe strip and
        // the AI vision capture (so the blob: URL isn't aborted by a second load). Detach it
        // once both captures have finished so the GC can reclaim the blob.
        try
        {
            await JS.InvokeVoidAsync("canvasDither.releaseVideo", _videoRef);
        }
        catch
        {
            // Non-critical: the page is leaving anyway and the element will be GC'd with the DOM.
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
                throw new InvalidOperationException("/api/config/ai-model returned no body.");

            _activeProvider = ai.Provider;
            _pendingProvider = ai.Provider;
            _isDevelopment = ai.IsDevelopment;

            _localModels = ai.LocalModels?.ToList() ?? [];
            _activeBrowserModelId = ai.BrowserLLMModel ?? (_localModels.FirstOrDefault()?.Id ?? _activeBrowserModelId);
            _pendingBrowserModelId = _activeBrowserModelId;

            _activeFoundryDeployment = ai.AiFoundryDeployment ?? DefaultDeployment;
            _pendingFoundryDeployment = _activeFoundryDeployment;
            if (ai.AiFoundryDeployments is { Length: > 0 } deployments)
                _foundryDeployments = deployments.ToList();

            _modelMessage = null;
        }
        catch
        {
            // The server enumerates AI Foundry deployments from ARM with DefaultAzureCredential.
            // That call can take tens of seconds on a cold start and can fail outright with no
            // Azure session. Falling through silently here used to leave _foundryDeployments
            // empty, which renders a <select> with zero <option>s — a blank control with no
            // explanation, indistinguishable from a broken page.
            _modelMessage = "MODEL LIST UNAVAILABLE — showing the active deployment only. "
                          + "Reload once Azure sign-in completes to see the full list.";
        }

        // Whatever happened above, the dropdown must never be empty: it always offers at least
        // the deployment that is actually active.
        if (!_foundryDeployments.Contains(_activeFoundryDeployment, StringComparer.OrdinalIgnoreCase))
            _foundryDeployments.Insert(0, _activeFoundryDeployment);

        _pendingModelSelection = _activeProvider == "BrowserLLM"
            ? $"browser:{_activeBrowserModelId}"
            : $"remote:{_activeFoundryDeployment}";
        UpdateDropdownHint();
        _displayActiveModel = ComputeDisplayName(_activeProvider);
        RecomputeModelDirty();

        // BrowserLLM is the Development default, but the ONNX weights are a separate download.
        // Without them the engine would stall on an inference request that can never complete,
        // so preselect the cloud path and tell the user why.
        if (_activeProvider == "BrowserLLM" && !_localModels.Any(model => model.Available))
        {
            _pendingProvider = "AiFoundry";
            _pendingModelSelection = $"remote:{_activeFoundryDeployment}";
            UpdateDropdownHint();
            RecomputeModelDirty();
            _modelMessage = "LOCAL MODELS NOT DOWNLOADED — Remote AI preselected. "
                          + "Click Apply, or run 'python scripts/download-models.py' to use the browser model.";
        }
    }

    private string ComputeDisplayName(string provider) => provider switch
    {
        "AzureOpenAI" => "Azure OpenAI · GPT-5.4 Nano",
        "AiFoundry" => $"AI Foundry · {_activeFoundryDeployment}",
        "BrowserLLM" => _localModels.Count > 0
            ? (_localModels.FirstOrDefault(m => m.Id == _activeBrowserModelId)?.Label ?? _activeBrowserModelId)
            : "No local models downloaded",
        _ => provider,
    };

    private void RecomputeModelDirty()
    {
        _modelDirty = _pendingProvider != _activeProvider
                      || _pendingFoundryDeployment != _activeFoundryDeployment
                      || _pendingBrowserModelId != _activeBrowserModelId;
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
                // Must be AiFoundry, not AzureOpenAI: the deployment name below is only honoured
                // by the Foundry director. Selecting a deployment used to silently switch to the
                // Azure OpenAI path, which ignores it.
                _pendingProvider = "AiFoundry";
                _pendingFoundryDeployment = name;
                _dropdownHint = $"Remote AI Foundry deployment → {name}";
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
            "BrowserLLM" => $"⚡ Browser · {(string.IsNullOrEmpty(_pendingBrowserModelId) ? "(no model)" : _pendingBrowserModelId)}",
            "AzureOpenAI" => "☁ Azure OpenAI · GPT-5.4 Nano",
            _ => $"☁ AI Foundry · {_pendingFoundryDeployment}",
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
            };
            var response = await Http.PutAsJsonAsync("/api/config/ai-model", body);

            if (!response.IsSuccessStatusCode)
            {
                var msg = await BuildErrorMessageAsync(response, "MODEL SWITCH FAILED");
                _modelMessage = msg;
                return;
            }

            _activeProvider = _pendingProvider;
            _activeFoundryDeployment = _pendingFoundryDeployment;
            _activeBrowserModelId = _pendingBrowserModelId;
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
