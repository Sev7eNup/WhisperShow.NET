using System.Text.Json.Nodes;

namespace WriteSpeech.Core.Services.Configuration;

public interface ISettingsPersistenceService
{
    void ScheduleUpdate(Action<JsonNode> mutator);
    Task FlushAsync();
}
