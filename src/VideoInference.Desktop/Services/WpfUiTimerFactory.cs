using System;
using System.Windows.Threading;

namespace VideoInferenceDemo;

public sealed class WpfUiTimerFactory : IUiTimerFactory
{
    private readonly Dispatcher _dispatcher;

    public WpfUiTimerFactory(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public IUiTimer CreatePeriodic(TimeSpan interval, Action tick)
    {
        return new WpfUiTimer(_dispatcher, interval, tick);
    }

    private sealed class WpfUiTimer : IUiTimer
    {
        private readonly DispatcherTimer _timer;

        public WpfUiTimer(Dispatcher dispatcher, TimeSpan interval, Action tick)
        {
            if (tick == null)
            {
                throw new ArgumentNullException(nameof(tick));
            }

            _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = interval
            };
            _timer.Tick += (_, _) => tick();
        }

        public void Start() => _timer.Start();

        public void Stop() => _timer.Stop();

        public void Dispose()
        {
            _timer.Stop();
        }
    }
}
