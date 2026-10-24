// See LICENSE in project root for license information.

using UnityEngine;

/// <summary>
/// The red notice at the top of the screen telling joiners not to leave while a host that crashed comes back.
/// </summary>
public static class DuetHostLostNotice
{
    static GUIStyle s_style;

    public static void Draw(double waitedSeconds, float scale)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        if (s_style == null)
        {
            Texture2D background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.55f, 0.05f, 0.05f, 0.92f));
            background.Apply();
            s_style = new GUIStyle(GUI.skin.box);
            s_style.normal.background = background;
            s_style.normal.textColor = Color.white;
            s_style.fontStyle = FontStyle.Bold;
            s_style.fontSize = 16;
            s_style.alignment = TextAnchor.MiddleCenter;
            s_style.wordWrap = true;
            s_style.padding = new RectOffset(12, 12, 8, 8);
        }

        int waited = Mathf.FloorToInt((float)waitedSeconds);
        string text = "THE HOST DISCONNECTED. DON'T LEAVE!\nYou'll be reconnected as soon as they're back, and your copy of the chart keeps everything. Waiting "
            + (waited / 60) + ":" + (waited % 60).ToString("00");

        Matrix4x4 matrix = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        float width = Mathf.Min(560, Screen.width / scale - 40);
        Rect rect = new Rect((Screen.width / scale - width) / 2, DuetMenuCover.menuBarBottom / scale + 10, width, 64);
        if (!DuetMenuCover.Covers(rect, scale))
            GUI.Box(rect, text, s_style);
        GUI.matrix = matrix;
    }
}
