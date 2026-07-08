using System;

namespace VideoInferenceDemo;

public interface IUiTimer : IDisposable
{
    void Start();

    void Stop();
}
