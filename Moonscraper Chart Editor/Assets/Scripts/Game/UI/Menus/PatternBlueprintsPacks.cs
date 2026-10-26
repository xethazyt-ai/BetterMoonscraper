// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using MoonscraperChartEditor.Song;

/// <summary>
/// Pattern Blueprints: Mirror and lane shift for pasting, and pattern packs, files of patterns to share and add.
/// </summary>
public partial class PatternBlueprints
{
    const string PackExtension = "json";

    bool m_pendingImport = false;
    bool m_pendingExport = false;
    int m_packRequested = 0;        // 1 import, 2 export, done from Update

    // How many lanes a pattern's notes move between: GH Live's black and white frets share three columns
    static int LaneColumns(Blueprint blueprint)
    {
        ChartEditor editor = ChartEditor.InstanceExists ? ChartEditor.Instance : null;
        Chart.GameMode gameMode = ParseGameMode(blueprint.gameMode);
        if (gameMode == Chart.GameMode.GHLGuitar)
            return 3;
        if (gameMode == Chart.GameMode.Drums && editor && editor.currentGameMode == Chart.GameMode.Drums && editor.laneInfo)
            return Mathf.Clamp(editor.laneInfo.laneCount, 2, 5);    // 4-lane or 5-lane, as the open chart is
        return 5;
    }

    static bool IsLaneNote(Chart.GameMode gameMode, int rawNote)
    {
        if (gameMode == Chart.GameMode.GHLGuitar)
            return rawNote >= 0 && rawNote < (int)Note.GHLiveGuitarFret.Open;
        return rawNote >= 0 && rawNote < (int)Note.GuitarFret.Open;     // Open notes and kicks stay as they are
    }

    static int MapLane(Blueprint blueprint, int rawNote, bool mirror, int laneShift)
    {
        Chart.GameMode gameMode = ParseGameMode(blueprint.gameMode);
        if ((!mirror && laneShift == 0) || !IsLaneNote(gameMode, rawNote))
            return rawNote;

        int columns = LaneColumns(blueprint);
        int column = gameMode == Chart.GameMode.GHLGuitar ? rawNote % 3 : Mathf.Min(rawNote, columns - 1);
        if (mirror)
            column = columns - 1 - column;
        column = Mathf.Clamp(column + laneShift, 0, columns - 1);

        return gameMode == Chart.GameMode.GHLGuitar ? (rawNote / 3) * 3 + column : column;
    }

    // How far the pattern can move without any note leaving the highway
    static void ShiftRange(Blueprint blueprint, bool mirror, out int minShift, out int maxShift)
    {
        Chart.GameMode gameMode = ParseGameMode(blueprint.gameMode);
        int columns = LaneColumns(blueprint);
        int lowest = columns, highest = -1;
        foreach (BlueprintNote note in blueprint.notes)
        {
            if (!IsLaneNote(gameMode, note.rawNote))
                continue;
            int column = MapLane(blueprint, note.rawNote, mirror, 0);
            if (gameMode == Chart.GameMode.GHLGuitar)
                column %= 3;
            lowest = Mathf.Min(lowest, column);
            highest = Mathf.Max(highest, column);
        }

        if (highest < 0)
        {
            minShift = maxShift = 0;    // Only open notes or kicks
            return;
        }
        minShift = -lowest;
        maxShift = columns - 1 - highest;
    }

    void ExportPack()
    {
        if (m_file.blueprints.Count == 0)
            return;

        string path;
        if (!FileExplorer.SaveFilePanel(new ExtensionFilter("Pattern pack", PackExtension), "My patterns", PackExtension, out path))
            return;

        try
        {
            File.WriteAllText(path, JsonUtility.ToJson(m_file, true));
            m_status = "Exported " + m_file.blueprints.Count + (m_file.blueprints.Count == 1 ? " pattern" : " patterns") + " to " + Path.GetFileName(path) + ".";
        }
        catch (Exception e)
        {
            m_status = "Couldn't export: " + e.Message;
        }
    }

    void ImportPack()
    {
        string path;
        if (!FileExplorer.OpenFilePanel(new ExtensionFilter("Pattern pack", PackExtension), PackExtension, out path))
            return;

        BlueprintFile pack;
        try
        {
            pack = JsonUtility.FromJson<BlueprintFile>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            m_status = "Couldn't read " + Path.GetFileName(path) + ": " + e.Message;
            return;
        }

        if (pack == null || pack.blueprints == null || pack.blueprints.Count == 0)
        {
            m_status = Path.GetFileName(path) + " has no patterns in it.";
            return;
        }

        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Blueprint blueprint in m_file.blueprints)
            names.Add(blueprint.name);

        int added = 0, same = 0;
        foreach (Blueprint blueprint in pack.blueprints)
        {
            if (blueprint == null || blueprint.notes == null || blueprint.notes.Count == 0)
                continue;
            if (IsAlreadySaved(blueprint))
            {
                ++same;
                continue;
            }

            string name = string.IsNullOrEmpty(blueprint.name) ? "Pattern" : blueprint.name.Trim();
            if (name.Length > 40)
                name = name.Substring(0, 40);
            string unique = name;
            for (int i = 2; names.Contains(unique); ++i)
                unique = name + " (" + i + ")";
            blueprint.name = unique;
            names.Add(unique);

            m_file.blueprints.Add(blueprint);
            ++added;
        }

        if (added > 0)
            Save();
        m_status = "Added " + added + (added == 1 ? " pattern" : " patterns") + " from " + Path.GetFileName(path)
            + (same > 0 ? " (" + same + " already here)" : string.Empty) + ".";
    }

    // The same notes as a saved pattern, whatever it's called
    bool IsAlreadySaved(Blueprint blueprint)
    {
        foreach (Blueprint saved in m_file.blueprints)
        {
            if (saved.gameMode != blueprint.gameMode || saved.step != blueprint.step || saved.notes.Count != blueprint.notes.Count)
                continue;

            bool same = true;
            for (int i = 0; i < saved.notes.Count && same; ++i)
            {
                BlueprintNote a = saved.notes[i], b = blueprint.notes[i];
                same = Math.Abs(a.beat - b.beat) < 1e-6 && a.rawNote == b.rawNote && a.flags == b.flags && Math.Abs(a.lengthBeats - b.lengthBeats) < 1e-6;
            }
            if (same)
                return true;
        }
        return false;
    }
}
