// See LICENSE in project root for license information.

using UnityEngine;

/// <summary>
/// Which download this is: the version plus, for GitHub builds, the commit it was built from (Resources/BuildId.txt,
/// written by the build workflow). Collab Mode uses it to warn when people in a session have different builds.
/// </summary>
public static class BuildInfo
{
    public static string id { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Load()
    {
        string commit = string.Empty;
        TextAsset file = Resources.Load<TextAsset>("BuildId");
        if (file)
        {
            commit = file.text.Trim();
            if (commit.Length > 7)
                commit = commit.Substring(0, 7);
        }

        id = "v" + Application.version + (commit.Length > 0 ? " " + commit : string.Empty);
        DuetSession.buildId = id;
    }
}
