using System;
using System.Runtime.InteropServices;

namespace IronbarkCoop
{
    // Moves this game's own window (testing two copies side by side on one PC). Windows only; does nothing elsewhere.
    internal static class WindowPlacer
    {
        delegate bool EnumProc(IntPtr window, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        public static void Place(int x, int y)
        {
            try
            {
                uint self = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
                EnumWindows((window, data) =>
                {
                    GetWindowThreadProcessId(window, out uint owner);
                    if (owner != self || !IsWindowVisible(window)) return true;
                    // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
                    SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, 0x0001 | 0x0004 | 0x0010);
                    return false;
                }, IntPtr.Zero);
            }
            catch (Exception ex) { Plugin.Log("WINDOW_PLACE_FAILED " + ex.Message); }
        }
    }
}
