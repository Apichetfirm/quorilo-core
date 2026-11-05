using System; using System.Threading; using System.Windows.Threading;
// Named Windows events wake or stop the existing widget without a polling timer.
public sealed class CreditMonitorWindowSignal : IDisposable
{
    private readonly EventWaitHandle signal;
    private readonly RegisteredWaitHandle registration;
    private int disposed;
    public CreditMonitorWindowSignal(string name, Dispatcher dispatcher, Action callback)
    {
        signal = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        registration = ThreadPool.RegisterWaitForSingleObject(signal, (state, timedOut) => {
            if (Volatile.Read(ref disposed) != 0 || dispatcher.HasShutdownStarted) return;
            try { dispatcher.BeginInvoke(new Action(() => { if (Volatile.Read(ref disposed) == 0) callback(); })); }
            catch (InvalidOperationException) { }
        }, null, Timeout.Infinite, false);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        registration.Unregister(null);
        signal.Dispose();
    }
}
