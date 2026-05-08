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

    private string _activeProvider = "BrowserLLM";
    private string _pendingProvider = "BrowserLLM";
    private string _activeBrowserModelId = "smollm2-360m-instruct-onnx";
    private string _pendingBrowserModelId = "smollm2-360m-instruct-onnx";
    private List<LocalModelInfo> _localModels = [];
    private bool _modelDirty;
    private bool _modelApplying;
    private string? _modelMessage;
    private bool CanSelectBrowserLlm => _localModels.Count > 0;
    private int CurrentStep => !_uploaded ? 1 : _visionInProgress ? 2 : 3;

    private ElementReference _videoRef;
    private DitheredKeyframeStrip? _keyframeStrip;

    protected override async Task OnInitializedAsync()
    {
        await LoadAiModelStateAsync();
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
            _errorMessage = $"ERROR: {ex.Message}";
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
            _localModels = ai.LocalModels?.ToList() ?? [];
            _activeBrowserModelId = ai.BrowserLLMModel ?? (_localModels.FirstOrDefault()?.Id ?? _activeBrowserModelId);
            _pendingBrowserModelId = _activeBrowserModelId;

            _displayActiveModel = ai.Provider == "BrowserLLM"
                ? (_localModels.Count > 0
                    ? (_localModels.FirstOrDefault(m => m.Id == _activeBrowserModelId)?.Label ?? _activeBrowserModelId)
                    : "No local models downloaded")
                : "Azure OpenAI (GPT-4o)";

            RecomputeModelDirty();
        }
        catch
        {
            _displayActiveModel = "UNKNOWN";
        }
    }

    private void SelectProvider(string provider)
    {
        if (provider == "BrowserLLM" && !CanSelectBrowserLlm)
        {
            _modelMessage = "LOCAL MODELS NOT FOUND - run: python tools/download-models.py";
            return;
        }

        _pendingProvider = provider;
        RecomputeModelDirty();
        _modelMessage = null;
    }

    private void RecomputeModelDirty()
    {
        _modelDirty = _pendingProvider != _activeProvider
                      || _pendingBrowserModelId != _activeBrowserModelId;
    }

    private async Task ApplyModelAsync()
    {
        _modelApplying = true;
        _modelMessage = null;
        StateHasChanged();

        try
        {
            var body = new { provider = _pendingProvider, browserLLMModel = _pendingBrowserModelId };
            var response = await Http.PutAsJsonAsync("/api/config/ai-model", body);

            if (!response.IsSuccessStatusCode)
            {
                _modelMessage = $"MODEL SWITCH FAILED: HTTP {(int)response.StatusCode}";
                return;
            }

            _activeProvider = _pendingProvider;
            _activeBrowserModelId = _pendingBrowserModelId;
            _modelDirty = false;
            _displayActiveModel = _activeProvider == "BrowserLLM"
                ? (_localModels.Count > 0
                    ? (_localModels.FirstOrDefault(m => m.Id == _activeBrowserModelId)?.Label ?? _activeBrowserModelId)
                    : "No local models downloaded")
                : "Azure OpenAI (GPT-4o)";
            _modelMessage = $"MODEL ACTIVE: {_displayActiveModel}";
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
    private sealed record AiModelResponse(string Provider, string? BrowserLLMModel, LocalModelInfo[]? LocalModels, bool IsDevelopment);
    private sealed record LocalModelInfo(string Id, string Label);
    private sealed record FrameUploadResult(int FramesStored, VisionLabelItem[]? VisionLabels, VisionDiagnostics? VisionDiagnostics);
    private sealed record VisionDiagnostics(int FramesReceived, int FramesStored, bool AnalysisAttempted, string? AnalysisError, int LabelsDetected, string PlacementMode);
    private sealed record VisionLabelItem(double TimestampSeconds, string Label);
    private sealed record SasTokenResponse(Guid SessionId, string SasUrl, DateTimeOffset ExpiresAt);
    private sealed record ErrorResponse(string Error, string Message);
}
