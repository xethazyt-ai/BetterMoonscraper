// See LICENSE in project root for license information.

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runs at 30 frames a second while Moonscraper isn't the focused window and isn't playing, so it stops using a whole
/// CPU core and GPU in the background (easier on a laptop's battery and fans). Everything keeps running, Collab
/// Mode included. The frame rate picked in Settings comes back as soon as the window is focused again.
/// </summary>
public class BackgroundFrameRate : MonoBehaviour
{
    const int BackgroundFps = 30;

    /// <summary>The saved frame rate meaning VSync: frames in step with the monitor, with no cap of its own.</summary>
    public const int VSync = 0;

    static int? s_userFrameRate;

    /// <summary>
    /// The frame rate picked in Settings, even while the background rate is in use. Save this, not
    /// Application.targetFrameRate.
    /// </summary>
    public static int userFrameRate
    {
        get { return s_userFrameRate ?? (QualitySettings.vSyncCount > 0 ? VSync : Application.targetFrameRate); }
    }

    /// <summary>
    /// Uses the frame rate picked in Settings: 60, 120 or 240, VSync, or anything else for no limit
    /// </summary>
    public static void ApplyUserFrameRate(int frameRate)
    {
        if (s_userFrameRate.HasValue)
        {
            s_userFrameRate = frameRate;    // Picked while throttled in the background; used once focused again
            return;
        }
        Set(frameRate);
    }

    static void Set(int frameRate)
    {
        if (frameRate == VSync)
        {
            QualitySettings.vSyncCount = 1;
            Application.targetFrameRate = -1;
        }
        else
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = frameRate == 60 || frameRate == 120 || frameRate == 240 ? frameRate : -1;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        GameObject holder = new GameObject("Background Frame Rate");
        DontDestroyOnLoad(holder);
        holder.AddComponent<BackgroundFrameRate>();
    }

    void OnApplicationFocus(bool focused)
    {
        if (focused)
            Restore();
        else
            Throttle();
    }

    void Update()
    {
        // Previewing in the background keeps full speed, so the audio and highway stay smooth
        if (s_userFrameRate.HasValue && IsPlaying())
            Restore();
        else if (!s_userFrameRate.HasValue && !Application.isFocused && !IsPlaying())
            Throttle();
    }

    void OnApplicationQuit()
    {
        Restore();
    }

    static void Throttle()
    {
        if (s_userFrameRate.HasValue || IsPlaying())
            return;

        int current = userFrameRate;
        if (current > 0 && current <= BackgroundFps)
            return;     // Already as slow or slower

        s_userFrameRate = current;
        QualitySettings.vSyncCount = 0;     // VSync would ignore the slower rate
        Application.targetFrameRate = BackgroundFps;
    }

    static void Restore()
    {
        if (!s_userFrameRate.HasValue)
            return;

        int frameRate = s_userFrameRate.Value;
        s_userFrameRate = null;
        Set(frameRate);
    }

    static bool IsPlaying()
    {
        return ChartEditor.InstanceExists && ChartEditor.Instance.currentState == ChartEditor.State.Playing;
    }
}
