using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo;

public sealed partial class ActionLabelStep : ObservableObject
{
    [ObservableProperty]
    private int step;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? tcnLabel;

    [ObservableProperty]
    private bool isActive;

    public string DisplayText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(TcnLabel))
            {
                return $"{Step}. {Name}";
            }

            return $"{Step}. {Name} ({TcnLabel})";
        }
    }

    partial void OnStepChanged(int value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayText));
    partial void OnTcnLabelChanged(string? value) => OnPropertyChanged(nameof(DisplayText));
}

public sealed partial class ActionLabelViewModel : ObservableObject, IDisposable
{
    private readonly Func<long> _getCurrentPtsMs;
    private readonly Func<string> _getSourceKey;
    private readonly Func<string> _getRunUuid;
    private readonly Func<long> _getRunStartedUtcMs;
    private readonly Func<bool> _isRunning;
    private readonly TcnLabelWriter _writer;
    private readonly ITcnPredictionProvider? _predictionProvider;
    private readonly IUiTimer _timer;
    private int _currentIndex = -1;
    private long? _currentStartMs;

    public ActionLabelViewModel(
        IEnumerable<FsmStepDefinition> steps,
        TcnLabelWriter writer,
        Func<long> getCurrentPtsMs,
        Func<string> getSourceKey,
        Func<string> getRunUuid,
        Func<long> getRunStartedUtcMs,
        Func<bool> isRunning,
        IUiTimerFactory timerFactory,
        ITcnPredictionProvider? predictionProvider = null)
    {
        _writer = writer;
        _getCurrentPtsMs = getCurrentPtsMs;
        _getSourceKey = getSourceKey;
        _getRunUuid = getRunUuid;
        _getRunStartedUtcMs = getRunStartedUtcMs;
        _isRunning = isRunning;
        _timer = timerFactory.CreatePeriodic(TimeSpan.FromMilliseconds(250), UpdateHeader);
        _predictionProvider = predictionProvider;
        Steps = new ObservableCollection<ActionLabelStep>(
            steps.Select(def => new ActionLabelStep
            {
                Step = def.Step,
                Name = def.Name,
                TcnLabel = def.TcnLabel
            }));

        UpdateHeader();
        _timer.Start();
    }

    [ObservableProperty]
    private bool isManualEnabled = true;

    [ObservableProperty]
    private ObservableCollection<ActionLabelStep> steps;

    [ObservableProperty]
    private string currentSourceText = "-";

    [ObservableProperty]
    private string currentPtsText = "-";

    [ObservableProperty]
    private string currentStepText = "-";

    [ObservableProperty]
    private string modeText = "Manual Labeling";

    [ObservableProperty]
    private string lastActionText = "-";

    [RelayCommand]
    private void SelectStep(ActionLabelStep? step)
    {
        if (step == null)
        {
            return;
        }

        if (!_isRunning())
        {
            LastActionText = "Playback not started. Cannot write labels.";
            return;
        }

        var sourceKey = _getSourceKey();
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            LastActionText = "Video source not detected.";
            return;
        }

        var runUuid = _getRunUuid();
        if (string.IsNullOrWhiteSpace(runUuid))
        {
            LastActionText = "Inference run not detected. Cannot write labels.";
            return;
        }

        var now = _getCurrentPtsMs();
        if (_currentIndex >= 0 && ReferenceEquals(step, Steps[_currentIndex]))
        {
            return;
        }

        CloseCurrentSegment(sourceKey, runUuid, _getRunStartedUtcMs(), now);
        StartSegment(step, now);
    }

    [RelayCommand]
    private void NextStep()
    {
        if (Steps.Count == 0)
        {
            return;
        }

        var nextIndex = _currentIndex < 0 ? 0 : Math.Min(_currentIndex + 1, Steps.Count - 1);
        SelectStep(Steps[nextIndex]);
    }

    [RelayCommand]
    private void FinishCurrent()
    {
        if (!_isRunning())
        {
            return;
        }

        var sourceKey = _getSourceKey();
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return;
        }

        var runUuid = _getRunUuid();
        if (string.IsNullOrWhiteSpace(runUuid))
        {
            return;
        }

        var now = _getCurrentPtsMs();
        CloseCurrentSegment(sourceKey, runUuid, _getRunStartedUtcMs(), now);
        ClearActive();
    }

    partial void OnIsManualEnabledChanged(bool value)
    {
        ModeText = value ? "Manual Labeling" : "TCN / Default";
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
    }

    private void UpdateHeader()
    {
        var sourceKey = _getSourceKey();
        CurrentSourceText = string.IsNullOrWhiteSpace(sourceKey) ? "-" : PrettySource(sourceKey);
        CurrentPtsText = FormatPts(_getCurrentPtsMs());
    }

    private static string PrettySource(string sourceKey)
    {
        if (sourceKey.StartsWith("camera:", StringComparison.OrdinalIgnoreCase))
        {
            return sourceKey;
        }

        return Path.GetFileName(sourceKey);
    }

    private void StartSegment(ActionLabelStep step, long now)
    {
        _currentIndex = Steps.IndexOf(step);
        _currentStartMs = now;
        foreach (var s in Steps)
        {
            s.IsActive = false;
        }

        step.IsActive = true;
        CurrentStepText = step.DisplayText;
    }

    private void CloseCurrentSegment(string sourceKey, string runUuid, long runStartedUtcMs, long now)
    {
        if (_currentIndex < 0 || !_currentStartMs.HasValue)
        {
            return;
        }

        var start = _currentStartMs.Value;
        if (now <= start)
        {
            return;
        }

        var step = Steps[_currentIndex];
        var label = ResolveLabel(step, out var sourceType, out var score);
        int? stepIndex = IsManualEnabled ? step.Step : null;
        var entry = new TcnLabelEntry(
            runUuid,
            runStartedUtcMs,
            sourceKey,
            stepIndex,
            label,
            start,
            now,
            sourceType,
            score);

        if (_writer.TryEnqueue(entry))
        {
            LastActionText = $"{sourceType} | {label} | {FormatPts(start)} -> {FormatPts(now)}";
        }
        else
        {
            LastActionText = "Write queue is full. Label dropped.";
        }
    }

    private string ResolveLabel(ActionLabelStep step, out string sourceType, out float? score)
    {
        score = null;
        if (IsManualEnabled)
        {
            sourceType = "manual";
            return string.IsNullOrWhiteSpace(step.TcnLabel) ? step.Name : step.TcnLabel;
        }

        if (_predictionProvider != null && _predictionProvider.TryGetCurrent(out var label, out var prob))
        {
            sourceType = "tcn";
            score = prob;
            return label;
        }

        sourceType = "default";
        return "unknown";
    }

    private void ClearActive()
    {
        _currentIndex = -1;
        _currentStartMs = null;
        foreach (var s in Steps)
        {
            s.IsActive = false;
        }

        CurrentStepText = "-";
    }

    private static string FormatPts(long ms)
    {
        if (ms < 0)
        {
            return "-";
        }

        var ts = TimeSpan.FromMilliseconds(ms);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
        }

        return $"{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
    }
}
