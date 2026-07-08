using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VideoInferenceDemo;

public sealed partial class SopProfileEditItem : ObservableObject
{
    private readonly SopProfile _source;

    public SopProfileEditItem(SopProfile source)
    {
        _source = source.Clone();
        name = _source.Name;
        strategy = _source.Strategy;
        fingerprintModuleId = _source.FingerprintModuleId;
        if (_source.Steps != null)
        {
            foreach (var step in _source.Steps.OrderBy(s => s.Step))
            {
                Steps.Add(new FsmStepDefinition
                {
                    Step = step.Step,
                    Name = step.Name,
                    ActionCode = step.ActionCode,
                    TcnLabel = step.TcnLabel,
                    ExpectedStateCode = step.ExpectedStateCode
                });
            }
        }

        if (Steps.Count == 0)
        {
            Steps.Add(new FsmStepDefinition { Step = 1, Name = "步骤 1" });
        }
    }

    public string Id => _source.Id;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string strategy = AnalysisStrategyNames.SopRules;

    [ObservableProperty]
    private string fingerprintModuleId = string.Empty;

    public ObservableCollection<FsmStepDefinition> Steps { get; } = new();

    public SopProfile ToSopProfile()
    {
        NormalizeSteps();
        return new SopProfile
        {
            Id = Id,
            Name = Name.Trim(),
            Strategy = Strategy.Trim(),
            FingerprintModuleId = string.IsNullOrWhiteSpace(FingerprintModuleId) ? string.Empty : FingerprintModuleId.Trim(),
            Steps = Steps.Select(s => new FsmStepDefinition
            {
                Step = s.Step,
                Name = s.Name?.Trim() ?? string.Empty,
                ActionCode = s.ActionCode?.Trim(),
                TcnLabel = s.TcnLabel?.Trim(),
                ExpectedStateCode = s.ExpectedStateCode
            }).ToList()
        };
    }

    public void NormalizeSteps()
    {
        for (var i = 0; i < Steps.Count; i++)
        {
            Steps[i].Step = i + 1;
        }
    }
}

public sealed partial class SopConfigViewModel : ObservableObject
{
    private readonly Action<IReadOnlyList<CameraSopProfile>>? _saveProfiles;

    public SopConfigViewModel(
        IReadOnlyList<SopProfile> existingProfiles,
        IReadOnlyList<FingerprintModuleOptions>? fingerprintModules = null,
        Action<IReadOnlyList<CameraSopProfile>>? saveProfiles = null)
    {
        _saveProfiles = saveProfiles;
        FingerprintModuleOptions.Add(FingerprintModuleOptionItem.Unbound);
        foreach (var module in (fingerprintModules ?? Array.Empty<FingerprintModuleOptions>())
                 .Select(item => item.Normalize())
                 .OrderBy(item => item.Name))
        {
            FingerprintModuleOptions.Add(new FingerprintModuleOptionItem(
                module.Id,
                string.IsNullOrWhiteSpace(module.Name)
                    ? module.Id
                    : $"{module.Name} ({module.Id})"));
        }

        foreach (var profile in existingProfiles ?? Array.Empty<SopProfile>())
        {
            Profiles.Add(new SopProfileEditItem(profile));
        }

        SelectedProfile = Profiles.FirstOrDefault();
    }

    public ObservableCollection<SopProfileEditItem> Profiles { get; } = new();
    public ObservableCollection<FingerprintModuleOptionItem> FingerprintModuleOptions { get; } = new();
    public IReadOnlyList<string> StrategyOptions { get; } = new[]
    {
        AnalysisStrategyNames.SopRules,
        AnalysisStrategyNames.Sop1,
        AnalysisStrategyNames.Sop2,
        AnalysisStrategyNames.BasicDistance
    };

    [ObservableProperty]
    private SopProfileEditItem? selectedProfile;

    [ObservableProperty]
    private string errorMessage = string.Empty;

    public bool HasSelectedProfile => SelectedProfile != null;

