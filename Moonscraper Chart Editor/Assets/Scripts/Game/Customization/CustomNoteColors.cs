// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using MoonscraperChartEditor.Song;

/// <summary>
/// Recolors notes, sustains and strikers with a Clone Hero color profile. The note materials are shared assets, so
/// rather than editing them this keeps recolored copies and MeshNoteResources hands those out instead.
/// </summary>
public static class CustomNoteColors
{
    // Moonscraper's note palettes put the same colors at the same positions for every instrument
    const int Green = 0, Red = 1, Yellow = 2, Blue = 3, Orange = 4, GhlBlack = 6, GhlWhite = 7;

    // Open note materials: 0 strum, 1 HOPO/tap. The rest are Star Power and the placement tool, which keep their look.
    const int OpenStrum = 0, OpenHopo = 1;

    static readonly Dictionary<Chart.GameMode, Dictionary<Material, Material>> s_swaps = new Dictionary<Chart.GameMode, Dictionary<Material, Material>>();
    static readonly Dictionary<Chart.GameMode, Color[]> s_laneColors = new Dictionary<Chart.GameMode, Color[]>();
    static readonly List<Material> s_createdMaterials = new List<Material>();

    // Star Power: notes and sustains inside a Star Power phrase, like Clone Hero's note_sp_phrase and sustain_sp_phrase
    static readonly Dictionary<Chart.GameMode, Color[]> s_starPowerColors = new Dictionary<Chart.GameMode, Color[]>();     // 0 note, 1 cymbal
    static readonly Dictionary<Chart.GameMode, Dictionary<Material, Material>> s_starPowerSwapsByMode = new Dictionary<Chart.GameMode, Dictionary<Material, Material>>();
    static Material s_starPowerSustain;
    static CloneHeroColorProfile s_profile;

    static Material[] s_originalSustains;
    static bool s_sustainsReplaced = false;

    public static bool active { get { return s_swaps.Count > 0; } }

    public static Material Swap(Chart.GameMode gameMode, Material material)
    {
        Dictionary<Material, Material> swaps;
        Material swapped;
        if (material && s_swaps.TryGetValue(gameMode, out swaps) && swaps.TryGetValue(material, out swapped))
            return swapped;

        return material;
    }

    public static bool TryGetLaneColor(Chart.GameMode gameMode, int laneCount, int lane, out Color color)
    {
        color = Color.white;
        Color[] colors;
        if (!s_laneColors.TryGetValue(gameMode, out colors))
            return false;

        // 4-lane drums drop orange, so its last lane is green
        if (gameMode == Chart.GameMode.Drums && laneCount == 4 && lane == 3)
            lane = 4;

        if (lane < 0 || lane >= colors.Length)
            return false;

        color = colors[lane];
        return true;
    }

    public static void Apply(CloneHeroColorProfile profile)
    {
        Clear();
        s_profile = profile;

        MeshNoteResources notes = FindAsset<MeshNoteResources>();
        if (notes)
        {
            AddPalettes(notes, Chart.GameMode.Guitar, new Dictionary<int, string[]>()
            {
                { Green, new[] { "guitar", "note_green" } },
                { Red, new[] { "guitar", "note_red" } },
                { Yellow, new[] { "guitar", "note_yellow" } },
                { Blue, new[] { "guitar", "note_blue" } },
                { Orange, new[] { "guitar", "note_orange" } },
            }, null, profile);
            AddOpen(notes, Chart.GameMode.Guitar, profile, "guitar", "note_open");

            // Drums use tom colors for normal pads and cymbal colors for cymbals. Clone Hero has no orange pad (5-lane
            // drums), so that one takes the guitar's orange.
            AddPalettes(notes, Chart.GameMode.Drums, new Dictionary<int, string[]>()
            {
                { Green, new[] { "drums", "tom_green" } },
                { Red, new[] { "drums", "tom_red" } },
                { Yellow, new[] { "drums", "tom_yellow" } },
                { Blue, new[] { "drums", "tom_blue" } },
                { Orange, new[] { "guitar", "note_orange" } },
            }, new Dictionary<int, string[]>()
            {
                { Green, new[] { "drums", "cym_green" } },
                { Red, new[] { "drums", "cym_red" } },
                { Yellow, new[] { "drums", "cym_yellow" } },
                { Blue, new[] { "drums", "cym_blue" } },
                { Orange, new[] { "guitar", "note_orange" } },
            }, profile);
            AddOpen(notes, Chart.GameMode.Drums, profile, "drums", "note_kick");

            AddPalettes(notes, Chart.GameMode.GHLGuitar, new Dictionary<int, string[]>()
            {
                { GhlBlack, new[] { "sixfret", "sf_note_black_left" } },
                { GhlWhite, new[] { "sixfret", "sf_note_white_left" } },
            }, null, profile);
            AddOpen(notes, Chart.GameMode.GHLGuitar, profile, "sixfret", "sf_note_open");
        }
        else
        {
            Debug.LogWarning("Couldn't find the note materials to recolor");
        }

        // Strikers (the frets at the bottom of the highway), in lane order
        AddLaneColors(Chart.GameMode.Guitar, profile, new[] {
            new[] { "guitar", "striker_cover_green", "note_green" },
            new[] { "guitar", "striker_cover_red", "note_red" },
            new[] { "guitar", "striker_cover_yellow", "note_yellow" },
            new[] { "guitar", "striker_cover_blue", "note_blue" },
            new[] { "guitar", "striker_cover_orange", "note_orange" },
        });
        AddLaneColors(Chart.GameMode.Drums, profile, new[] {
            new[] { "drums", "drums_striker_cover_red", "tom_red" },
            new[] { "drums", "drums_striker_cover_yellow", "tom_yellow" },
            new[] { "drums", "drums_striker_cover_blue", "tom_blue" },
            new[] { "guitar", "striker_cover_orange", "note_orange" },
            new[] { "drums", "drums_striker_cover_green", "tom_green" },
        });

        ApplySustains(profile);
        ApplyStarPower(profile);
    }

