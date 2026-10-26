// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The look of Moonscraper's menus and panels: Default, or Dark, which keeps the same layout and navy-and-teal
/// style but turns the white boxes (text fields, dropdowns, list items) dark grey with light text and dims the panels.
/// Picked in Settings > Customize. The original colors are remembered, so going back to Default is exact.
/// </summary>
public static class UITheme
{
    public enum Theme { Default, Dark }

    const string PrefTheme = "Customize.Theme";

    // Dark colors: boxes you type in or pick from, smaller controls such as checkboxes and slider tracks, and text
    static readonly Color DarkField = new Color(0.15f, 0.17f, 0.21f);
    static readonly Color DarkControl = new Color(0.33f, 0.36f, 0.42f);
    static readonly Color DarkPanelTint = new Color(0.62f, 0.62f, 0.66f);
    static readonly Color LightText = new Color(0.9f, 0.92f, 0.95f);
    static readonly Color DarkPlaceholder = new Color(0.62f, 0.65f, 0.7f);

    // Sprites by name: white backgrounds for fields and buttons, small controls, and the panel frames
    static readonly HashSet<string> s_fieldSprites = new HashSet<string> { "InputFieldBackground", "UISprite", "Background", "TestUISprite", "white_1" };
    static readonly HashSet<string> s_controlSprites = new HashSet<string> { "SliderBG", "1491902985_Checkbox Empty", "Knob", "rect_knob_test" };
    static readonly HashSet<string> s_panelSprites = new HashSet<string> { "settings_panel", "settings_panel_resz", "settings_panel_no_title", "panel1", "panel_bg" };

    static readonly Dictionary<Graphic, Color> s_originalColors = new Dictionary<Graphic, Color>();
    static readonly Dictionary<Selectable, ColorBlock> s_originalBlocks = new Dictionary<Selectable, ColorBlock>();
    static Theme? s_theme;
    static Theme s_applied = Theme.Default;

    public static Theme current
    {
        get
        {
            if (!s_theme.HasValue)
                s_theme = PlayerPrefs.GetInt(PrefTheme, 0) == 1 ? Theme.Dark : Theme.Default;
            return s_theme.Value;
        }
        set
        {
            s_theme = value;
            PlayerPrefs.SetInt(PrefTheme, (int)value);
            PlayerPrefs.Save();
            Apply();
        }
    }

    /// <summary>
    /// Puts the theme on every menu and panel there is now, including hidden ones. Call again after building new UI.
    /// </summary>
    public static void Apply()
    {
        Theme theme = current;
        DarkTitleBar.Set(theme == Theme.Dark);
        if (theme == Theme.Default && s_applied == Theme.Default)
            return;     // Nothing has been changed, so there's nothing to put back

        foreach (Graphic graphic in Resources.FindObjectsOfTypeAll<Graphic>())
        {
            if (!graphic || !graphic.gameObject.scene.IsValid())
                continue;   // Prefab assets, not things on screen

            Color original;
            if (!s_originalColors.TryGetValue(graphic, out original))
            {
                if (s_applied != Theme.Default)
                    continue;   // Made since the theme went on, from copies that already had it
                original = graphic.color;
                s_originalColors[graphic] = original;
            }

            graphic.color = theme == Theme.Dark ? DarkColor(graphic, original) : original;
        }

        foreach (Selectable selectable in Resources.FindObjectsOfTypeAll<Selectable>())
        {
            if (!selectable || !selectable.gameObject.scene.IsValid() || selectable.transition != Selectable.Transition.ColorTint
                || (!s_originalBlocks.ContainsKey(selectable) && !HasDarkenedBackground(selectable)))
                continue;

            ColorBlock original;
            if (!s_originalBlocks.TryGetValue(selectable, out original))
            {
                if (s_applied != Theme.Default)
                    continue;
                original = selectable.colors;
                s_originalBlocks[selectable] = original;
            }

            selectable.colors = theme == Theme.Dark ? DarkBlock(original) : original;
        }

        // Forget anything that's gone, such as closed windows
        RemoveDestroyed(s_originalColors);
        RemoveDestroyed(s_originalBlocks);

        s_applied = theme;
    }

