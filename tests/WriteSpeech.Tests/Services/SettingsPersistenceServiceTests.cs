using System.IO;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WriteSpeech.App.Services;

namespace WriteSpeech.Tests.Services;

public class SettingsPersistenceServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public SettingsPersistenceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WriteSpeechTests_" + Guid.NewGuid().ToString("N")[..8]);
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
        WriteInitialSettings("""{ "WriteSpeech": { "Language": "de" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WriteSpeech"]!["Language"]!.GetValue<string>().Should().Be("en");
    }

    [Fact]
    public async Task ScheduleUpdate_MultipleMutators_ComposesAll()
    {
        WriteInitialSettings("""{ "WriteSpeech": { "Language": "de", "Provider": "OpenAI" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "fr");
        service.ScheduleUpdate(section => section["Provider"] = "Local");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WriteSpeech"]!["Language"]!.GetValue<string>().Should().Be("fr");
        doc["WriteSpeech"]!["Provider"]!.GetValue<string>().Should().Be("Local");
    }

    [Fact]
    public async Task ScheduleUpdate_PreservesOtherKeys()
    {
        WriteInitialSettings("""{ "WriteSpeech": { "Language": "de", "Provider": "OpenAI" }, "Logging": { "Level": "Info" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WriteSpeech"]!["Provider"]!.GetValue<string>().Should().Be("OpenAI");
        doc["Logging"]!["Level"]!.GetValue<string>().Should().Be("Info");
    }

    [Fact]
    public async Task ScheduleUpdate_CreatesNestedObjects()
    {
        WriteInitialSettings("""{ "WriteSpeech": {} }""");
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
        doc["WriteSpeech"]!["App"]!["Theme"]!.GetValue<string>().Should().Be("Dark");
    }

    [Fact]
    public async Task ScheduleUpdate_NoPendingMutator_DoesNotWriteFile()
    {
        WriteInitialSettings("""{ "WriteSpeech": { "Language": "de" } }""");
        var lastWrite = File.GetLastWriteTimeUtc(_filePath);
        using var service = CreateService();

        // Don't schedule anything — just wait
        await Task.Delay(200);

        File.GetLastWriteTimeUtc(_filePath).Should().Be(lastWrite);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        WriteInitialSettings("""{ "WriteSpeech": {} }""");
        var service = CreateService();
        service.ScheduleUpdate(section => section["Test"] = "value");

        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ScheduleUpdate_RapidUpdates_LastValueWins()
    {
        WriteInitialSettings("""{ "WriteSpeech": { "Counter": 0 } }""");
        using var service = CreateService();

        // Schedule many rapid updates — all should compose correctly
        for (int i = 1; i <= 10; i++)
        {
            var value = i;
            service.ScheduleUpdate(section => section["Counter"] = value);
        }
        await Task.Delay(500);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WriteSpeech"]!["Counter"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task ScheduleUpdate_OverwritesSameKey()
    {
        WriteInitialSettings("""{ "WriteSpeech": { "Language": "de" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        service.ScheduleUpdate(section => section["Language"] = "fr");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["WriteSpeech"]!["Language"]!.GetValue<string>().Should().Be("fr");
    }
}
