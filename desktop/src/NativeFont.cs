using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CreditMonitor
{
    // Register bundled fonts with GDI as well as GDI+, only for this process.
    internal sealed class NativeFont : IDisposable
    {
        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint size, IntPtr reserved, ref uint count);
        [DllImport("gdi32.dll")]
        private static extern bool RemoveFontMemResourceEx(IntPtr handle);
        public IntPtr Memory { get; private set; }
        private IntPtr handle;
        public NativeFont(byte[] bytes)
        {
            Memory = Marshal.AllocCoTaskMem(bytes.Length);
            Marshal.Copy(bytes, 0, Memory, bytes.Length);
            uint count = 0;
            handle = AddFontMemResourceEx(Memory, (uint)bytes.Length, IntPtr.Zero, ref count);
            if (handle == IntPtr.Zero) { Dispose(); throw new Win32Exception("Cannot load the bundled font."); }
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { RemoveFontMemResourceEx(handle); handle = IntPtr.Zero; }
            if (Memory != IntPtr.Zero) { Marshal.FreeCoTaskMem(Memory); Memory = IntPtr.Zero; }
        }
    }
}
