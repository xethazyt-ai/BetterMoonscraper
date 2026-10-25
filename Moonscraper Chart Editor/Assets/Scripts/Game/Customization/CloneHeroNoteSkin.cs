// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Clone Hero's note textures are grey pieces (a base, a color layer, caps and glows) that Clone Hero tints with the
/// color profile while it runs. Moonscraper wants finished, colored pictures named by fret and type ("0_reg_strum"),
/// so when the picked note skin is a set of Clone Hero pieces, this puts those pictures together, colored with the
/// color profile picked in Settings > Customize (or Clone Hero's default colors).
/// </summary>
public static class CloneHeroNoteSkin
{
    const int NoteWidth = 128, NoteHeight = 64, OpenWidth = 512;
    const float TintBoost = 1.6f;       // The color layers are mid grey, so a plain multiply comes out too dark

    // Clone Hero's file names, found anywhere under the picked folder
    const string NotesFile = "spr_newnotes_strip4.png";             // 0 strum top, 1 HOPO top, 2 color layer, 3 shine, 4 tap top
    const string StarCapsFile = "spr_star_notes_cap_strip4.png";    // 0 strum cap, 1 HOPO glow, 4 tap cap
    const string StarOutlineFile = "spr_star_notes_strip4.png";     // 16 frames
    const string StarBodyFile = "spr_star_notes_strip1.png";        // 16 frames, the color layer
    const string OpenFile = "spr_open_notes_strip5.png";            // 0 top, 1 color layer, 3 HOPO glow
    const string StrikerBottomFile = "spr_newtargets_bottom_strip12.png";  // 0 base, 3 pressed base, 6 ring, 9 front of the ring
    const string StrikerHeadFile = "spr_newtargets_head_strip6.png";       // 0 head, 3 head's color rim
    const string StrikerLightFile = "Head_Lights.png";                     // 0 hit flash, 3 held glow; drawn additively

    const int NoteTopStrum = 0, NoteTopHopo = 1, NoteColor = 2, NoteTopTap = 4;
    const int CapStrum = 0, CapHopo = 1, CapTap = 4;
    const int OpenTop = 0, OpenColor = 1, OpenHopoGlow = 3;
    const int StrikerBase = 0, StrikerBasePressed = 3, StrikerRing = 6, StrikerRingFront = 9;
    const int StrikerHead = 0, StrikerHeadRim = 3, StrikerLightHit = 0, StrikerLightHeld = 3;
    const float StrikerTintBoost = 1.3f;
    const int FirstMirroredStriker = 3;     // Blue
    const int StrikerRaise = 7;             // Pixels from the head's middle (row 40 of 64 from the top) up to the picture's middle

    // Clone Hero's default colors, for keys the profile doesn't have
    static readonly string[] s_fretKeys = { "note_green", "note_red", "note_yellow", "note_blue", "note_orange" };
    static readonly Color32[] s_defaultFretColors = { Hex(0x00FF00), Hex(0xFF0000), Hex(0xFFFF00), Hex(0x0089FF), Hex(0xFFB300) };
    static readonly Color32 s_defaultOpen = Hex(0xBA00FF);
    static readonly Color32 s_defaultStarPower = Hex(0x00FFFF);
    static readonly string[] s_fretNames = { "green", "red", "yellow", "blue", "orange" };

    const string PrefLastResult = "Customize.NoteSkinResult";

    /// <summary>
    /// What happened to the note skin the last time Moonscraper opened, to show in Settings > Customize
    /// </summary>
    public static string lastResult
    {
        get { return PlayerPrefs.GetString(PrefLastResult, string.Empty); }
        private set { PlayerPrefs.SetString(PrefLastResult, value ?? string.Empty); PlayerPrefs.Save(); }
    }

    public static string builtFolder
    {
        get { return Path.Combine(CustomizationPrefs.storageFolder, "NoteSkinBuilt"); }
    }

