using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace WriteSpeech.Core.Services.ModelManagement;

public class ModelDownloadHelper
{
    private const int BufferSize = 81920;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ModelDownloadHelper> _logger;

    public ModelDownloadHelper(IHttpClientFactory httpClientFactory, ILogger<ModelDownloadHelper> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task DownloadToFileAsync(
        Stream sourceStream,
        string targetPath,
        long expectedSize,
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default,
        string? expectedSha256 = null)
    {
        var tempPath = targetPath + ".downloading";
        try
        {
            using var sha256 = expectedSha256 is not null ? SHA256.Create() : null;

            await using (var fileStream = File.Create(tempPath))
            {
                var buffer = new byte[BufferSize];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    sha256?.TransformBlock(buffer, 0, bytesRead, null, 0);
                    totalRead += bytesRead;
                    progress?.Report((float)totalRead / expectedSize);
                }

                sha256?.TransformFinalBlock([], 0, 0);
                await fileStream.FlushAsync(cancellationToken);

                _logger.LogInformation("Download completed: {Size} bytes written to {Path}", totalRead, targetPath);
            }

            // Verify SHA-256 hash if provided
            if (sha256 is not null && expectedSha256 is not null)
            {
                var actualHash = Convert.ToHexStringLower(sha256.Hash!);
                if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError("Hash mismatch for {Path}: expected {Expected}, got {Actual}",
                        targetPath, expectedSha256, actualHash);
                    try { File.Delete(tempPath); } catch (Exception deleteEx) { _logger.LogDebug(deleteEx, "Failed to delete temp file after hash mismatch: {TempPath}", tempPath); }
                    throw new InvalidOperationException(
                        $"Downloaded file hash mismatch. Expected: {expectedSha256}, actual: {actualHash}");
                }

                _logger.LogInformation("SHA-256 verified for {Path}: {Hash}", targetPath, actualHash);
            }

            // Atomic rename: only move to final path after successful complete download + hash check
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            // Clean up partial temp file on failure or cancellation
            try { File.Delete(tempPath); } catch (Exception deleteEx) { _logger.LogDebug(deleteEx, "Failed to delete partial temp file: {TempPath}", tempPath); }
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
