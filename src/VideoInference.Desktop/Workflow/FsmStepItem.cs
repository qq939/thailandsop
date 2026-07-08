using System;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoInferenceDemo;

public partial class FsmStepItem : ObservableObject
{
    private readonly long _displayClockStartedAt = Stopwatch.GetTimestamp();
    private readonly DateTimeOffset _displayClockStartedUtc = DateTimeOffset.UtcNow;

    public static FsmStepItem FromSnapshot(FsmStepSnapshot snapshot, DateTimeOffset? timelineOriginUtc = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new FsmStepItem
        {
            Step = snapshot.Step,
            Name = snapshot.Name,
            ActionCode = snapshot.ActionCode,
            TcnLabel = snapshot.TcnLabel,
            ExpectedStateCode = snapshot.ExpectedStateCode,
            StartTimeUtc = snapshot.StartTimeUtc,
            EndTimeUtc = snapshot.EndTimeUtc,
            Duration = snapshot.Duration,
            IsNg = snapshot.IsNg,
            TimelineOriginUtc = timelineOriginUtc,
            Status = snapshot.Status
        };
    }

    public static DateTimeOffset? ResolveTimelineOrigin(IEnumerable<FsmStepSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var ordered = snapshots.OrderBy(snapshot => snapshot.Step).ToArray();
        return ordered.FirstOrDefault(snapshot => snapshot.Step == 1)?.StartTimeUtc
               ?? ordered.FirstOrDefault(snapshot => snapshot.StartTimeUtc.HasValue)?.StartTimeUtc;
    }

    public FsmStepSnapshot ToSnapshot()
    {
        return new FsmStepSnapshot(
            Step,
            Name,
            ActionCode,
            TcnLabel,
            ExpectedStateCode,
            Status,
            StartTimeUtc,
            EndTimeUtc,
            Duration,
            IsNg);
    }

    [ObservableProperty]
    private int step;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? actionCode;

    [ObservableProperty]
    private string? tcnLabel;

    [ObservableProperty]
    private string? expectedStateCode;

    [ObservableProperty]
    private FsmStepStatus status = FsmStepStatus.Waiting;

    [ObservableProperty]
    private DateTimeOffset? startTimeUtc;

    [ObservableProperty]
    private DateTimeOffset? endTimeUtc;

    [ObservableProperty]
    private TimeSpan? duration;

    [ObservableProperty]
    private bool isNg;

    [ObservableProperty]
    private DateTimeOffset? timelineOriginUtc;

    public string StatusText => Status switch
    {
        FsmStepStatus.Waiting => "等待",
        FsmStepStatus.InProgress => "进行中",
        FsmStepStatus.Done => "完成",
        _ => "未知"
    };

    public string CardBackground => IsNg
        ? "#FFF1F0"
        : Status switch
        {
            FsmStepStatus.Done => "#ECF8EF",
            FsmStepStatus.InProgress => "#F8FBFF",
            _ => "#F7F9FC"
        };

    public string CardBorderBrush => IsNg
        ? "#F4B8B2"
        : Status switch
        {
            FsmStepStatus.Done => "#B7E4C7",
            FsmStepStatus.InProgress => "#4D93DA",
            _ => "#D7E0EA"
        };

    public string StepBadgeBackground => Status switch
    {
        FsmStepStatus.Done => "#2E7D4F",
        FsmStepStatus.InProgress => "#233B5D",
        _ => "#91A0AF"
    };

    public string StatusBadgeBackground => IsNg
        ? "#FFE2DF"
        : Status switch
        {
            FsmStepStatus.Done => "#DFF4E7",
            FsmStepStatus.InProgress => "#E7F0FB",
            _ => "#EDF2F7"
        };

    public string StatusBadgeForeground => IsNg
        ? "#A83228"
        : Status switch
        {
            FsmStepStatus.Done => "#1C6B3F",
            FsmStepStatus.InProgress => "#245B94",
            _ => "#607080"
        };

    public bool IsExpanded => Status != FsmStepStatus.Waiting;

    public string TimelineTimeText => TryGetTimelineTime(out var timelineTime)
        ? FormatTimeline(timelineTime)
        : "-";

    public string EndTimeText => EndTimeUtc.HasValue
        ? EndTimeUtc.Value.ToLocalTime().ToString("HH:mm:ss")
        : "-";

    public string DurationText => TryGetDuration(out var duration)
        ? FormatTimeline(duration)
        : "-";