    /// <summary>
    /// If the note skin folder holds Clone Hero's note pieces, builds Moonscraper note pictures from them and returns
    /// the folder they're in. Returns null if it isn't a Clone Hero skin or nothing could be built.
    /// </summary>
    public static string BuildIfCloneHero(string skinFolder)
    {
        string notesPath = FindFile(skinFolder, NotesFile);
        if (notesPath == null)
        {
            lastResult = "Note skin loaded as Moonscraper pictures (no Clone Hero " + NotesFile + " in it).";
            return null;
        }

        try
        {
            CloneHeroColorProfile profile = LoadProfile();
            Color32[] frets = new Color32[s_fretKeys.Length];
            for (int i = 0; i < frets.Length; ++i)
                frets[i] = GetColor(profile, s_fretKeys[i], s_defaultFretColors[i]);
            Color32 open = GetColor(profile, "note_open", s_defaultOpen);
            Color32 starPower = GetColor(profile, "note_sp_phrase", s_defaultStarPower);

            string output = builtFolder;
            if (Directory.Exists(output))
                Directory.Delete(output, true);
            Directory.CreateDirectory(output);

            int written = 0;

            List<Color32[]> notes = LoadFrames(notesPath, NoteWidth);
            List<Color32[]> caps = LoadFrames(FindFile(skinFolder, StarCapsFile), NoteWidth);
            List<Color32[]> outlines = LoadFrames(FindFile(skinFolder, StarOutlineFile), NoteWidth);
            List<Color32[]> bodies = LoadFrames(FindFile(skinFolder, StarBodyFile), NoteWidth);

            for (int fret = 0; fret < frets.Length; ++fret)
            {
                if (notes.Count > NoteTopTap)
                {
                    Color32[] coloredNote = Tint(notes[NoteColor], frets[fret], TintBoost);
                    written += Write(output, fret + "_reg_strum", NoteWidth, NoteHeight, Layer(coloredNote, notes[NoteTopStrum]));
                    written += Write(output, fret + "_reg_hopo", NoteWidth, NoteHeight, Layer(coloredNote, notes[NoteTopHopo]));
                    written += Write(output, fret + "_reg_tap", NoteWidth, NoteHeight, Layer(coloredNote, notes[NoteTopTap]));
                }

                // Star Power notes: the star in the fret's color with a Star Power colored outline, every frame of
                // the outline's animation, then the same caps as normal notes
                int frames = Mathf.Min(bodies.Count, outlines.Count);
                if (frames > 0 && caps.Count > CapTap)
                {
                    List<Color32[]> strum = new List<Color32[]>(), hopo = new List<Color32[]>(), tap = new List<Color32[]>();
                    for (int frame = 0; frame < frames; ++frame)
                    {
                        Color32[] star = Layer(Tint(bodies[frame], frets[fret], TintBoost), Tint(outlines[frame], starPower, 1));
                        strum.Add(Layer(star, caps[CapStrum]));
                        hopo.Add(Layer(star, caps[CapHopo]));
                        tap.Add(Layer(star, caps[CapTap]));
                    }
                    written += WriteStrip(output, fret + "_sp_strum", NoteWidth, NoteHeight, strum);
                    written += WriteStrip(output, fret + "_sp_hopo", NoteWidth, NoteHeight, hopo);
                    written += WriteStrip(output, fret + "_sp_tap", NoteWidth, NoteHeight, tap);
                }
            }

            List<Color32[]> opens = LoadFrames(FindFile(skinFolder, OpenFile), OpenWidth);
            if (opens.Count > OpenHopoGlow)
            {
                Color32[] openNote = Layer(Tint(opens[OpenColor], open, TintBoost), opens[OpenTop]);
                Color32[] openStar = Layer(Tint(opens[OpenColor], starPower, TintBoost), opens[OpenTop]);
                written += Write(output, "5_reg_strum", OpenWidth, NoteHeight, openNote);
                written += Write(output, "5_reg_hopo", OpenWidth, NoteHeight, Layer(openNote, opens[OpenHopoGlow]));
                written += Write(output, "5_sp_strum", OpenWidth, NoteHeight, openStar);
                written += Write(output, "5_sp_hopo", OpenWidth, NoteHeight, Layer(openStar, opens[OpenHopoGlow]));
            }

            int strikers = BuildStrikers(skinFolder, output, profile, frets);
            written += strikers;

            Debug.Log("Built " + written + " note and striker pictures from the Clone Hero note skin in " + skinFolder);
            lastResult = written > 0
                ? "Clone Hero notes: built " + (written - strikers) + " note pictures" + (strikers > 0 ? " and the strikers" : " (no striker textures in the folder, so the strikeline stays Moonscraper's)") + (profile != null ? ", colored with your color profile." : ", in Clone Hero's default colors.")
                : "Clone Hero notes: found the folder but couldn't build any notes from it.";
            return written > 0 ? output : null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't build notes from the Clone Hero note skin. " + e);
            lastResult = "Clone Hero notes failed to build: " + e.Message;
            return null;
        }
    }

