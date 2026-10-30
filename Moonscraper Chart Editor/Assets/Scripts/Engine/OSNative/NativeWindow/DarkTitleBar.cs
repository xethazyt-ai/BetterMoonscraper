// See LICENSE in project root for license information.

using System;
using System.Runtime.InteropServices;

/// <summary>
/// Turns the Windows title bar dark or light, to go with the Dark theme. Windows 10 (1809 and later) and 11 only;
/// elsewhere it does nothing.
/// </summary>
public static class DarkTitleBar
{
    const string PrefTheme = "Customize.Theme";                 // UITheme's, 1 = Dark
    const string PrefLastColor = "Customize.TitleBarColor";     // The last color the title bar was given, as 0xRRGGBB

    /// <summary>
    /// Puts the title bar back how it was last time as early as Unity allows, so it's already the theme's color while
    /// the splash screen and the first menu load, instead of turning from white to dark once everything is in.
    /// </summary>
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyLastLook()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        Set(UnityEngine.PlayerPrefs.GetInt(PrefTheme, 0) == 1);
        if (UnityEngine.PlayerPrefs.HasKey(PrefLastColor))
        {
            int rgb = UnityEngine.PlayerPrefs.GetInt(PrefLastColor);
            SetColor(new UnityEngine.Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255), false);
        }
#endif
    }

    public static void Set(bool dark)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (QuitState.isQuitting)
            return;     // The window is going away
        try
        {
            IntPtr window = WindowFitOnLaunch.FindOwnWindow();
            if (window == IntPtr.Zero)
                return;

            int value = dark ? 1 : 0;
            // 20 on Windows 10 20H1 and later and Windows 11, 19 on the 1809-1909 builds
            if (DwmSetWindowAttribute(window, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(window, DwmUseImmersiveDarkModeOld, ref value, sizeof(int));

            // Redraw the frame so it changes now rather than on the next focus change
            SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("Couldn't change the title bar: " + e.Message);
        }
#endif
    }

    /// <summary>
    /// Colors the title bar itself (Windows 11 only), or puts Windows' own color back with null. False when Windows
    /// can't (Windows 10), so the title bar keeps Windows' own black or white.
    /// </summary>
    public static bool SetColor(UnityEngine.Color? color)
    {
        return SetColor(color, true);
    }

    static bool SetColor(UnityEngine.Color? color, bool remember)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (QuitState.isQuitting)
            return true;
        try
        {
            IntPtr window = WindowFitOnLaunch.FindOwnWindow();
            if (window == IntPtr.Zero)
                return true;

            int caption = DwmColorDefault, text = DwmColorDefault;
            if (color.HasValue)
            {
                UnityEngine.Color32 c = color.Value;
                caption = c.r | (c.g << 8) | (c.b << 16);    // COLORREF is 0x00BBGGRR
                // Dark writing on a light bar (the Light theme), light writing on a dark one
                float luminance = 0.2126f * color.Value.r + 0.7152f * color.Value.g + 0.0722f * color.Value.b;
                text = luminance > 0.5f ? 0x101010 : 0xF0F0F0;
            }
            bool colored = DwmSetWindowAttribute(window, DwmCaptionColor, ref caption, sizeof(int)) == 0;
            DwmSetWindowAttribute(window, DwmTextColor, ref text, sizeof(int));
            if (remember)
            {
                if (colored && color.HasValue)
                {
                    UnityEngine.Color32 c = color.Value;
                    UnityEngine.PlayerPrefs.SetInt(PrefLastColor, (c.r << 16) | (c.g << 8) | c.b);
                }
                else
                    UnityEngine.PlayerPrefs.DeleteKey(PrefLastColor);
            }
            return colored;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("Couldn't color the title bar: " + e.Message);
            return false;
        }
#else
        return true;
#endif
    }

    /// <summary>
    /// The color Windows draws a title bar it can't recolor (Windows 10), or null when it uses the accent color.
    /// Dark title bars go grey while another window has focus.
    /// </summary>
    public static UnityEngine.Color? WindowsTitleBarColor(bool dark, bool focused)
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        try
        {
            using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
            {
                object prevalence = key != null ? key.GetValue("ColorPrevalence") : null;
                if (prevalence is int && (int)prevalence != 0)
                    return null;    // "Show accent color on title bars" is on
            }
        }
        catch (Exception)
        {
        }
#endif
        if (!dark)
            return UnityEngine.Color.white;
        return focused ? UnityEngine.Color.black : new UnityEngine.Color(43 / 255f, 43 / 255f, 43 / 255f);
    }

    /// <summary>True once the title bar always looks focused (see KeepFocusedLook).</summary>
    public static bool keepsFocusedLook { get; private set; }

    /// <summary>
    /// Windows 10 greys a title bar out while another window (such as one on a second monitor) has focus, which it
    /// can't be told not to do with a color. This tells Windows the window is always active as far as drawing the title
    /// bar goes, so it stays the same color as the menu bar under it. Focus itself works as usual.
    /// </summary>
    public static void KeepFocusedLook()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        if (keepsFocusedLook || QuitState.isQuitting)
            return;
        try
        {
            IntPtr window = WindowFitOnLaunch.FindOwnWindow();
            if (window == IntPtr.Zero)
                return;

            s_window = window;
            s_newProc = TitleBarWindowProc;     // Kept in a field so it isn't garbage collected while Windows calls it
            s_oldProc = SetWindowLongPtr(window, GwlWndProc, Marshal.GetFunctionPointerForDelegate(s_newProc));
            if (s_oldProc == IntPtr.Zero)
                return;
            keepsFocusedLook = true;
            UnityEngine.Application.quitting += StopKeepingFocusedLook;

            // Redraw the title bar as focused now
            SendMessage(window, WmNcActivate, (IntPtr)1, IntPtr.Zero);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("Couldn't keep the title bar's color: " + e.Message);
        }
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    delegate IntPtr WindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    static IntPtr s_window;
    static IntPtr s_oldProc;
    static WindowProc s_newProc;

    const int GwlWndProc = -4;
    const uint WmNcActivate = 0x0086;

    [AOT.MonoPInvokeCallback(typeof(WindowProc))]
    static IntPtr TitleBarWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmNcActivate)
            wParam = (IntPtr)1;     // Draw the title bar as focused either way
        return CallWindowProc(s_oldProc, hWnd, message, wParam, lParam);
    }

    // Puts Unity's own window procedure back before the scripting side shuts down
    static void StopKeepingFocusedLook()
    {
        UnityEngine.Application.quitting -= StopKeepingFocusedLook;
        if (!keepsFocusedLook)
            return;
        try
        {
            SetWindowLongPtr(s_window, GwlWndProc, s_oldProc);
        }
        catch (Exception)
        {
        }
        keepsFocusedLook = false;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] static extern IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    const int DwmCaptionColor = 35, DwmTextColor = 36;
    const int DwmColorDefault = unchecked((int)0xFFFFFFFF);
    const int DwmUseImmersiveDarkMode = 20, DwmUseImmersiveDarkModeOld = 19;
    const uint SwpNoSize = 0x1, SwpNoMove = 0x2, SwpNoZOrder = 0x4, SwpNoActivate = 0x10, SwpFrameChanged = 0x20;

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
#endif
}
