using System;

namespace VideoInferenceDemo;

public static class AnalysisStrategyFactory
{
    public static IAnalysisStrategy Create(AnalysisConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.Equals(config.Strategy, AnalysisStrategyNames.BasicDistance, StringComparison.OrdinalIgnoreCase))
        {
            return new BasicDistanceAnalysisStrategy();
        }

        if (string.Equals(config.Strategy, AnalysisStrategyNames.Sop1, StringComparison.OrdinalIgnoreCase))
        {
            return new Sop1AnalysisStrategy();
        }

        if (string.Equals(config.Strategy, AnalysisStrategyNames.Sop2, StringComparison.OrdinalIgnoreCase))
        {
            return new Sop2AnalysisStrategy();
        }

        return new SopRuleAnalysisStrategy();
    }
}
