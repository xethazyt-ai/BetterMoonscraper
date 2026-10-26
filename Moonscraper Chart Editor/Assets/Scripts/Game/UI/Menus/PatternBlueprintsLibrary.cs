// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using UnityEngine;
using MoonscraperChartEditor.Song;

/// <summary>
/// The Pattern Library: every saved pattern in a scrolling list, each drawn as a small chart so you can see it before
/// pasting. Time runs left to right with beat lines (brighter on each beat, brightest on each bar of four) and one
/// row per lane, green at the top. The picked pattern is drawn as it would paste, with Fit to step and Speed applied.
/// Opened with the Library button in Pattern Blueprints.
/// </summary>
public partial class PatternBlueprints
{
    const int LibraryWindowId = 0x4251;
    const float PreviewHeight = 74;

    readonly DuetWindow m_libraryWindow = new DuetWindow(LibraryWindowId, "PatternLibrary", new Rect(0, 0, 560, 460), new Vector2(380, 240), true, false);
    bool m_libraryVisible = false;
    Vector2 m_libraryScroll;
    string m_librarySearch = string.Empty;
    int m_libraryPick = -1;
    int m_libraryPaste = -1;
    int m_libraryDelete = -1;

    static Texture2D s_white;
    static Texture2D s_dot;
    static Texture2D s_ring;

    static readonly Color[] s_guitarColors =
    {
        new Color(0.13f, 0.78f, 0.2f), new Color(0.92f, 0.16f, 0.14f), new Color(0.98f, 0.86f, 0.14f),
        new Color(0.16f, 0.5f, 0.98f), new Color(1f, 0.55f, 0.1f),
    };
    static readonly Color s_openColor = new Color(0.72f, 0.33f, 1f);
    static readonly Color s_kickColor = new Color(1f, 0.6f, 0.15f);

    bool IsPointerOverLibrary(Vector2 pointer, float scale)
    {
        return m_libraryVisible && m_libraryWindow.rect.Contains(pointer) && !DuetMenuCover.Covers(m_libraryWindow.rect, scale);
    }

    void DrawLibrary(float scale)
    {
        if (!m_libraryVisible)
            return;

        if (!m_libraryWindow.isPlaced)
            m_libraryWindow.PlaceOnce(new Vector2(Mathf.Max(20, Screen.width / scale / 2 - 280), 80));

        if (DuetMenuCover.Covers(m_libraryWindow.rect, scale))
            return;

        m_libraryWindow.Draw("Pattern Library", DrawLibraryWindow, scale);
    }

