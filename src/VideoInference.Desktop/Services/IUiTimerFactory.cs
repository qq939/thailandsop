using System;

namespace VideoInferenceDemo;

public interface IUiTimerFactory
{
    IUiTimer CreatePeriodic(TimeSpan interval, Action tick);
}
