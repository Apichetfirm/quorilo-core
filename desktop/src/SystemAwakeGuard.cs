using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CreditMonitor
{
    // Windows execution requirements belong to the calling thread. The hub UI
    // thread owns this guard for its whole lifetime, including while in the tray.
    internal sealed class SystemAwakeGuard : IDisposable
    {
        const uint Continuous = 0x80000000, SystemRequired = 0x00000001;
        readonly int ownerThread = Thread.CurrentThread.ManagedThreadId;
        bool disposed;
        public bool Enabled { get; private set; }
        [DllImport("kernel32.dll", EntryPoint="SetThreadExecutionState")]
        static extern uint SetThreadExecutionState(uint flags);
        public bool SetEnabled(bool enabled)
        {
            if(disposed)throw new ObjectDisposedException("SystemAwakeGuard");
            CheckThread();
            // Do not request DISPLAY_REQUIRED: the monitor does not keep the screen on.
            if(SetThreadExecutionState(Continuous | (enabled ? SystemRequired : 0)) == 0)return false;
            Enabled=enabled;return true;
        }
        void CheckThread()
        {
            if(Thread.CurrentThread.ManagedThreadId != ownerThread)
                throw new InvalidOperationException("Execution state must be changed on its owning UI thread.");
        }
        public void Dispose()
        {
            if(disposed)return;
            CheckThread();
            if(Enabled)SetThreadExecutionState(Continuous);
            Enabled=false;disposed=true;
            // Windows also removes the requirement automatically when this thread exits.
        }
    }
}
