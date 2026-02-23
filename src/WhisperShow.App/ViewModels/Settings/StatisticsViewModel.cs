using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperShow.Core.Services.Statistics;

namespace WhisperShow.App.ViewModels.Settings;

public partial class StatisticsViewModel : ObservableObject
{
    private readonly IUsageStatsService _statsService;

    [ObservableProperty] private int _totalTranscriptions;
    [ObservableProperty] private string _totalRecordingTimeDisplay = "0:00";
    [ObservableProperty] private string _averageDurationDisplay = "0.0s";
    [ObservableProperty] private string _estimatedCostDisplay = "$0.00";
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private string _providerBreakdownDisplay = "";

    public StatisticsViewModel(IUsageStatsService statsService)
    {
        _statsService = statsService;
    }

    [RelayCommand]
    public void Refresh()
    {
        var stats = _statsService.GetStats();
        TotalTranscriptions = stats.TotalTranscriptions;
        ErrorCount = stats.ErrorCount;
        TotalRecordingTimeDisplay = FormatDuration(stats.TotalRecordingSeconds);
        AverageDurationDisplay = $"{stats.AverageRecordingSeconds:F1}s";
        EstimatedCostDisplay = $"${stats.EstimatedApiCost:F4}";

        ProviderBreakdownDisplay = stats.TranscriptionsByProvider.Count > 0
            ? string.Join(", ", stats.TranscriptionsByProvider.Select(kv => $"{kv.Key}: {kv.Value}"))
            : "No data yet";
    }

    [RelayCommand]
    private void Reset()
    {
        _statsService.Reset();
        Refresh();
    }

    private static string FormatDuration(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : $"{ts.Minutes}m {ts.Seconds}s";
    }
}
