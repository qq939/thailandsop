using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoInferenceDemo;

public sealed partial class PerformanceDiagnosticsViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _mainViewModel;
    private readonly ReadOnlyObservableCollection<CameraSessionViewModel> _sessions;

    public PerformanceDiagnosticsViewModel(MainViewModel mainViewModel)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _sessions = new ReadOnlyObservableCollection<CameraSessionViewModel>(_mainViewModel.CameraSessions);
        _mainViewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        _mainViewModel.CameraSessions.CollectionChanged += OnCameraSessionsCollectionChanged;

        SelectedSession = _mainViewModel.SelectedCameraSession ?? _mainViewModel.CameraSessions.FirstOrDefault();
    }

    public ReadOnlyObservableCollection<CameraSessionViewModel> Sessions => _sessions;

    [ObservableProperty]
    private CameraSessionViewModel? selectedSession;

    public string SummaryText => SelectedSession == null
        ? "当前没有可观测的 session。"
        : $"{SelectedSession.Name} | {SelectedSession.SourceModeText} | {SelectedSession.UiRunStateText}";

    public string DeviceText => SelectedSession?.InferenceDeviceText ?? "-";

    public string LastErrorText => string.IsNullOrWhiteSpace(SelectedSession?.LastError)
        ? "无"
        : SelectedSession.LastError;

    partial void OnSelectedSessionChanged(CameraSessionViewModel? oldValue, CameraSessionViewModel? newValue)
    {
        if (oldValue != null)
        {
            oldValue.PropertyChanged -= OnSelectedSessionPropertyChanged;
        }

        if (newValue != null)
        {
            newValue.PropertyChanged += OnSelectedSessionPropertyChanged;
        }

        RaiseSelectedSessionProjectionChanged();
    }

    public void Dispose()
    {
        _mainViewModel.PropertyChanged -= OnMainViewModelPropertyChanged;
        _mainViewModel.CameraSessions.CollectionChanged -= OnCameraSessionsCollectionChanged;

        if (SelectedSession != null)
        {
            SelectedSession.PropertyChanged -= OnSelectedSessionPropertyChanged;
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedCameraSession))
        {
            SelectedSession = _mainViewModel.SelectedCameraSession ?? SelectedSession;
        }
    }

    private void OnCameraSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (SelectedSession != null && _mainViewModel.CameraSessions.Contains(SelectedSession))
        {
            return;
        }

        SelectedSession = _mainViewModel.SelectedCameraSession ?? _mainViewModel.CameraSessions.FirstOrDefault();
    }

    private void OnSelectedSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CameraSessionViewModel.Name) ||
            e.PropertyName == nameof(CameraSessionViewModel.SourceModeText) ||
            e.PropertyName == nameof(CameraSessionViewModel.UiRunStateText) ||
            e.PropertyName == nameof(CameraSessionViewModel.InferenceDeviceText) ||
            e.PropertyName == nameof(CameraSessionViewModel.LastError))
        {
            RaiseSelectedSessionProjectionChanged();
        }
    }

    private void RaiseSelectedSessionProjectionChanged()
    {
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(DeviceText));
        OnPropertyChanged(nameof(LastErrorText));
    }
}
