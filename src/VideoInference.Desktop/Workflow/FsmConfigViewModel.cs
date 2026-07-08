using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo;

public sealed partial class FsmConfigViewModel : ObservableObject
{
    private readonly string _cameraConfigPath;

    public FsmConfigViewModel(string cameraConfigPath, IEnumerable<FsmStepDefinition> steps)
    {
        _cameraConfigPath = cameraConfigPath;
        Steps = new ObservableCollection<FsmStepDefinition>(
            steps.Select(s => new FsmStepDefinition
            {
                Step = s.Step,
                Name = s.Name,
                ActionCode = s.ActionCode,
                TcnLabel = s.TcnLabel,
                ExpectedStateCode = s.ExpectedStateCode
            }));

        if (Steps.Count == 0)
        {
            Steps.Add(new FsmStepDefinition { Step = 1, Name = "步骤 1" });
        }
    }

    public ObservableCollection<FsmStepDefinition> Steps { get; }

    [ObservableProperty]
    private FsmStepDefinition? selectedStep;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    [RelayCommand]
    private void AddStep()
    {
        var nextStep = Steps.Count + 1;
        var def = new FsmStepDefinition { Step = nextStep, Name = $"步骤 {nextStep}" };
        Steps.Add(def);
        SelectedStep = def;
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedStep == null)
        {
            return;
        }

        Steps.Remove(SelectedStep);
        SelectedStep = null;
        NormalizeSteps();
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedStep == null)
        {
            return;
        }

        var index = Steps.IndexOf(SelectedStep);
        if (index <= 0)
        {
            return;
        }

        Steps.Move(index, index - 1);
        NormalizeSteps();
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedStep == null)
        {
            return;
        }

        var index = Steps.IndexOf(SelectedStep);
        if (index < 0 || index >= Steps.Count - 1)
        {
            return;
        }

        Steps.Move(index, index + 1);
        NormalizeSteps();
    }

    public bool TrySave()
    {
        ErrorMessage = string.Empty;
        NormalizeSteps();

        if (Steps.Any(s => string.IsNullOrWhiteSpace(s.Name)))
        {
            ErrorMessage = "请填写每一个步骤的名称。";
            return false;
        }

        try
        {
            var profile = new SopProfile
            {
                Id = "sop-default",
                Name = "默认组装 SOP",
                Strategy = AnalysisStrategyNames.SopRules,
                Steps = Steps.ToList()
            };
            CameraSettingsStorage.SaveSopProfiles(_cameraConfigPath, new[] { ToCameraSopProfile(profile) });
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败: {ex.Message}";
            return false;
        }
    }

    private void NormalizeSteps()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].Step = i + 1;
        }
    }

    private static CameraSopProfile ToCameraSopProfile(SopProfile profile)
    {
        return new CameraSopProfile
        {
            Id = profile.Id,
            Name = profile.Name,
            Strategy = profile.Strategy,
            FingerprintModuleId = profile.FingerprintModuleId,
            Steps = profile.Steps
                .Select(step => new CameraSopStep
                {
                    Step = step.Step,
                    Name = step.Name,
                    ActionCode = step.ActionCode,
                    TcnLabel = step.TcnLabel,
                    ExpectedStateCode = step.ExpectedStateCode
                })
                .ToList()
        };
    }
}
