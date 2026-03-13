namespace WriteSpeech.Core.Services.Configuration;

/// <summary>
/// Manages Windows auto-start registration via the HKCU Run registry key.
/// </summary>
public interface IAutoStartService
{
    /// <summary>Enables or disables launching the application at Windows login.</summary>
    void SetAutoStart(bool enable);
}
