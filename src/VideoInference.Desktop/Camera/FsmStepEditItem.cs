using CommunityToolkit.Mvvm.ComponentModel;

namespace VideoInferenceDemo;

public partial class FsmStepEditItem : ObservableObject
{
    [ObservableProperty]
    private int step;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string? actionCode;

    [ObservableProperty]
    private string? tcnLabel;
}
