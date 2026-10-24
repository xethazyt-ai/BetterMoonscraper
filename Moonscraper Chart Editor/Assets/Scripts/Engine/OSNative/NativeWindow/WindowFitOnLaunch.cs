// See LICENSE in project root for license information.

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Unity opens the window at the screen's full resolution, which with the title bar and taskbar doesn't fit: it hangs
/// off the bottom and side. Once it's open, a window that doesn't fit the screen's free area is maximised instead.
/// A smaller window someone sized themselves is left as it is.
/// </summary>
public class WindowFitOnLaunch : MonoBehaviour
{
    const int SW_MAXIMIZE = 3;
    const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder name, int count);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int command);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        GameObject runner = new GameObject("Window Fit On Launch");
        DontDestroyOnLoad(runner);
        runner.AddComponent<WindowFitOnLaunch>();
    }

    IEnumerator Start()
    {
        // Give Unity a moment to finish sizing the window itself
        for (int i = 0; i < 5; ++i)
            yield return null;

        try
        {
            if (!Screen.fullScreen)
                FitWindow();
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't fit the window to the screen. " + e.Message);
        }

        Destroy(gameObject);
    }

    static void FitWindow()
    {
        IntPtr window = FindOwnWindow();
        if (window == IntPtr.Zero || IsZoomed(window))
            return;

        RECT rect;
        MONITORINFO monitor = new MONITORINFO();
        monitor.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetWindowRect(window, out rect) || !GetMonitorInfo(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref monitor))
            return;

        // A few pixels over is just the invisible resize border Windows puts around windows
        const int Slack = 16;
        RECT work = monitor.rcWork;
        bool fits = rect.right - rect.left <= work.right - work.left + Slack && rect.bottom - rect.top <= work.bottom - work.top + Slack
            && rect.top >= work.top - Slack && rect.bottom <= work.bottom + Slack;
        if (!fits)
            ShowWindow(window, SW_MAXIMIZE);
    }

    // This process's Unity window
    static IntPtr FindOwnWindow()
    {
        uint ourProcess = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
        IntPtr found = IntPtr.Zero;
        System.Text.StringBuilder className = new System.Text.StringBuilder(64);
        EnumWindows((hWnd, lParam) =>
        {
            uint process;
            GetWindowThreadProcessId(hWnd, out process);
            if (process != ourProcess || !IsWindowVisible(hWnd))
                return true;

            className.Length = 0;
            GetClassName(hWnd, className, className.Capacity);
            if (className.ToString() != "UnityWndClass")
                return true;

            found = hWnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}

#endif