    // The strikeline: Moonscraper draws a base, the head (up, held down, or flashing when hit) and a cover in front,
    // which match Clone Hero's striker pieces. Returns how many pictures it wrote, 0 without Clone Hero's strikers.
    static int BuildStrikers(string skinFolder, string output, CloneHeroColorProfile profile, Color32[] noteColors)
    {
        List<Color32[]> bottoms = LoadFrames(FindFile(skinFolder, StrikerBottomFile), NoteWidth);
        List<Color32[]> heads = LoadFrames(FindFile(skinFolder, StrikerHeadFile), NoteWidth);
        List<Color32[]> lights = LoadFrames(FindFile(skinFolder, StrikerLightFile), NoteWidth);
        if (bottoms.Count <= StrikerRingFront || heads.Count <= StrikerHeadRim || lights.Count <= StrikerLightHeld)
            return 0;

        int written = 0;
        for (int fret = 0; fret < s_fretNames.Length; ++fret)
        {
            Color32 baseColor = GetColor(profile, "striker_base_" + s_fretNames[fret], Hex(0xFFFFFF));
            Color32 cover = GetColor(profile, "striker_cover_" + s_fretNames[fret], noteColors[fret]);
            Color32 headCover = GetColor(profile, "striker_head_cover_" + s_fretNames[fret], noteColors[fret]);
            Color32 light = GetColor(profile, "striker_head_light_" + s_fretNames[fret], noteColors[fret]);

            Color32[] ring = Tint(bottoms[StrikerRing], cover, StrikerTintBoost);
            Color32[] head = Layer(Tint(heads[StrikerHead], baseColor, 1), Tint(heads[StrikerHeadRim], headCover, StrikerTintBoost));
            Color32[] pressed = Layer(Tint(bottoms[StrikerBasePressed], baseColor, 1), ring);

            // Clone Hero mirrors the blue and orange strikers, so their lit side faces the middle of the highway
            bool mirror = fret >= FirstMirroredStriker;
            written += Write(output, fret + "_fret_base", NoteWidth, NoteHeight, Place(Layer(Tint(bottoms[StrikerBase], baseColor, 1), ring), mirror));
            written += Write(output, fret + "_fret_release", NoteWidth, NoteHeight, Place(head, mirror));
            written += Write(output, fret + "_fret_press", NoteWidth, NoteHeight, Place(AddLight(pressed, lights[StrikerLightHeld], light), mirror));
            written += Write(output, fret + "_fret_anim", NoteWidth, NoteHeight, Place(AddLight(head, lights[StrikerLightHit], light), mirror));
            written += Write(output, fret + "_fret_cover", NoteWidth, NoteHeight, Place(Tint(bottoms[StrikerRingFront], cover, StrikerTintBoost), mirror));
        }

        // Clone Hero's strikers have no stem, so hide Moonscraper's
        written += Write(output, "fret_stem", 4, 4, new Color32[16]);
        return written;
    }

    // Raised so the head's middle is the picture's middle, which Moonscraper puts on the strikeline (Clone Hero
    // pins its strikers there too), and flipped left to right when `mirror` is set
    static Color32[] Place(Color32[] pixels, bool mirror)
    {
        Color32[] result = new Color32[pixels.Length];
        for (int y = StrikerRaise; y < NoteHeight; ++y)     // Rows go bottom to top
        {
            for (int x = 0; x < NoteWidth; ++x)
                result[y * NoteWidth + x] = pixels[(y - StrikerRaise) * NoteWidth + (mirror ? NoteWidth - 1 - x : x)];
        }
        return result;
    }

    // A glow on black, added on top of `bottom` the way Clone Hero draws its striker lights
    static Color32[] AddLight(Color32[] bottom, Color32[] glow, Color32 color)
    {
        Color32[] result = new Color32[bottom.Length];
        for (int i = 0; i < bottom.Length; ++i)
        {
            Color32 under = bottom[i], over = glow[i];
            float underAlpha = under.a / 255f;
            float overAlpha = over.a / 255f;
            float glowAlpha = Mathf.Max(over.r, Mathf.Max(over.g, over.b)) / 255f * overAlpha;
            float alpha = Mathf.Min(1, underAlpha + glowAlpha * (1 - underAlpha));
            if (alpha <= 0)
            {
                result[i] = new Color32(0, 0, 0, 0);
                continue;
            }

            result[i] = new Color32(
                (byte)Mathf.Min(255, (under.r * underAlpha + over.r * color.r / 255f * overAlpha) / alpha),
                (byte)Mathf.Min(255, (under.g * underAlpha + over.g * color.g / 255f * overAlpha) / alpha),
                (byte)Mathf.Min(255, (under.b * underAlpha + over.b * color.b / 255f * overAlpha) / alpha),
                (byte)(alpha * 255));
        }
        return result;
    }

