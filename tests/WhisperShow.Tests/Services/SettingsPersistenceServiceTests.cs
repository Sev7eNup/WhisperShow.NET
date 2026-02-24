using System.IO;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WhisperShow.App.Services;

namespace WhisperShow.Tests.Services;

public class SettingsPersistenceServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public SettingsPersistenceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WhisperShowTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "appsettings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { /* cleanup best-effort */ }
    }

    private void WriteInitialSettings(string json)
    {
        File.WriteAllText(_filePath, json);
    }

    private SettingsPersistenceService CreateService()
    {
        return new SettingsPersistenceService(
            NullLogger<SettingsPersistenceService>.Instance,
            _filePath,
            debounceMs: 50);
    }

    [Fact]
    public async Task ScheduleUpdate_SingleMutator_WritesToFile()
    {
        WriteInitialSettings("""{ "WhisperShow": { "Language": "de" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WhisperShow"]!["Language"]!.GetValue<string>().Should().Be("en");
    }

    [Fact]
    public async Task ScheduleUpdate_MultipleMutators_ComposesAll()
    {
        WriteInitialSettings("""{ "WhisperShow": { "Language": "de", "Provider": "OpenAI" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "fr");
        service.ScheduleUpdate(section => section["Provider"] = "Local");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WhisperShow"]!["Language"]!.GetValue<string>().Should().Be("fr");
        doc["WhisperShow"]!["Provider"]!.GetValue<string>().Should().Be("Local");
    }

    [Fact]
    public async Task ScheduleUpdate_PreservesOtherKeys()
    {
        WriteInitialSettings("""{ "WhisperShow": { "Language": "de", "Provider": "OpenAI" }, "Logging": { "Level": "Info" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WhisperShow"]!["Provider"]!.GetValue<string>().Should().Be("OpenAI");
        doc["Logging"]!["Level"]!.GetValue<string>().Should().Be("Info");
    }

    [Fact]
    public async Task ScheduleUpdate_CreatesNestedObjects()
    {
        WriteInitialSettings("""{ "WhisperShow": {} }""");
        using var service = CreateService();

        service.ScheduleUpdate(section =>
        {
            var app = new JsonObject();
            app["Theme"] = "Dark";
            section["App"] = app;
        });
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WhisperShow"]!["App"]!["Theme"]!.GetValue<string>().Should().Be("Dark");
    }

    [Fact]
    public async Task ScheduleUpdate_NoPendingMutator_DoesNotWriteFile()
    {
        WriteInitialSettings("""{ "WhisperShow": { "Language": "de" } }""");
        var lastWrite = File.GetLastWriteTimeUtc(_filePath);
        using var service = CreateService();

        // Don't schedule anything — just wait
        await Task.Delay(200);

        File.GetLastWriteTimeUtc(_filePath).Should().Be(lastWrite);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        WriteInitialSettings("""{ "WhisperShow": {} }""");
        var service = CreateService();
        service.ScheduleUpdate(section => section["Test"] = "value");

        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ScheduleUpdate_OverwritesSameKey()
    {
        WriteInitialSettings("""{ "WhisperShow": { "Language": "de" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        service.ScheduleUpdate(section => section["Language"] = "fr");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WhisperShow"]!["Language"]!.GetValue<string>().Should().Be("fr");
    }
}
