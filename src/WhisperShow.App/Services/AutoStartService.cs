using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using WhisperShow.Core.Services.Configuration;

namespace WhisperShow.App.Services;

public class AutoStartService : IAutoStartService
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WhisperShow";

    private readonly ILogger<AutoStartService> _logger;

    public AutoStartService(ILogger<AutoStartService> logger)
    {
        _logger = logger;
    }

    public void SetAutoStart(bool enable)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                _logger.LogWarning("Cannot set autostart: ProcessPath is null");
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            if (key is null)
            {
                _logger.LogWarning("Cannot open Run registry key for writing");
                return;
            }

            var expected = $"\"{exePath}\"";
            var currentValue = key.GetValue(AppName) as string;

            if (enable)
            {
                if (!string.Equals(currentValue, expected, StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue(AppName, expected);
                    _logger.LogInformation("Set autostart registry entry to {Path}", expected);
                }
            }
            else
            {
                if (currentValue is not null)
                {
                    key.DeleteValue(AppName, throwOnMissingValue: false);
                    _logger.LogInformation("Removed autostart registry entry");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to {Action} autostart registry entry",
                enable ? "set" : "remove");
        }
    }
}