    static CloneHeroColorProfile LoadProfile()
    {
        string path = CustomizationPrefs.GetStored(CustomizationPrefs.Kind.ColorProfile);
        if (path == null)
            return null;

        try
        {
            return CloneHeroColorProfile.Load(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't read the color profile for the note skin, so using Clone Hero's colors. " + e.Message);
            return null;
        }
    }

    static Color32 GetColor(CloneHeroColorProfile profile, string key, Color32 fallback)
    {
        CloneHeroColorProfile.Rgb rgb;
        if (profile != null && profile.TryGet("guitar", key, out rgb))
            return new Color32(rgb.r, rgb.g, rgb.b, 255);
        return fallback;
    }

    static Color32 Hex(int rgb)
    {
        return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }

    static string FindFile(string folder, string name)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return null;

        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileName(file), name, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }

    // Cuts a strip of frames side by side into one pixel array per frame. Empty if the file is missing or the wrong size.
    static List<Color32[]> LoadFrames(string path, int frameWidth)
    {
        List<Color32[]> frames = new List<Color32[]>();
        if (path == null)
            return frames;

        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(File.ReadAllBytes(path)) || texture.height != NoteHeight || texture.width % frameWidth != 0)
            {
                Debug.LogWarning("Skipping " + path + ", it isn't the size Clone Hero's note textures are");
                return frames;
            }

            Color32[] pixels = texture.GetPixels32();
            for (int frame = 0; frame < texture.width / frameWidth; ++frame)
            {
                Color32[] cut = new Color32[frameWidth * NoteHeight];
                for (int y = 0; y < NoteHeight; ++y)
                    Array.Copy(pixels, y * texture.width + frame * frameWidth, cut, y * frameWidth, frameWidth);
                frames.Add(cut);
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(texture);
        }

        return frames;
    }

    static Color32[] Tint(Color32[] source, Color32 color, float boost)
    {
        Color32[] tinted = new Color32[source.Length];
        for (int i = 0; i < source.Length; ++i)
        {
            Color32 pixel = source[i];
            tinted[i] = new Color32(
                (byte)Mathf.Min(255, pixel.r * color.r / 255f * boost),
                (byte)Mathf.Min(255, pixel.g * color.g / 255f * boost),
                (byte)Mathf.Min(255, pixel.b * color.b / 255f * boost),
                pixel.a);
        }
        return tinted;
    }

    // `top` drawn over `bottom`, as a new array
    static Color32[] Layer(Color32[] bottom, Color32[] top)
    {
        Color32[] result = new Color32[bottom.Length];
        for (int i = 0; i < bottom.Length; ++i)
        {
            Color32 under = bottom[i], over = top[i];
            float overAlpha = over.a / 255f;
            float underAlpha = under.a / 255f * (1 - overAlpha);
            float alpha = overAlpha + underAlpha;
            if (alpha <= 0)
            {
                result[i] = new Color32(0, 0, 0, 0);
                continue;
            }

            result[i] = new Color32(
                (byte)((over.r * overAlpha + under.r * underAlpha) / alpha),
                (byte)((over.g * overAlpha + under.g * underAlpha) / alpha),
                (byte)((over.b * overAlpha + under.b * underAlpha) / alpha),
                (byte)(alpha * 255));
        }
        return result;
    }

    static int Write(string folder, string name, int width, int height, Color32[] pixels)
    {
        return WriteStrip(folder, name, width, height, new List<Color32[]> { pixels });
    }

    // Frames side by side, which Moonscraper's loader cuts back up into the animation
    static int WriteStrip(string folder, string name, int width, int height, List<Color32[]> frames)
    {
        Texture2D texture = new Texture2D(width * frames.Count, height, TextureFormat.RGBA32, false);
        try
        {
            Color32[] pixels = new Color32[texture.width * height];
            for (int frame = 0; frame < frames.Count; ++frame)
            {
                for (int y = 0; y < height; ++y)
                    Array.Copy(frames[frame], y * width, pixels, y * texture.width + frame * width, width);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
            return 1;
        }
        finally
        {
            UnityEngine.Object.Destroy(texture);
        }
    }
}
