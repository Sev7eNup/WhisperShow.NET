using Microsoft.Extensions.Logging;

namespace WriteSpeech.Core.Services.ModelManagement;

public class ModelDownloadHelper
{
    private const int BufferSize = 81920;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ModelDownloadHelper> _logger;

    public ModelDownloadHelper(IHttpClientFactory httpClientFactory, ILogger<ModelDownloadHelper> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task DownloadToFileAsync(
        Stream sourceStream,
        string targetPath,
        long expectedSize,
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var tempPath = targetPath + ".downloading";
        try
        {
            await using (var fileStream = File.Create(tempPath))
            {
                var buffer = new byte[BufferSize];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    totalRead += bytesRead;
                    progress?.Report((float)totalRead / expectedSize);
                }

                await fileStream.FlushAsync(cancellationToken);

                _logger.LogInformation("Download completed: {Size} bytes written to {Path}", totalRead, targetPath);
            }

            // Atomic rename: only move to final path after successful complete download
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            // Clean up partial temp file on failure or cancellation
            try { File.Delete(tempPath); } catch { /* best-effort */ }
            throw;
        }
    }

    public HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var client = _httpClientFactory.CreateClient("ModelDownload");
        if (timeout.HasValue)
            client.Timeout = timeout.Value;
        return client;
    }
}
