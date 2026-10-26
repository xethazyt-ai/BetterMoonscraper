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

    static int? s_userFrameRate;

    /// <summary>
    /// The frame rate picked in Settings, even while the background rate is in use. Save this, not
    /// Application.targetFrameRate.
    /// </summary>
    public static int userFrameRate
    {
        get { return s_userFrameRate ?? Application.targetFrameRate; }
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

        int current = Application.targetFrameRate;
        if (current > 0 && current <= BackgroundFps)
            return;     // Already as slow or slower

        s_userFrameRate = current;
        Application.targetFrameRate = BackgroundFps;
    }

    static void Restore()
    {
        if (!s_userFrameRate.HasValue)
            return;

        Application.targetFrameRate = s_userFrameRate.Value;
        s_userFrameRate = null;
    }

    static bool IsPlaying()
    {
        return ChartEditor.InstanceExists && ChartEditor.Instance.currentState == ChartEditor.State.Playing;
    }
}