    public string CurrentStepTimeText => TryGetCurrentStepTime(out var currentStepTime)
        ? FormatTimeline(currentStepTime)
        : "-";

    public void RefreshLiveTimes()
    {
        if (Status != FsmStepStatus.InProgress)
        {
            return;
        }

        OnPropertyChanged(nameof(TimelineTimeText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(CurrentStepTimeText));
    }

    partial void OnStatusChanged(FsmStepStatus value)
    {
        if (value == FsmStepStatus.InProgress && !StartTimeUtc.HasValue)
        {
            StartTimeUtc = DateTimeOffset.UtcNow;
        }

        if (value == FsmStepStatus.Done)
        {
            EndTimeUtc ??= DateTimeOffset.UtcNow;
            if (StartTimeUtc.HasValue && EndTimeUtc.HasValue)
            {
                Duration = EndTimeUtc.Value - StartTimeUtc.Value;
            }
        }

        RaiseDerived();
    }

    partial void OnStartTimeUtcChanged(DateTimeOffset? value) => RaiseDerived();
    partial void OnEndTimeUtcChanged(DateTimeOffset? value) => RaiseDerived();
    partial void OnDurationChanged(TimeSpan? value) => RaiseDerived();
    partial void OnIsNgChanged(bool value) => RaiseDerived();
    partial void OnTimelineOriginUtcChanged(DateTimeOffset? value) => RaiseDerived();

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CardBackground));
        OnPropertyChanged(nameof(CardBorderBrush));
        OnPropertyChanged(nameof(StepBadgeBackground));
        OnPropertyChanged(nameof(StatusBadgeBackground));
        OnPropertyChanged(nameof(StatusBadgeForeground));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(TimelineTimeText));
        OnPropertyChanged(nameof(EndTimeText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(CurrentStepTimeText));
    }

    private bool TryGetTimelineTime(out TimeSpan timelineTime)
    {
        timelineTime = TimeSpan.Zero;
        if (!TimelineOriginUtc.HasValue)
        {
            return false;
        }

        var pointUtc = Status switch
        {
            FsmStepStatus.InProgress => GetDisplayClockUtc(),
            FsmStepStatus.Done => EndTimeUtc,
            _ => null
        };

        if (!pointUtc.HasValue)
        {
            return false;
        }

        timelineTime = ClampPositive(pointUtc.Value - TimelineOriginUtc.Value);
        return true;
    }

    private bool TryGetDuration(out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (Status == FsmStepStatus.InProgress && StartTimeUtc.HasValue)
        {
            value = ClampPositive(GetDisplayClockUtc() - StartTimeUtc.Value);
            return true;
        }

        if (Duration.HasValue)
        {
            value = ClampPositive(Duration.Value);
            return true;
        }

        if (StartTimeUtc.HasValue && EndTimeUtc.HasValue)
        {
            value = ClampPositive(EndTimeUtc.Value - StartTimeUtc.Value);
            return true;
        }

        return false;
    }

    private bool TryGetCurrentStepTime(out TimeSpan value)
    {
        value = TimeSpan.Zero;
        if (Status == FsmStepStatus.InProgress && StartTimeUtc.HasValue)
        {
            value = ClampPositive(GetDisplayClockUtc() - StartTimeUtc.Value);
            return true;
        }

        if (Status == FsmStepStatus.Done && Duration.HasValue)
        {
            value = ClampPositive(Duration.Value);
            return true;
        }

        if (Status == FsmStepStatus.Done && StartTimeUtc.HasValue && EndTimeUtc.HasValue)
        {
            value = ClampPositive(EndTimeUtc.Value - StartTimeUtc.Value);
            return true;
        }

        if (!StartTimeUtc.HasValue)
        {
            return false;
        }

        return false;
    }

    private DateTimeOffset GetDisplayClockUtc()
    {
        return _displayClockStartedUtc + Stopwatch.GetElapsedTime(_displayClockStartedAt);
    }

    private static TimeSpan ClampPositive(TimeSpan value)
    {
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    private static string FormatTimeline(TimeSpan value)
    {
        value = ClampPositive(value);
        var milliseconds = value.Milliseconds;
        var seconds = value.Seconds;
        if (value.TotalMinutes >= 1)
        {
            return $"{(int)value.TotalMinutes:00}:{seconds:00}:{milliseconds:000}";
        }

        return $"{(int)value.TotalSeconds:00}:{milliseconds:000}";
    }
}
