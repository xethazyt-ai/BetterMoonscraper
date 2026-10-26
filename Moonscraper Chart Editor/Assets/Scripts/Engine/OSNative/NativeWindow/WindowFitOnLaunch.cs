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
/// Before that, the window always opens at 1280x720 (16:9) in the middle, rather than whatever size Unity remembered,
/// and coming back from maximised at nearly the whole screen's size goes back to that too.
/// </summary>
public class WindowFitOnLaunch : MonoBehaviour
{
    const int SW_MAXIMIZE = 3, SW_RESTORE = 9;
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
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
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
        Application.quitting += RememberLaunchSize;
    }

    // Unity opens the window (and shows its splash screen) at the size it saved when it last closed, which is the whole
    // screen after being maximised. Once BetterMoonscraper has closed and Unity has saved, a hidden PowerShell puts
    // 1280x720 in its place, so the next launch (and Unity's splash screen) opens at that size straight away.
    static void RememberLaunchSize()
    {
        try
        {
            int width, height;
            IntPtr window = FindOwnWindow();
            if (window == IntPtr.Zero || !GetLaunchSize(window, out width, out height))
                return;

            string key = @"HKCU:\Software\" + Application.companyName + @"\" + Application.productName;
            string script = string.Format(
                "Wait-Process -Id {0} -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 300; " +
                "$k = '{1}'; if (Test-Path $k) {{ $p = Get-ItemProperty $k; foreach ($n in $p.PSObject.Properties.Name) {{ " +
                "if ($n -like 'Screenmanager Resolution Width*' -or $n -like 'Screenmanager Resolution Window Width*') {{ Set-ItemProperty $k $n {2} -Type DWord }} " +
                "elseif ($n -like 'Screenmanager Resolution Height*' -or $n -like 'Screenmanager Resolution Window Height*') {{ Set-ItemProperty $k $n {3} -Type DWord }} " +
                "elseif ($n -like 'Screenmanager Resolution Use Native*') {{ Set-ItemProperty $k $n 0 -Type DWord }} " +
                "elseif ($n -like 'Screenmanager Fullscreen mode*') {{ Set-ItemProperty $k $n 3 -Type DWord }} " +
                "elseif ($n -like 'Screenmanager Is Fullscreen mode*') {{ Set-ItemProperty $k $n 0 -Type DWord }} }} }}",
                System.Diagnostics.Process.GetCurrentProcess().Id, key.Replace("'", "''"), width, height);

            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" + script.Replace("\"", "\\\"") + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            System.Diagnostics.Process.Start(info);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't save the launch window size. " + e.Message);
        }
    }

    // Always 1280x720 inside (16:9), unless the screen is too small for that
    public const int LaunchWidth = 1280, LaunchHeight = 720;

    static bool GetLaunchSize(IntPtr window, out int width, out int height)
    {
        width = LaunchWidth;
        height = LaunchHeight;
        MONITORINFO monitor = new MONITORINFO();
        monitor.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetMonitorInfo(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref monitor))
            return true;

        int workWidth = monitor.rcWork.right - monitor.rcWork.left, workHeight = monitor.rcWork.bottom - monitor.rcWork.top;
        if (width > workWidth * 0.95f || height > workHeight * 0.9f)
        {
            float fit = Mathf.Min(workWidth * 0.95f / width, workHeight * 0.9f / height);
            width = Mathf.RoundToInt(width * fit);
            height = Mathf.RoundToInt(height * fit);
        }
        return width > 0 && height > 0;
    }

    IEnumerator Start()
    {
        // Give Unity a moment to finish sizing the window itself, and check again once loading has settled
        for (int i = 0; i < 5; ++i)
            yield return null;

        // However Unity remembered the last run (fullscreen, maximised), it opens as a plain window first
        if (Screen.fullScreen)
        {
            Debug.Log(string.Format("Window fit: opened fullscreen ({0}), switching to a window", Screen.fullScreenMode));
            Screen.SetResolution(LaunchWidth, LaunchHeight, FullScreenMode.Windowed);
            for (int i = 0; i < 5; ++i)
                yield return null;
        }
        try
        {
            IntPtr opened = FindOwnWindow();
            if (opened != IntPtr.Zero && IsZoomed(opened))
            {
                Debug.Log("Window fit: opened maximised, restoring");
                ShowWindow(opened, SW_RESTORE);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't restore the window. " + e.Message);
        }
        for (int i = 0; i < 3; ++i)
            yield return null;

        if (TrySetLaunchSize())
        {
            for (int i = 0; i < 3; ++i)
                yield return null;
            TryFitWindow(true);
        }
        TryFitWindow();

        yield return new WaitForSecondsRealtime(1.5f);

        // Unity sometimes puts its remembered size back while loading: size it again if so
        int launchWidth, launchHeight;
        IntPtr check = FindOwnWindow();
        if (check != IntPtr.Zero && !Screen.fullScreen && !IsZoomed(check) && GetLaunchSize(check, out launchWidth, out launchHeight)
            && (Mathf.Abs(Screen.width - launchWidth) > 4 || Mathf.Abs(Screen.height - launchHeight) > 4))
        {
            Debug.Log(string.Format("Window fit: still {0}x{1} after loading, sizing again", Screen.width, Screen.height));
            ResizeToLaunchSize(check);
            yield return null;
        }
        TryFitWindow();

        // Keeps running, to catch the window coming back from maximised at nearly the whole screen's size
        m_window = FindOwnWindow();
        m_wasZoomed = m_window != IntPtr.Zero && IsZoomed(m_window);
        m_watching = true;
    }

    IntPtr m_window;
    bool m_watching, m_wasZoomed;
    float m_nextCheck;

    void Update()
    {
        if (!m_watching || m_window == IntPtr.Zero || Time.unscaledTime < m_nextCheck)
            return;
        m_nextCheck = Time.unscaledTime + 0.2f;

        try
        {
            bool zoomed = IsZoomed(m_window);
            if (m_wasZoomed && !zoomed && !Screen.fullScreen && IsNearlyWholeScreen(m_window))
            {
                Debug.Log("Window fit: back from maximised at the whole screen's size, so back to the launch size");
                ResizeToLaunchSize(m_window);
            }
            m_wasZoomed = zoomed;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't watch the window size. " + e.Message);
            m_watching = false;
        }
    }

    static bool IsNearlyWholeScreen(IntPtr window)
    {
        RECT rect;
        MONITORINFO monitor = new MONITORINFO();
        monitor.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetWindowRect(window, out rect) || !GetMonitorInfo(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref monitor))
            return false;
        RECT work = monitor.rcWork;
        return rect.right - rect.left > (work.right - work.left) * 0.9f || rect.bottom - rect.top > (work.bottom - work.top) * 0.9f;
    }

    // Sizes the inside of the window to the launch size and centres it in the screen's free area, with Windows itself
    // (Unity's Screen.SetResolution doesn't always resize a window that isn't fullscreen)
    static bool ResizeToLaunchSize(IntPtr window)
    {
        int width, height;
        RECT rect, client, visible;
        MONITORINFO monitor = new MONITORINFO();
        monitor.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        if (!GetLaunchSize(window, out width, out height) || !GetWindowRect(window, out rect) || !GetClientRect(window, out client)
            || !GetMonitorInfo(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref monitor))
            return false;
        if (DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out visible, Marshal.SizeOf(typeof(RECT))) != 0)
            visible = rect;

        // The frame around the inside (title bar, borders), and the invisible resize border outside what's seen
        int outerWidth = width + (rect.right - rect.left) - (client.right - client.left);
        int outerHeight = height + (rect.bottom - rect.top) - (client.bottom - client.top);
        int insetLeft = visible.left - rect.left, insetTop = visible.top - rect.top;
        int insetRight = rect.right - visible.right, insetBottom = rect.bottom - visible.bottom;
        int seenWidth = outerWidth - insetLeft - insetRight, seenHeight = outerHeight - insetTop - insetBottom;

        RECT work = monitor.rcWork;
        int x = work.left + (work.right - work.left - seenWidth) / 2 - insetLeft;
        int y = work.top + Math.Max(0, (work.bottom - work.top - seenHeight) / 2) - insetTop;
        Debug.Log(string.Format("Window fit: resizing to {0}x{1} inside, at {2},{3}", width, height, x, y));
        return SetWindowPos(window, IntPtr.Zero, x, y, outerWidth, outerHeight, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // Sizes the inside of the window to the launch size and centres it; true if it was resized
    static bool TrySetLaunchSize()
    {
        try
        {
            if (Screen.fullScreen)
                return false;

            IntPtr window = FindOwnWindow();
            if (window == IntPtr.Zero || IsZoomed(window))
                return false;

            int width, height;
            if (!GetLaunchSize(window, out width, out height))
                return false;

            Debug.Log(string.Format("Window fit: launch size {0}x{1} (was {2}x{3})", width, height, Screen.width, Screen.height));
            ResizeToLaunchSize(window);
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