    static void ApplyStarPower(CloneHeroColorProfile profile)
    {
        AddStarPowerColors(Chart.GameMode.Guitar, profile, new[] { "guitar", "note_sp_phrase" }, null);
        AddStarPowerColors(Chart.GameMode.Drums, profile, new[] { "drums", "tom_sp_phrase" }, new[] { "drums", "cym_sp_phrase" });
        AddStarPowerColors(Chart.GameMode.GHLGuitar, profile, new[] { "sixfret", "sf_note_sp_phrase" }, null);

        SustainResources sustainResources = FindAsset<SustainResources>();
        Color color;
        if (sustainResources && sustainResources.sustainColours.Length > 0 && sustainResources.sustainColours[0]
            && TryGet(profile, new[] { "guitar", "sustain_sp_phrase" }, out color))
        {
            Material template = sustainResources.sustainColours[0];
            s_starPowerSustain = new Material(template);
            s_starPowerSustain.name = "Star Power sustain (color profile)";
            color.a = template.GetColor("_Color").a;
            s_starPowerSustain.SetColor("_Color", color);
            s_createdMaterials.Add(s_starPowerSustain);
        }
    }

    static void AddStarPowerColors(Chart.GameMode gameMode, CloneHeroColorProfile profile, string[] noteKey, string[] cymbalKey)
    {
        Color note;
        if (!TryGet(profile, noteKey, out note))
            return;

        Color cymbal;
        if (cymbalKey == null || !TryGet(profile, cymbalKey, out cymbal))
            cymbal = note;

        s_starPowerColors[gameMode] = new[] { note, cymbal };
    }

    /// <summary>
    /// The color material for a note in a Star Power phrase: the note's own material in the profile's Star Power color,
    /// or the material unchanged when the profile has none
    /// </summary>
    public static Material StarPower(Chart.GameMode gameMode, Material material, bool isCymbal)
    {
        Color[] colors;
        if (!material || !s_starPowerColors.TryGetValue(gameMode, out colors))
            return material;

        Dictionary<Material, Material> swaps;
        if (!s_starPowerSwapsByMode.TryGetValue(gameMode, out swaps))
        {
            swaps = new Dictionary<Material, Material>();
            s_starPowerSwapsByMode[gameMode] = swaps;
        }

        Material recolored;
        if (swaps.TryGetValue(material, out recolored) && recolored)
            return recolored;

        recolored = new Material(material);
        recolored.name = material.name + " (Star Power)";
        if (material.HasProperty("_Color"))
        {
            Color color = colors[isCymbal ? 1 : 0];
            color.a = material.GetColor("_Color").a;     // Taps and cymbals stay see-through
            recolored.SetColor("_Color", color);
        }

        s_createdMaterials.Add(recolored);
        swaps[material] = recolored;
        return recolored;
    }

    /// <summary>
    /// The sustain material for a note in a Star Power phrase, or null to use the fret's own
    /// </summary>
    public static Material starPowerSustain { get { return s_starPowerSustain; } }

    public static void Clear()
    {
        s_swaps.Clear();
        s_laneColors.Clear();
        s_starPowerColors.Clear();
        s_starPowerSwapsByMode.Clear();
        s_starPowerSustain = null;
        s_profile = null;

        foreach (Material material in s_createdMaterials)
        {
            if (material)
                Object.Destroy(material);
        }
        s_createdMaterials.Clear();

        if (s_sustainsReplaced && SkinManager.InstanceExists)
        {
            Material[] sustains = SkinManager.Instance.currentSkin.sustain_mats;
            for (int i = 0; i < sustains.Length && i < s_originalSustains.Length; ++i)
                sustains[i] = s_originalSustains[i];
            s_sustainsReplaced = false;
        }
    }

