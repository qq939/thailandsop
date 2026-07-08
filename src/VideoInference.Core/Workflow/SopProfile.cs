using System.Collections.Generic;

namespace VideoInferenceDemo;

public sealed class SopProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New SOP";
    public string Strategy { get; set; } = AnalysisStrategyNames.SopRules;
    public string FingerprintModuleId { get; set; } = string.Empty;
    public List<FsmStepDefinition> Steps { get; set; } = new();

    public SopProfile Clone()
    {
        return new SopProfile
        {
            Id = Id,
            Name = Name,
            Strategy = Strategy,
            FingerprintModuleId = FingerprintModuleId,
            Steps = Steps.ConvertAll(s => new FsmStepDefinition
            {
                Step = s.Step,
                Name = s.Name,
                ActionCode = s.ActionCode,
                TcnLabel = s.TcnLabel,
                ExpectedStateCode = s.ExpectedStateCode
            })
        };
    }
}