    partial void OnSelectedProfileChanged(SopProfileEditItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedProfile));
        RemoveProfileCommand.NotifyCanExecuteChanged();
        AddStepCommand.NotifyCanExecuteChanged();
        RemoveStepCommand.NotifyCanExecuteChanged();
        MoveStepUpCommand.NotifyCanExecuteChanged();
        MoveStepDownCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddProfile()
    {
        var profile = new SopProfileEditItem(new SopProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "新建 SOP",
            Strategy = AnalysisStrategyNames.SopRules,
            Steps = new List<FsmStepDefinition>
            {
                new() { Step = 1, Name = "步骤 1" }
            }
        });
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private void RemoveProfile()
    {
        if (SelectedProfile == null) return;

        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private void AddStep()
    {
        if (SelectedProfile == null) return;

        var maxStep = SelectedProfile.Steps.Count > 0
            ? SelectedProfile.Steps.Max(s => s.Step)
            : 0;
        SelectedProfile.Steps.Add(new FsmStepDefinition
        {
            Step = maxStep + 1,
            Name = $"步骤 {maxStep + 1}"
        });
        SelectedProfile.NormalizeSteps();
    }

    [RelayCommand(CanExecute = nameof(CanRemoveStep))]
    private void RemoveStep(FsmStepDefinition? step)
    {
        if (SelectedProfile == null || step == null) return;

        SelectedProfile.Steps.Remove(step);
        SelectedProfile.NormalizeSteps();
    }

    private bool CanRemoveStep(FsmStepDefinition? step) => step != null && HasSelectedProfile;

    [RelayCommand(CanExecute = nameof(CanMoveStepUp))]
    private void MoveStepUp(FsmStepDefinition? step)
    {
        if (SelectedProfile == null || step == null) return;

        var index = SelectedProfile.Steps.IndexOf(step);
        if (index <= 0) return;

        SelectedProfile.Steps.Move(index, index - 1);
        SelectedProfile.NormalizeSteps();
    }

    private bool CanMoveStepUp(FsmStepDefinition? step) => step != null && HasSelectedProfile;

    [RelayCommand(CanExecute = nameof(CanMoveStepDown))]
    private void MoveStepDown(FsmStepDefinition? step)
    {
        if (SelectedProfile == null || step == null) return;

        var index = SelectedProfile.Steps.IndexOf(step);
        if (index < 0 || index >= SelectedProfile.Steps.Count - 1) return;

        SelectedProfile.Steps.Move(index, index + 1);
        SelectedProfile.NormalizeSteps();
    }

    private bool CanMoveStepDown(FsmStepDefinition? step) => step != null && HasSelectedProfile;

    public bool TrySave()
    {
        ErrorMessage = string.Empty;

        // Validate
        foreach (var profile in Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                ErrorMessage = $"SOP 配置 '{profile.Id}' 的名称为空。";
                SelectedProfile = profile;
                return false;
            }

            if (profile.Steps.Any(s => string.IsNullOrWhiteSpace(s.Name)))
            {
                ErrorMessage = $"SOP 配置 '{profile.Name}' 中存在名称为空的步骤。";
                SelectedProfile = profile;
                return false;
            }
        }

        foreach (var profile in Profiles)
        {
            if (!string.IsNullOrWhiteSpace(profile.FingerprintModuleId) &&
                FingerprintModuleOptions.All(item =>
                    !string.Equals(item.Id, profile.FingerprintModuleId, StringComparison.OrdinalIgnoreCase)))
            {
                ErrorMessage = $"SOP 配置 '{profile.Name}' 绑定的指纹模块不存在。";
                SelectedProfile = profile;
                return false;
            }
        }

        try
        {
            var profiles = Profiles.Select(p => ToCameraSopProfile(p.ToSopProfile())).ToList();
            _saveProfiles?.Invoke(profiles);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败: {ex.Message}";
            return false;
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

public sealed record FingerprintModuleOptionItem(string Id, string DisplayName)
{
    public static FingerprintModuleOptionItem Unbound { get; } = new(string.Empty, "未绑定");
}
