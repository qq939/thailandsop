using System;

namespace VideoInferenceDemo;

public interface IUiDispatcher
{
    void Post(Action action);

    void Render(Action action);
}
