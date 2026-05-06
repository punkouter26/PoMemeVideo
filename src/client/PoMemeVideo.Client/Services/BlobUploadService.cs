using Microsoft.AspNetCore.Components.Forms;

namespace PoMemeVideo.Client.Services;

/// <summary>
/// Uploads a browser file directly to Azure Blob Storage using a pre-signed SAS URL.
/// Reports upload progress via an <see cref="IProgress{T}"/> callback (0–100).
/// </summary>
public sealed class BlobUploadService
{
    private readonly HttpClient _http;

    public BlobUploadService(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Streams <paramref name="file"/> to <paramref name="sasUrl"/> using an HTTP PUT request.
    /// </summary>
    /// <param name="sasUrl">Time-limited SAS URL with Write permission.</param>
    /// <param name="file">Browser file selected by the user.</param>
    /// <param name="progress">Optional progress reporter (0–100 percent).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task UploadAsync(
        string sasUrl,
        IBrowserFile file,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Azure Blob Storage requires Content-Type and x-ms-blob-type headers for PUT uploads
        using var content = new ProgressStreamContent(
            file.OpenReadStream(maxAllowedSize: 500L * 1024 * 1024),
            file.Size,
            progress);

        content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType ?? "application/octet-stream");

        using var request = new HttpRequestMessage(HttpMethod.Put, sasUrl)
        {
            Content = content,
        };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");

        var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// HttpContent that reads from a stream and reports progress as each chunk is sent.
/// </summary>
internal sealed class ProgressStreamContent : HttpContent
{
    private readonly Stream _stream;
    private readonly long _totalBytes;
    private readonly IProgress<int>? _progress;

    public ProgressStreamContent(Stream stream, long totalBytes, IProgress<int>? progress)
    {
        _stream = stream;
        _totalBytes = totalBytes;
        _progress = progress;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context)
    {
        var buffer = new byte[81920]; // 80 KB chunks
        long bytesSent = 0;
        int bytesRead;

        while ((bytesRead = await _stream.ReadAsync(buffer)) > 0)
        {
            await stream.WriteAsync(buffer.AsMemory(0, bytesRead));
            bytesSent += bytesRead;

            if (_progress is not null && _totalBytes > 0)
            {
                var percent = (int)(bytesSent * 100 / _totalBytes);
                _progress.Report(Math.Min(percent, 100));
            }
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _totalBytes;
        return _totalBytes > 0;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _stream.Dispose();
        base.Dispose(disposing);
    }
}