    void DrawLibraryWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(52));
        GUI.SetNextControlName("LibrarySearch");
        m_librarySearch = GUILayout.TextField(m_librarySearch, 40);
        if (GUILayout.Button("Close", GUILayout.Width(60)))
            m_libraryVisible = false;
        GUILayout.EndHorizontal();

        GUILayout.Label("Click a pattern to pick it. The picked one shows how it will paste with Fit to step and Speed.");

        m_libraryScroll = GUILayout.BeginScrollView(m_libraryScroll);
        int shown = 0;
        for (int i = 0; i < m_file.blueprints.Count; ++i)
        {
            Blueprint blueprint = m_file.blueprints[i];
            if (m_librarySearch.Trim().Length > 0 && blueprint.name.IndexOf(m_librarySearch.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            ++shown;

            bool picked = i == m_selected;
            int fitStep = picked ? m_fitStep : OriginalStep(blueprint);
            float speed = picked ? m_speed : 1;

            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            GUILayout.Label((picked ? "> " : string.Empty) + blueprint.name + "   " + Describe(blueprint, fitStep, speed)
                + (string.IsNullOrEmpty(blueprint.gameMode) ? string.Empty : "   " + blueprint.gameMode));
            if (GUILayout.Button("Paste", GUILayout.Width(56)))
                m_libraryPaste = i;
            if (GUILayout.Button("X", GUILayout.Width(26)))
                m_libraryDelete = i;
            GUILayout.EndHorizontal();

            Rect area = GUILayoutUtility.GetRect(10, PreviewHeight, GUILayout.ExpandWidth(true));
            if (UnityEngine.Event.current.type == EventType.Repaint)
                DrawPreview(area, blueprint, fitStep, speed, picked);
            else if (UnityEngine.Event.current.type == EventType.MouseDown && area.Contains(UnityEngine.Event.current.mousePosition))
            {
                m_libraryPick = i;
                UnityEngine.Event.current.Use();
            }
            GUILayout.EndVertical();
        }

        if (m_file.blueprints.Count == 0)
            GUILayout.Label("No patterns saved yet. Select some notes, then Save selection in Pattern Blueprints (F9).");
        else if (shown == 0)
            GUILayout.Label("No pattern names match \"" + m_librarySearch.Trim() + "\".");
        GUILayout.EndScrollView();

        ApplyLibraryPending();
    }

    // As in the main window, changes wait until the window has been drawn
    void ApplyLibraryPending()
    {
        if (m_libraryPick >= 0 && m_libraryPick < m_file.blueprints.Count && m_libraryPick != m_selected)
            m_pendingSelect = m_libraryPick;
        m_libraryPick = -1;

        if (m_libraryPaste >= 0 && m_libraryPaste < m_file.blueprints.Count)
        {
            if (m_libraryPaste != m_selected)
            {
                m_selected = m_libraryPaste;
                ResetShape(m_file.blueprints[m_selected]);
            }
            m_pasteRequested = true;    // At the strikeline, since the mouse is on this window
        }
        m_libraryPaste = -1;

        if (m_libraryDelete >= 0)
            m_pendingDelete = m_libraryDelete;
        m_libraryDelete = -1;

        ApplyPending();
    }

    void DrawPreview(Rect area, Blueprint blueprint, int fitStep, float speed, bool picked)
    {
        EnsureTextures();

        int count;
        double span;
        List<Placed> placed = Arrange(blueprint, fitStep, speed, out count, out span);

        Chart.GameMode gameMode = ParseGameMode(blueprint.gameMode);
        int lanes = gameMode == Chart.GameMode.GHLGuitar ? 6 : 5;

        // Fits the whole pattern, sustains included, with room for the last note's circle
        double end = span;
        foreach (Placed note in placed)
            end = Math.Max(end, note.beat + note.lengthBeats);
        end = Math.Max(end, 0.25);

        const float Pad = 10;
        Rect inner = new Rect(area.x + Pad, area.y + 6, area.width - Pad * 2, area.height - 12);
        float rowHeight = inner.height / lanes;
        float dot = Mathf.Clamp(rowHeight * 1.1f, 6, 16);
        Func<double, float> x = beat => inner.x + (float)(beat / end) * inner.width;

        Color old = GUI.color;

        // Background and lane rows
        Fill(area, picked ? new Color(0.05f, 0.12f, 0.2f, 0.95f) : new Color(0.03f, 0.05f, 0.09f, 0.9f));
        for (int lane = 0; lane < lanes; ++lane)
            Fill(new Rect(inner.x, inner.y + rowHeight * (lane + 0.5f), inner.width, 1), new Color(1, 1, 1, 0.08f));

        // Beat lines: one per step, brighter on beats, brightest every four beats
        double gap = 4.0 / Math.Max(1, fitStep) / speed;
        int lines = (int)Math.Min(4000, Math.Floor(end / gap + 1e-6));
        float minSpacing = inner.width / Math.Max(1, lines);
        for (int i = 0; i <= lines; ++i)
        {
            double beat = i * gap;
            bool onBeat = Math.Abs(beat - Math.Round(beat)) < 1e-4;
            bool onBar = onBeat && ((int)Math.Round(beat)) % 4 == 0;
            if (!onBeat && minSpacing < 3)
                continue;   // Too dense to see; beats are still drawn
            float alpha = onBar ? 0.55f : onBeat ? 0.32f : 0.12f;
            Fill(new Rect(x(beat), inner.y, onBar ? 2 : 1, inner.height), new Color(1, 1, 1, alpha));
        }
        for (int beat = 0; beat <= (int)end; ++beat)
        {
            if (lines >= 4000)
                Fill(new Rect(x(beat), inner.y, beat % 4 == 0 ? 2 : 1, inner.height), new Color(1, 1, 1, beat % 4 == 0 ? 0.55f : 0.32f));
        }

        // Sustains first, so notes sit on top of them
        foreach (Placed note in placed)
        {
            if (note.lengthBeats <= 0)
                continue;
            int row;
            Color color;
            bool fullWidth;
            LaneOf(gameMode, note.note.rawNote, lanes, out row, out color, out fullWidth);
            float y = fullWidth ? inner.y + inner.height / 2 : inner.y + rowHeight * (row + 0.5f);
            color.a = 0.75f;
            Fill(new Rect(x(note.beat), y - dot * 0.18f, x(note.beat + note.lengthBeats) - x(note.beat), dot * 0.36f), color);
        }

        foreach (Placed note in placed)
        {
            int row;
            Color color;
            bool fullWidth;
            LaneOf(gameMode, note.note.rawNote, lanes, out row, out color, out fullWidth);
            float nx = x(note.beat);

            if (fullWidth)
            {
                // Open notes and kicks are bars across every lane, as on the highway
                Fill(new Rect(nx - 2, inner.y, 4, inner.height), color);
                continue;
            }

            float y = inner.y + rowHeight * (row + 0.5f);
            Rect circle = new Rect(nx - dot / 2, y - dot / 2, dot, dot);
            bool tap = (note.note.flags & (int)Note.Flags.Tap) != 0;
            bool cymbal = (note.note.flags & (int)Note.Flags.ProDrums_Cymbal) != 0;
            GUI.color = color;
            GUI.DrawTexture(circle, tap || cymbal ? s_ring : s_dot);
            if (tap)
            {
                GUI.color = new Color(1, 1, 1, 0.9f);
                GUI.DrawTexture(new Rect(nx - dot * 0.2f, y - dot * 0.2f, dot * 0.4f, dot * 0.4f), s_dot);
            }
        }

        GUI.color = old;
    }

    // Which row a note sits on (green at the top, as the lanes run left to right on the highway) and its color
    static void LaneOf(Chart.GameMode gameMode, int rawNote, int lanes, out int row, out Color color, out bool fullWidth)
    {
        fullWidth = false;
        row = Mathf.Clamp(rawNote, 0, lanes - 1);

        switch (gameMode)
        {
            case Chart.GameMode.Drums:
                if (rawNote == (int)Note.DrumPad.Kick)
                {
                    fullWidth = true;
                    color = ProfileColor(gameMode, -1, s_kickColor);
                    return;
                }
                // Red, yellow, blue, orange (5-lane), green, as the drum lanes run
                Color[] drums = { s_guitarColors[1], s_guitarColors[2], s_guitarColors[3], s_guitarColors[4], s_guitarColors[0] };
                color = ProfileColor(gameMode, row, drums[row]);
                return;

            case Chart.GameMode.GHLGuitar:
                if (rawNote == (int)Note.GHLiveGuitarFret.Open)
                {
                    fullWidth = true;
                    color = s_openColor;
                    return;
                }
                color = rawNote <= (int)Note.GHLiveGuitarFret.Black3 ? new Color(0.35f, 0.35f, 0.4f) : new Color(0.95f, 0.95f, 0.95f);
                return;

            default:
                if (rawNote == (int)Note.GuitarFret.Open)
                {
                    fullWidth = true;
                    color = s_openColor;
                    return;
                }
                color = ProfileColor(Chart.GameMode.Guitar, row, s_guitarColors[row]);
                return;
        }
    }

    // The picked color profile's lane color when there is one, else Moonscraper's
    static Color ProfileColor(Chart.GameMode gameMode, int lane, Color fallback)
    {
        Color color;
        if (lane >= 0 && CustomNoteColors.TryGetLaneColor(gameMode, 5, lane, out color))
            return color;
        return fallback;
    }

    static Chart.GameMode ParseGameMode(string text)
    {
        Chart.GameMode gameMode;
        return !string.IsNullOrEmpty(text) && Enum.TryParse(text, out gameMode) ? gameMode : Chart.GameMode.Guitar;
    }

    static void Fill(Rect rect, Color color)
    {
        Color old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, s_white);
        GUI.color = old;
    }

    static void EnsureTextures()
    {
        if (!s_white)
        {
            s_white = new Texture2D(1, 1);
            s_white.SetPixel(0, 0, Color.white);
            s_white.Apply();
        }
        if (!s_dot)
            s_dot = MakeCircle(32, 0);
        if (!s_ring)
            s_ring = MakeCircle(32, 0.55f);
    }

    // A soft-edged white circle, with a hole in the middle when hole > 0
    static Texture2D MakeCircle(int size, float hole)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        float radius = size / 2f;
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius)) / radius;
                float alpha = Mathf.Clamp01((1 - distance) * radius / 1.5f);
                if (hole > 0)
                    alpha *= Mathf.Clamp01((distance - hole) * radius / 1.5f);
                texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        }
        texture.Apply();
        return texture;
    }
}