    /// <summary>
    /// Themes UI made after Apply ran (for example a window built the first time it opens), from its own colors
    /// as made. Safe to call often.
    /// </summary>
    public static void ApplyToNew(GameObject root)
    {
        if (current == Theme.Default || !root)
            return;

        foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
        {
            if (s_originalColors.ContainsKey(graphic))
                continue;
            s_originalColors[graphic] = graphic.color;
            graphic.color = DarkColor(graphic, graphic.color);
        }

        foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
        {
            if (selectable.transition != Selectable.Transition.ColorTint || s_originalBlocks.ContainsKey(selectable) || !HasDarkenedBackground(selectable))
                continue;
            s_originalBlocks[selectable] = selectable.colors;
            selectable.colors = DarkBlock(selectable.colors);
        }
    }

    static Color DarkColor(Graphic graphic, Color original)
    {
        if (graphic is Text || graphic is TMPro.TMP_Text)
        {
            // Dark writing on the old white boxes becomes light writing on the dark ones; light writing stays
            if (Luminance(original) >= 0.5f)
                return original;
            Color light = original.a < 0.75f ? DarkPlaceholder : LightText;
            light.a = original.a < 0.75f ? Mathf.Max(original.a, 0.6f) : original.a;
            return light;
        }

        Image image = graphic as Image;
        if (!image)
            return original;

        string sprite = image.sprite ? image.sprite.name : string.Empty;
        if (s_panelSprites.Contains(sprite))
            return Multiply(original, DarkPanelTint);

        if ((s_fieldSprites.Contains(sprite) || sprite == string.Empty) && Luminance(original) > 0.6f && !IsColored(original))
            return WithAlpha(DarkField, original.a);

        if (s_controlSprites.Contains(sprite) && Luminance(original) > 0.6f)
            return WithAlpha(DarkControl, original.a);

        return original;    // Icons, notes and anything with its own color keep it
    }

    // Button tints on a darkened field: a little dimmer normally and full brightness on hover, so hovering lightens
    // the dark field as it used to darken the white one. Colored tints stay as they are.
    static ColorBlock DarkBlock(ColorBlock block)
    {
        if (!IsColored(block.normalColor))
            block.normalColor = WithAlpha(new Color(0.82f, 0.82f, 0.82f), block.normalColor.a);
        if (!IsColored(block.highlightedColor))
            block.highlightedColor = WithAlpha(Color.white, block.highlightedColor.a);
        if (!IsColored(block.pressedColor))
            block.pressedColor = WithAlpha(new Color(0.65f, 0.65f, 0.65f), block.pressedColor.a);
        return block;
    }

    // Only buttons and fields whose background went dark get the dark tints; icon buttons keep theirs
    static bool HasDarkenedBackground(Selectable selectable)
    {
        Image image = selectable.targetGraphic as Image;
        Color original;
        return image && s_originalColors.TryGetValue(image, out original) && DarkColor(image, original) != original;
    }

    static bool IsColored(Color color)
    {
        float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        return max - min > 0.12f;
    }

    static float Luminance(Color color)
    {
        return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
    }

    static Color Multiply(Color a, Color b)
    {
        return new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a);
    }

    static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    static void RemoveDestroyed<T>(Dictionary<T, Color> map) where T : Object
    {
        List<T> gone = new List<T>();
        foreach (T key in map.Keys)
        {
            if (!key)
                gone.Add(key);
        }
        foreach (T key in gone)
            map.Remove(key);
    }

    static void RemoveDestroyed<T>(Dictionary<T, ColorBlock> map) where T : Object
    {
        List<T> gone = new List<T>();
        foreach (T key in map.Keys)
        {
            if (!key)
                gone.Add(key);
        }
        foreach (T key in gone)
            map.Remove(key);
    }
}
