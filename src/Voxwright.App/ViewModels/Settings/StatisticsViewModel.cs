using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Voxwright.Core.Services.Statistics;

namespace Voxwright.App.ViewModels.Settings;

public partial class StatisticsViewModel : ObservableObject
{
    private readonly IUsageStatsService _statsService;

    [ObservableProperty] private int _totalTranscriptions;
    [ObservableProperty] private string _totalRecordingTimeDisplay = "0:00";
    [ObservableProperty] private string _averageDurationDisplay = "0.0s";
    [ObservableProperty] private string _wordsTranscribedDisplay = "0";
    [ObservableProperty] private string _timeSavedDisplay = "0 min";
    [ObservableProperty] private string _estimatedCostDisplay = "$0.00";
    [ObservableProperty] private string _successRateDisplay = "—";
    [ObservableProperty] private string _longestRecordingDisplay = "—";
    [ObservableProperty] private string _shortestRecordingDisplay = "—";
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private string _providerBreakdownDisplay = "";
    [ObservableProperty] private string _correctionBreakdownDisplay = "";

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

        WordsTranscribedDisplay = stats.TotalWordsTranscribed.ToString("N0");
        TimeSavedDisplay = FormatTimeSaved(stats.EstimatedTimeSavedMinutes);
        SuccessRateDisplay = stats.TotalTranscriptions + stats.ErrorCount > 0
            ? $"{stats.SuccessRatePercent:F1}%"
            : "—";
        LongestRecordingDisplay = stats.TotalTranscriptions > 0
            ? $"{stats.LongestRecordingSeconds:F1}s"
            : "—";
        ShortestRecordingDisplay = stats.ShortestRecordingSeconds.HasValue
            ? $"{stats.ShortestRecordingSeconds.Value:F1}s"
            : "—";

        ProviderBreakdownDisplay = stats.TranscriptionsByProvider.Count > 0
            ? string.Join(", ", stats.TranscriptionsByProvider.Select(kv => $"{kv.Key}: {kv.Value}"))
            : "No data yet";
        CorrectionBreakdownDisplay = stats.CorrectionsByProvider.Count > 0
            ? string.Join(", ", stats.CorrectionsByProvider.Select(kv => $"{kv.Key}: {kv.Value}"))
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

    internal static string FormatTimeSaved(double minutes)
    {
        if (minutes >= 60)
            return $"{minutes / 60:F1}h";
        return $"{minutes:F0} min";
    }
}
