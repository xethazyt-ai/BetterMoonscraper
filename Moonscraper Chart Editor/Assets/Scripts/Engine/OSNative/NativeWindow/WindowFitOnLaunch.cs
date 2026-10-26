// See LICENSE in project root for license information.

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Unity opens the window at the screen's full resolution, or where it was last left, and it can hang off the side
/// or under the taskbar. Once it's open, a window too big for the screen's free area is maximised, and one that fits
/// but sticks out is moved back to the middle. A window that's already fully on screen is left as it is.
/// Before that, a window that isn't maximised opens at a normal 16:9 size (three quarters of the screen's height),
/// in the middle, rather than whatever odd size Unity remembered.
/// </summary>
public class WindowFitOnLaunch : MonoBehaviour
{
    const int SW_MAXIMIZE = 3;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

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
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out RECT value, int size);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        GameObject runner = new GameObject("Window Fit On Launch");
        DontDestroyOnLoad(runner);
        runner.AddComponent<WindowFitOnLaunch>();
    }

    IEnumerator Start()
    {
        // Give Unity a moment to finish sizing the window itself, and check again once loading has settled
        for (int i = 0; i < 5; ++i)
            yield return null;

        if (TrySetLaunchSize())
        {
            for (int i = 0; i < 3; ++i)
                yield return null;
            TryFitWindow(true);
        }
        TryFitWindow();

        yield return new WaitForSecondsRealtime(1.5f);
        TryFitWindow();

        Destroy(gameObject);
    }

    const float LaunchHeightOfScreen = 0.75f;

    // Sizes the inside of the window to 16:9, three quarters of the screen's free height; true if it was resized
    static bool TrySetLaunchSize()
    {
        try
        {
            if (Screen.fullScreen)
                return false;

            IntPtr window = FindOwnWindow();
            if (window == IntPtr.Zero || IsZoomed(window))
                return false;

            MONITORINFO monitor = new MONITORINFO();
            monitor.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref monitor))
                return false;

            int workWidth = monitor.rcWork.right - monitor.rcWork.left, workHeight = monitor.rcWork.bottom - monitor.rcWork.top;
            int height = Mathf.RoundToInt(workHeight * LaunchHeightOfScreen);
            int width = Mathf.RoundToInt(height * 16f / 9f);
            if (width > workWidth * 0.9f)
            {
                width = Mathf.RoundToInt(workWidth * 0.9f);
                height = Mathf.RoundToInt(width * 9f / 16f);
            }

            Debug.Log(string.Format("Window fit: launch size {0}x{1} (was {2}x{3})", width, height, Screen.width, Screen.height));
            if (Mathf.Abs(Screen.width - width) <= 2 && Mathf.Abs(Screen.height - height) <= 2)
                return true;   // Already that size, just centre it
            Screen.SetResolution(width, height, FullScreenMode.Windowed);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't size the window. " + e.Message);
            return false;
        }
    }

    static void TryFitWindow(bool centre = false)
    {
        try
        {
            if (!Screen.fullScreen)
                FitWindow(centre);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't fit the window to the screen. " + e.Message);
        }
    }

    static void FitWindow(bool centre)
    {
        IntPtr window = FindOwnWindow();
        if (window == IntPtr.Zero)
        {
            Debug.Log("Window fit: couldn't find Moonscraper's window");
            return;
        }
        if (IsZoomed(window))
            return;

        RECT rect;
        MONITORINFO monitor = new MONITORINFO();
        monitor.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetWindowRect(window, out rect) || !GetMonitorInfo(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref monitor))
            return;

        // The part you can see: the window's rect also holds an invisible resize border on three sides
        RECT visible;
        if (DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out visible, Marshal.SizeOf(typeof(RECT))) != 0)
            visible = rect;

        const int Slack = 2;
        RECT work = monitor.rcWork;
        int width = visible.right - visible.left, height = visible.bottom - visible.top;
        int workWidth = work.right - work.left, workHeight = work.bottom - work.top;
        Debug.Log(string.Format("Window fit: window {0},{1} {2}x{3}, screen's free area {4},{5} {6}x{7}",
            visible.left, visible.top, width, height, work.left, work.top, workWidth, workHeight));
        if (width > workWidth + Slack || height > workHeight + Slack)
        {
            ShowWindow(window, SW_MAXIMIZE);
            return;
        }

        bool onScreen = visible.left >= work.left - Slack && visible.top >= work.top - Slack
            && visible.right <= work.right + Slack && visible.bottom <= work.bottom + Slack;
        if (!onScreen || centre)
        {
            // Centre it in the free area, keeping its size
            int moveX = work.left + (workWidth - width) / 2 - visible.left;
            int moveY = work.top + (workHeight - height) / 2 - visible.top;
            SetWindowPos(window, IntPtr.Zero, rect.left + moveX, rect.top + moveY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    // This process's Unity window
    internal static IntPtr FindOwnWindow()
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
