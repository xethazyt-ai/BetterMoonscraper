// See LICENSE in project root for license information.

using UnityEngine;

/// <summary>
/// The speed checkboxes in the left panel, which also decide how a chart exported at another speed sounds.
/// </summary>
public static class SpeedOptions
{
    const string PrefNonSpacedSlowdown = "Speed.NonSpacedSlowdown";
    const string PrefExportSuffix = "Export.SpeedSuffix";

    static bool? s_nonSpacedSlowdown;

    /// <summary>
    /// Keeps the notes at their 1x spacing at any song speed (they just scroll slower or faster), instead of spreading
    /// them out or packing them together. Good for previewing a chart at another speed.
    /// </summary>
    public static bool nonSpacedSlowdown
    {
        get
        {
            if (!s_nonSpacedSlowdown.HasValue)
                s_nonSpacedSlowdown = PlayerPrefs.GetInt(PrefNonSpacedSlowdown, 0) != 0;
            return s_nonSpacedSlowdown.Value;
        }
        set
        {
            s_nonSpacedSlowdown = value;
            PlayerPrefs.SetInt(PrefNonSpacedSlowdown, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Speed changes change the pitch too, like a record, both slowed down and sped up. Otherwise the pitch stays the
    /// same. This is Moonscraper's "Slowdown Pitch Correction" setting turned around, so the two always agree.
    /// </summary>
    public static bool pitchedSpeedups
    {
        get { return !Globals.gameSettings.slowdownPitchCorrectionEnabled; }
        set { Globals.gameSettings.slowdownPitchCorrectionEnabled.value = !value; }
    }

    /// <summary>
    /// Exporting at another speed adds "(0.75x Speed)" to the song's name
    /// </summary>
    public static bool exportSpeedSuffix
    {
        get { return PlayerPrefs.GetInt(PrefExportSuffix, 1) != 0; }
        set { PlayerPrefs.SetInt(PrefExportSuffix, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    public static string SpeedText(float speed)
    {
        return speed.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "x";
    }
}