    static void AddPalettes(MeshNoteResources notes, Chart.GameMode gameMode, Dictionary<int, string[]> noteKeys, Dictionary<int, string[]> cymbalKeys, CloneHeroColorProfile profile)
    {
        foreach (var entry in noteKeys)
        {
            Color color;
            if (!TryGet(profile, entry.Value, out color))
                continue;

            AddSwap(gameMode, Palette(notes.strumColorPalette, entry.Key), color);
            AddSwap(gameMode, Palette(notes.tapColorPalette, entry.Key), color);
            if (cymbalKeys == null)
                AddSwap(gameMode, Palette(notes.cymbalColorPalette, entry.Key), color);
        }

        if (cymbalKeys != null)
        {
            foreach (var entry in cymbalKeys)
            {
                Color color;
                if (TryGet(profile, entry.Value, out color))
                    AddSwap(gameMode, Palette(notes.cymbalColorPalette, entry.Key), color);
            }
        }
    }

    static void AddOpen(MeshNoteResources notes, Chart.GameMode gameMode, CloneHeroColorProfile profile, string section, string key)
    {
        Color color;
        if (!TryGet(profile, new[] { section, key }, out color))
            return;

        AddSwap(gameMode, Palette(notes.openMaterials, OpenStrum), color);
        AddSwap(gameMode, Palette(notes.openMaterials, OpenHopo), color);
    }

    static void AddLaneColors(Chart.GameMode gameMode, CloneHeroColorProfile profile, string[][] keys)
    {
        Color[] colors = new Color[keys.Length];
        for (int i = 0; i < keys.Length; ++i)
        {
            if (!TryGet(profile, new[] { keys[i][0], keys[i][1] }, out colors[i]) && !TryGet(profile, new[] { keys[i][0], keys[i][2] }, out colors[i]))
                return;     // Leave the strikers alone rather than recolor only some of them
        }

        s_laneColors[gameMode] = colors;
    }

    // Sustains follow the guitar's fret order: green, red, yellow, blue, orange, open
    static void ApplySustains(CloneHeroColorProfile profile)
    {
        SustainResources sustainResources = FindAsset<SustainResources>();
        if (!sustainResources)
            return;

        string[] keys = { "sustain_green", "sustain_red", "sustain_yellow", "sustain_blue", "sustain_orange", "sustain_open" };
        Material[] sustains = SkinManager.Instance.currentSkin.sustain_mats;

        s_originalSustains = (Material[])sustains.Clone();
        s_sustainsReplaced = true;

        for (int i = 0; i < keys.Length && i < sustains.Length && i < sustainResources.sustainColours.Length; ++i)
        {
            Color color;
            if (!TryGet(profile, new[] { "guitar", keys[i] }, out color) || !sustainResources.sustainColours[i])
                continue;

            Material material = new Material(sustainResources.sustainColours[i]);
            material.name = i.ToString();
            color.a = sustainResources.sustainColours[i].GetColor("_Color").a;
            material.SetColor("_Color", color);
            s_createdMaterials.Add(material);
            sustains[i] = material;
        }
    }

    /// <summary>
    /// Any color from the picked color profile, such as [other] striker_hit_flame. False when none is picked or it
    /// doesn't have that key.
    /// </summary>
    public static bool TryGetProfileColor(string section, string key, out Color color)
    {
        if (s_profile == null)
        {
            color = Color.white;
            return false;
        }
        return TryGet(s_profile, new[] { section, key }, out color);
    }

    static Material Palette(Material[] palette, int index)
    {
        return palette != null && index < palette.Length ? palette[index] : null;
    }

    static void AddSwap(Chart.GameMode gameMode, Material original, Color color)
    {
        if (!original)
            return;

        Dictionary<Material, Material> swaps;
        if (!s_swaps.TryGetValue(gameMode, out swaps))
        {
            swaps = new Dictionary<Material, Material>();
            s_swaps[gameMode] = swaps;
        }

        if (swaps.ContainsKey(original))
            return;

        Material recolored = new Material(original);
        recolored.name = original.name + " (color profile)";
        if (original.HasProperty("_Color"))
        {
            color.a = original.GetColor("_Color").a;     // Taps and cymbals are see-through on purpose
            recolored.SetColor("_Color", color);
        }

        s_createdMaterials.Add(recolored);
        swaps[original] = recolored;
    }

    static bool TryGet(CloneHeroColorProfile profile, string[] sectionAndKey, out Color color)
    {
        CloneHeroColorProfile.Rgb rgb;
        if (profile.TryGet(sectionAndKey[0], sectionAndKey[1], out rgb))
        {
            color = new Color32(rgb.r, rgb.g, rgb.b, 255);
            return true;
        }

        color = Color.white;
        return false;
    }

    static T FindAsset<T>() where T : Object
    {
        T[] found = Resources.FindObjectsOfTypeAll<T>();
        return found.Length > 0 ? found[0] : null;
    }
}
