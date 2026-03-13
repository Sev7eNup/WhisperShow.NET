using System.IO;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Voxwright.App.Services;

namespace Voxwright.Tests.Services;

public class SettingsPersistenceServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public SettingsPersistenceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "VoxwrightTests_" + Guid.NewGuid().ToString("N")[..8]);
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
        WriteInitialSettings("""{ "Voxwright": { "Language": "de" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["Voxwright"]!["Language"]!.GetValue<string>().Should().Be("en");
    }

    [Fact]
    public async Task ScheduleUpdate_MultipleMutators_ComposesAll()
    {
        WriteInitialSettings("""{ "Voxwright": { "Language": "de", "Provider": "OpenAI" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "fr");
        service.ScheduleUpdate(section => section["Provider"] = "Local");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["Voxwright"]!["Language"]!.GetValue<string>().Should().Be("fr");
        doc["Voxwright"]!["Provider"]!.GetValue<string>().Should().Be("Local");
    }

    [Fact]
    public async Task ScheduleUpdate_PreservesOtherKeys()
    {
        WriteInitialSettings("""{ "Voxwright": { "Language": "de", "Provider": "OpenAI" }, "Logging": { "Level": "Info" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["Voxwright"]!["Provider"]!.GetValue<string>().Should().Be("OpenAI");
        doc["Logging"]!["Level"]!.GetValue<string>().Should().Be("Info");
    }

    [Fact]
    public async Task ScheduleUpdate_CreatesNestedObjects()
    {
        WriteInitialSettings("""{ "Voxwright": {} }""");
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
        doc["Voxwright"]!["App"]!["Theme"]!.GetValue<string>().Should().Be("Dark");
    }

    [Fact]
    public async Task ScheduleUpdate_NoPendingMutator_DoesNotWriteFile()
    {
        WriteInitialSettings("""{ "Voxwright": { "Language": "de" } }""");
        var lastWrite = File.GetLastWriteTimeUtc(_filePath);
        using var service = CreateService();

        // Don't schedule anything — just wait
        await Task.Delay(200);

        File.GetLastWriteTimeUtc(_filePath).Should().Be(lastWrite);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        WriteInitialSettings("""{ "Voxwright": {} }""");
        var service = CreateService();
        service.ScheduleUpdate(section => section["Test"] = "value");

        var act = () => service.Dispose();

        act.Should().NotThrow();
    }

    [Fact]
    public async Task ScheduleUpdate_RapidUpdates_LastValueWins()
    {
        WriteInitialSettings("""{ "Voxwright": { "Counter": 0 } }""");
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
        doc["Voxwright"]!["Counter"]!.GetValue<int>().Should().Be(10);
    }

    [Fact]
    public async Task ScheduleUpdate_OverwritesSameKey()
    {
        WriteInitialSettings("""{ "Voxwright": { "Language": "de" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        service.ScheduleUpdate(section => section["Language"] = "fr");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["Voxwright"]!["Language"]!.GetValue<string>().Should().Be("fr");
    }

    [Fact]
    public async Task ScheduleUpdate_InvalidJson_DoesNotThrow()
    {
        WriteInitialSettings("{ not valid json !!! }");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        await Task.Delay(200);

        // File should remain unchanged (invalid JSON can't be parsed)
        var content = await File.ReadAllTextAsync(_filePath);
        content.Should().Contain("not valid json");
    }

    [Fact]
    public async Task ScheduleUpdate_FileDoesNotExist_DoesNotThrow()
    {
        var missingPath = Path.Combine(_tempDir, "nonexistent", "appsettings.json");
        var service = new SettingsPersistenceService(
            NullLogger<SettingsPersistenceService>.Instance, missingPath, debounceMs: 50);

        service.ScheduleUpdate(section => section["Language"] = "en");

        // Should not throw, even though file doesn't exist
        await Task.Delay(200);
        service.Dispose();
    }

    [Fact]
    public async Task ScheduleUpdate_MissingVoxwrightSection_DoesNotCorrupt()
    {
        WriteInitialSettings("""{ "Logging": { "Level": "Info" } }""");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        await Task.Delay(200);

        // File should still contain original content (skipped save due to missing section)
        var content = await File.ReadAllTextAsync(_filePath);
        content.Should().Contain("Logging");
    }

    [Fact]
    public async Task ScheduleUpdate_WithJsonComments_PreservesValues()
    {
        // JSON with comments (common in appsettings.json)
        WriteInitialSettings("{\n  // Comment\n  \"Voxwright\": { \"Language\": \"de\" }\n}");
        using var service = CreateService();

        service.ScheduleUpdate(section => section["Language"] = "en");
        await Task.Delay(200);

        var json = await File.ReadAllTextAsync(_filePath);
        var doc = JsonNode.Parse(json)!;
        doc["Voxwright"]!["Language"]!.GetValue<string>().Should().Be("en");
    }
}
