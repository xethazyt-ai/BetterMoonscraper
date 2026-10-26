// See LICENSE in project root for license information.

using UnityEngine;

/// <summary>
/// Whether BetterMoonscraper is closing for real (Application.quitting, which, unlike OnApplicationQuit, doesn't
/// happen when the quit is cancelled). Things that would rebuild or redraw stop once it's set.
/// </summary>
public static class QuitState
{
    public static bool isQuitting { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        isQuitting = false;
        Application.quitting += () => isQuitting = true;     // Registered before anything else, so it's set first
    }
}
