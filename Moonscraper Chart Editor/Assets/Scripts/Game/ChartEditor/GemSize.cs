// See LICENSE in project root for license information.

using UnityEngine;

/// <summary>
/// How big notes are drawn, like Clone Hero's gem size. Set from the left panel; only the look changes.
/// </summary>
public static class GemSize
{
    const string PrefPercent = "Display.GemSizePercent";
    public const int MinPercent = 50, MaxPercent = 200, DefaultPercent = 100;

    static int? s_percent;

    /// <summary>
    /// Goes up every time the size changes, so notes that are already showing know to resize
    /// </summary>
    public static int version { get; private set; }

    public static int percent
    {
        get
        {
            if (!s_percent.HasValue)
                s_percent = Mathf.Clamp(PlayerPrefs.GetInt(PrefPercent, DefaultPercent), MinPercent, MaxPercent);
            return s_percent.Value;
        }
        set
        {
            value = Mathf.Clamp(value, MinPercent, MaxPercent);
            if (value == percent)
                return;

            s_percent = value;
            ++version;
            PlayerPrefs.SetInt(PrefPercent, value);
            PlayerPrefs.Save();
        }
    }

    public static float scale
    {
        get { return percent / 100f; }
    }
}
