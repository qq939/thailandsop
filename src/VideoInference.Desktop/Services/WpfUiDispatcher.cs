using System;
using System.Windows.Threading;

namespace VideoInferenceDemo;

public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfUiDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Post(Action action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        _dispatcher.BeginInvoke(action, DispatcherPriority.Normal);
    }

    public void Render(Action action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        _dispatcher.BeginInvoke(action, DispatcherPriority.Render);
    }
}
