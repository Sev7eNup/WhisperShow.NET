using System.Text.Json.Nodes;

namespace WriteSpeech.Core.Services.Configuration;

/// <summary>
/// Centralized appsettings.json persistence service. Multiple scheduled mutators
/// are composed and flushed together via debounced save.
/// </summary>
public interface ISettingsPersistenceService
{
    /// <summary>Schedules a mutation to the settings JSON document, flushed after a debounce delay.</summary>
    void ScheduleUpdate(Action<JsonNode> mutator);
    /// <summary>Immediately flushes all pending mutations to disk.</summary>
    Task FlushAsync();
}
