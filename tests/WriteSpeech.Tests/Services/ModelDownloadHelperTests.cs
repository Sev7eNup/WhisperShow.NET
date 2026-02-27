using System.IO;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using WriteSpeech.Core.Services.ModelManagement;

namespace WriteSpeech.Tests.Services;

public class ModelDownloadHelperTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ModelDownloadHelper _helper;

    public ModelDownloadHelperTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"writespeech-download-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var httpFactory = Substitute.For<IHttpClientFactory>();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        _helper = new ModelDownloadHelper(httpFactory, NullLogger<ModelDownloadHelper>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task DownloadToFileAsync_WritesCompleteStream()
    {
        var data = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
        using var sourceStream = new MemoryStream(data);
        var targetPath = Path.Combine(_tempDir, "test-download.bin");

        await _helper.DownloadToFileAsync(sourceStream, targetPath, data.Length);

        var written = await File.ReadAllBytesAsync(targetPath);
        written.Should().Equal(data);
    }

    [Fact]
    public async Task DownloadToFileAsync_ReportsProgressToCompletion()
    {
        var data = new byte[100_000]; // Large enough for multiple buffer reads
        Array.Fill(data, (byte)42);
        using var sourceStream = new MemoryStream(data);
        var targetPath = Path.Combine(_tempDir, "progress-test.bin");

        var progressValues = new List<float>();
        IProgress<float> progress = new SyncProgress<float>(v => progressValues.Add(v));

        await _helper.DownloadToFileAsync(sourceStream, targetPath, data.Length, progress);

        progressValues.Should().NotBeEmpty();
        progressValues.Last().Should().BeApproximately(1.0f, 0.01f);
    }

    [Fact]
    public async Task DownloadToFileAsync_CancellationStopsDownload()
    {
        var data = new byte[1_000_000];
        using var sourceStream = new MemoryStream(data);
        var targetPath = Path.Combine(_tempDir, "cancel-test.bin");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _helper.DownloadToFileAsync(sourceStream, targetPath, data.Length, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task DownloadToFileAsync_EmptyStream_CreatesEmptyFile()
    {
        using var sourceStream = new MemoryStream([]);
        var targetPath = Path.Combine(_tempDir, "empty-test.bin");

        await _helper.DownloadToFileAsync(sourceStream, targetPath, 0);

        File.Exists(targetPath).Should().BeTrue();
        (await File.ReadAllBytesAsync(targetPath)).Should().BeEmpty();
    }

    [Fact]
    public void CreateClient_SetsTimeout()
    {
        var httpFactory = Substitute.For<IHttpClientFactory>();
        var client = new HttpClient();
        httpFactory.CreateClient(Arg.Any<string>()).Returns(client);
        var helper = new ModelDownloadHelper(httpFactory, NullLogger<ModelDownloadHelper>.Instance);

        var result = helper.CreateClient(TimeSpan.FromMinutes(10));

        result.Timeout.Should().Be(TimeSpan.FromMinutes(10));
    }

    private class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
