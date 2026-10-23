// See LICENSE in project root for license information.

using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// A chat box in the bottom left corner during a Duet session. Press Enter to write, Enter to send, Escape to cancel.
/// New messages show for a few seconds and then fade, so they don't cover the highway. Also shows who's typing.
/// </summary>
public class DuetChat : MonoBehaviour
{
    const string InputName = "DuetChatInput";
    const float Width = 380;
    const float Margin = 12;
    const float BottomMargin = 70;
    const int MaxLinesShown = 8;
    const double ShowSeconds = 12;
    const double FadeSeconds = 2;

    static DuetChat s_instance;

    ChartEditor m_editor;
    string m_text = string.Empty;
    bool m_open = false;
    bool m_focusRequested = false;
    int m_openedFrame = -1;
    Rect m_area;        // In screen pixels, y down, for blocking clicks to the highway
    GUIStyle m_lineStyle;
    GUIStyle m_hintStyle;
    Texture2D m_background;

    // True while writing a message, so the editor's shortcuts leave the keys alone
    public static bool isTyping { get { return s_instance != null && s_instance.m_open; } }

    public static bool IsPointerOver()
    {
        if (s_instance == null || DuetPanel.activeSession == null)
            return false;

        Vector2 pointer = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
        return s_instance.m_area.Contains(pointer);
    }

    void Start()
    {
        s_instance = this;
        m_editor = ChartEditor.Instance;

        m_background = new Texture2D(1, 1);
        m_background.SetPixel(0, 0, new Color(0, 0, 0, 0.6f));
        m_background.Apply();
    }

    void Update()
    {
        DuetSession session = DuetPanel.activeSession;
        if (session == null || session.state != DuetSession.State.Connected)
        {
            if (m_open)
                Close();
            m_area = new Rect();
            return;
        }

        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        if (enter && !m_open && !Services.IsTyping && m_editor.currentState == ChartEditor.State.Editor)
            Open();
    }

    void Open()
    {
        m_open = true;
        m_focusRequested = true;
        m_openedFrame = Time.frameCount;
    }

    void Close()
    {
        m_open = false;
        m_focusRequested = false;
        GUIUtility.keyboardControl = 0;
    }

    void OnGUI()
    {
        DuetSession session = DuetPanel.activeSession;
        if (session == null || session.state != DuetSession.State.Connected)
            return;

        if (m_lineStyle == null)
            MakeStyles();

        float scale = DuetPanel.GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        GUI.depth = 5;      // Above cursors, below the Duet window

        Event e = Event.current;
        float bottom = Screen.height / scale - BottomMargin;
        float y = bottom;

        // The line to type in, or a hint to press Enter
        const float InputHeight = 24;
        y -= InputHeight;
        Rect inputRect = new Rect(Margin, y, Width, InputHeight);
        if (m_open)
        {
            if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == InputName && Time.frameCount != m_openedFrame)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    session.SendChat(m_text);
                    m_text = string.Empty;
                    Close();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    Close();
                    e.Use();
                }
            }

            if (m_open)
            {
                GUI.SetNextControlName(InputName);
                m_text = GUI.TextField(inputRect, m_text, DuetSession.MaxChatLength);
                if (m_focusRequested)
                {
                    GUI.FocusControl(InputName);
                    m_focusRequested = false;
                }
                else if (e.type == EventType.MouseDown && !inputRect.Contains(e.mousePosition))
                {
                    Close();    // Clicked somewhere else
                }
            }
        }
        else if (GUI.Button(inputRect, "Press Enter to chat", m_hintStyle))
        {
            Open();
            m_openedFrame = -1;
        }

        // Who's typing
        string typing = TypingText(session);
        if (typing != null)
        {
            GUIContent content = new GUIContent(typing);
            float height = m_hintStyle.CalcHeight(content, Width);
            y -= height + 2;
            GUI.Label(new Rect(Margin, y, Width, height), content, m_hintStyle);
        }

        // Recent messages, newest at the bottom. All of them while writing, otherwise only new ones, fading out.
        double now = session.clockSeconds;
        int shown = 0;
        Color oldColor = GUI.color;
        for (int i = session.chat.Count - 1; i >= 0 && shown < MaxLinesShown; --i)
        {
            DuetChatLine line = session.chat[i];
            double age = now - line.time;
            if (!m_open && age > ShowSeconds)
                break;

            GUIContent content = new GUIContent(FormatLine(session, line));
            float height = m_lineStyle.CalcHeight(content, Width);
            y -= height + 2;

            float alpha = m_open ? 1f : Mathf.Clamp01((float)((ShowSeconds - age) / FadeSeconds));
            GUI.color = new Color(1, 1, 1, alpha);
            GUI.Label(new Rect(Margin, y, Width, height), content, m_lineStyle);
            ++shown;
        }
        GUI.color = oldColor;

        m_area = new Rect(Margin * scale, y * scale, Width * scale, (bottom - y) * scale);
    }

    string TypingText(DuetSession session)
    {
        List<string> names = new List<string>();
        foreach (KeyValuePair<int, DuetPresence> pair in session.presences)
        {
            if (pair.Value.isTyping)
            {
                DuetUser user = session.users.Find(u => u.id == pair.Key);
                names.Add(user != null ? user.name : "Someone");
            }
        }

        if (names.Count == 0)
            return null;
        if (names.Count == 1)
            return names[0] + " is typing...";
        return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1] + " are typing...";
    }

    static string FormatLine(DuetSession session, DuetChatLine line)
    {
        Color color = Color.white;
        DuetPresence presence;
        if (line.userId == session.localUserId)
            color = DuetPresence.GetColor(DuetPanel.localColor);
        else if (session.presences.TryGetValue(line.userId, out presence))
            color = DuetPresence.GetColor(presence.color);

        return "<b><color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + Escape(line.name) + "</color></b>: " + Escape(line.text);
    }

    // Stops anything typed from being read as formatting
    static string Escape(string text)
    {
        StringBuilder escaped = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            escaped.Append(c);
            if (c == '<')
                escaped.Append('​');
        }
        return escaped.ToString();
    }

    void MakeStyles()
    {
        m_lineStyle = new GUIStyle(GUI.skin.label);
        m_lineStyle.richText = true;
        m_lineStyle.wordWrap = true;
        m_lineStyle.fontSize = 14;
        m_lineStyle.normal.textColor = Color.white;
        m_lineStyle.normal.background = m_background;
        m_lineStyle.padding = new RectOffset(6, 6, 3, 3);

        m_hintStyle = new GUIStyle(m_lineStyle);
        m_hintStyle.richText = false;
        m_hintStyle.fontStyle = FontStyle.Italic;
        m_hintStyle.fontSize = 12;
        m_hintStyle.normal.textColor = new Color(1, 1, 1, 0.7f);
        m_hintStyle.hover.background = m_background;
        m_hintStyle.hover.textColor = Color.white;
        m_hintStyle.active.background = m_background;
        m_hintStyle.active.textColor = Color.white;
    }
}
