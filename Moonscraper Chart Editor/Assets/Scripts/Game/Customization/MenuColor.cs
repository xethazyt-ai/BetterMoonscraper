// See LICENSE in project root for license information.

using System.Collections.Generic;
using MoonscraperChartEditor.Song;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The menu color picked in Settings > Customize, in place of Moonscraper's usual blue. Works on top of the Default
/// or Dark theme. Every menu, panel, the Collab Mode windows and the loading screen logo's glow turn their blues and
/// teals to it, keeping how light or dark each part was. Until one is picked (and after Default), nothing changes.
/// </summary>
public static class MenuColor
{
    const string PrefColor = "Customize.MenuColor";     // RRGGBB, or empty for Moonscraper's own blue

    // Moonscraper's blue, as the picker shows it; picking exactly this looks the same as the default
    const float BaseHue = 0.56f, BaseSaturation = 0.85f, BaseValue = 0.85f;
    public static readonly Color DefaultColor = Color.HSVToRGB(BaseHue, BaseSaturation, BaseValue);

    static bool s_loaded;
    static Color? s_color;
    static Material s_material;
    static bool s_applied;      // The material is on the UI
    static float s_nextSweep;
    static readonly Dictionary<Graphic, Material> s_originalMaterials = new Dictionary<Graphic, Material>();

    /// <summary>The picked color, or null for Moonscraper's own blue.</summary>
    public static Color? color
    {
        get
        {
            if (!s_loaded)
            {
                s_loaded = true;
                Color parsed;
                string saved = PlayerPrefs.GetString(PrefColor, string.Empty);
                if (!string.IsNullOrEmpty(saved) && ColorUtility.TryParseHtmlString("#" + saved, out parsed))
                    s_color = parsed;
            }
            return s_color;
        }
        set
        {
            s_loaded = true;
            s_color = value;
            PlayerPrefs.SetString(PrefColor, value.HasValue ? ColorUtility.ToHtmlStringRGB(value.Value) : string.Empty);
            PlayerPrefs.Save();
            UpdateMaterial();
            if (wantedOnUi != s_applied)
                Apply();
            else
                ApplyToButtons(Resources.FindObjectsOfTypeAll<Selectable>());
            DuetSkin.Reset();
            // The title bar is matched to the menu bar's color
            if (CustomizationManager.instance)
                CustomizationManager.instance.MatchTitleBarToMenuBar(true);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => { Apply(); RefreshStarPowerIcon(); };
        Apply();    // The loading screen, already open

        GameObject holder = new GameObject("Menu Color");
        Object.DontDestroyOnLoad(holder);
        holder.AddComponent<Sweeper>();
    }

    // Menus made later (popups, lists filled in) get the color too
    class Sweeper : MonoBehaviour
    {
        void Update()
        {
            if (s_applied && Time.unscaledTime >= s_nextSweep)
            {
                s_nextSweep = Time.unscaledTime + 2;
                Apply();
            }
        }
    }

    static Material material
    {
        get
        {
            if (!s_material)
            {
                Shader shader = Resources.Load<Shader>("Shaders/MenuColor");
                if (!shader)
                {
                    Debug.LogWarning("Couldn't find the menu color shader");
                    return null;
                }
                s_material = new Material(shader);
                s_material.name = "Menu Color";
                UpdateMaterial();
            }
            return s_material;
        }
    }

    static void UpdateMaterial()
    {
        if (!s_material)
            return;

        float hueShift, saturationScale, valueScale;
        GetShift(out hueShift, out saturationScale, out valueScale);
        s_material.SetFloat("_HueShift", hueShift);
        s_material.SetFloat("_SaturationScale", saturationScale);
        s_material.SetFloat("_ValueScale", valueScale);
        s_material.SetFloat("_DarkNeutral", darkNeutral ? 1 : 0);
    }

    static bool darkNeutral { get { return UITheme.current == UITheme.Theme.Dark; } }

    // On or off for everything (the Dark theme also uses this material, for its near-black panels)
    static bool wantedOnUi { get { return color.HasValue || darkNeutral; } }

    /// <summary>The Light or Dark theme was picked.</summary>
    public static void OnThemeChanged()
    {
        UpdateMaterial();
        Apply();
        RefreshStarPowerIcon();
    }

    static void GetShift(out float hueShift, out float saturationScale, out float valueScale)
    {
        GetShift(color ?? DefaultColor, out hueShift, out saturationScale, out valueScale);
    }

    // How to turn Moonscraper's blue into this color
    static void GetShift(Color picked, out float hueShift, out float saturationScale, out float valueScale)
    {
        float hue, saturation, value;
        Color.RGBToHSV(picked, out hue, out saturation, out value);
        hueShift = hue - BaseHue;
        saturationScale = saturation / BaseSaturation;
        valueScale = value / BaseValue;
    }

    /// <summary>
    /// Puts the menu color on every menu and panel there is now (or takes it off for the default).
    /// </summary>
    public static void Apply()
    {
        ApplyToButtons(Resources.FindObjectsOfTypeAll<Selectable>());
        bool wanted = wantedOnUi && material;
        foreach (Graphic graphic in Resources.FindObjectsOfTypeAll<Graphic>())
        {
            if (graphic && graphic.gameObject.scene.IsValid())
                ApplyTo(graphic, wanted);
        }
        s_applied = wanted;
    }

    // The images that make up Moonscraper's look (panels, frames, buttons, sliders, boxes); anything else with its
    // own picture, such as the tool icons, keeps its colors
    static readonly HashSet<string> s_frameSprites = new HashSet<string>
    {
        "TestUISprite", "SliderBG", "settings_panel", "settings_panel_no_title", "settings_panel_resz", "panel1", "panel2",
        "panel3", "panel4", "panel_bg", "UISprite", "Background", "InputFieldBackground", "white_1", "Knob",
        "rect_knob_test", "1491902985_Checkbox Empty", "dropdown_arrow", "arrow", "handle_end", "UIMask", "dropshadow",
        "fade", "fade2", "fade3", "border", "WhiteBox", "settings_panel_resz",
        // The menu bar's blue icons
        "undo", "redo", "undo_grey", "redo_grey", "play", "play_highlight", "play_pressed", "pause", "pause_pressed",
    };

    // Buttons drawn as a plain box in their tint colors (no picture), such as the Settings tab buttons and the
    // Customize tab's Default: the tints themselves are turned to the menu color, as the shader can miss them
    static readonly Dictionary<Selectable, ColorBlock> s_originalBlocks = new Dictionary<Selectable, ColorBlock>();
    static readonly Dictionary<Selectable, ColorBlock> s_shiftedBlocks = new Dictionary<Selectable, ColorBlock>();
    static readonly HashSet<Graphic> s_tintedButtons = new HashSet<Graphic>();

    static void ApplyToButtons(Selectable[] selectables)
    {
        foreach (Selectable selectable in selectables)
        {
            if (!selectable || !selectable.gameObject.scene.IsValid() || selectable.transition != Selectable.Transition.ColorTint)
                continue;
            Image image = selectable.targetGraphic as Image;
            if (!image || image.sprite || UITheme.IsKept(image))
                continue;

            // Anything that set its own tints since (such as the Dark theme) counts as the original
            ColorBlock original, shifted;
            if (!s_originalBlocks.TryGetValue(selectable, out original)
                || !s_shiftedBlocks.TryGetValue(selectable, out shifted) || !SameBlock(shifted, selectable.colors))
            {
                original = selectable.colors;
                if (!s_originalBlocks.ContainsKey(selectable) && !HasBlueTint(original))
                    continue;   // White or grey tints, which the theme looks after
                s_originalBlocks[selectable] = original;
            }

            shifted = original;
            shifted.normalColor = Shift(original.normalColor);
            shifted.highlightedColor = Shift(original.highlightedColor);
            shifted.pressedColor = Shift(original.pressedColor);
            shifted.disabledColor = Shift(original.disabledColor);
            s_shiftedBlocks[selectable] = shifted;
            selectable.colors = shifted;
            s_tintedButtons.Add(image);
        }

        RemoveDestroyed(s_originalBlocks);
        RemoveDestroyed(s_shiftedBlocks);
        s_tintedButtons.RemoveWhere(graphic => !graphic);
    }

    /// <summary>A button whose tints the menu color looks after, so the theme leaves them alone.</summary>
    public static bool OwnsTints(Selectable selectable)
    {
        return s_originalBlocks.ContainsKey(selectable);
    }

    static bool HasBlueTint(ColorBlock block)
    {
        return IsBlue(block.normalColor) || IsBlue(block.highlightedColor) || IsBlue(block.pressedColor);
    }

    static bool IsBlue(Color color)
    {
        float hue, saturation, value;
        Color.RGBToHSV(color, out hue, out saturation, out value);
        return saturation > 0.18f && hue > 0.40f && hue < 0.77f;
    }

    static bool SameBlock(ColorBlock a, ColorBlock b)
    {
        return a.normalColor == b.normalColor && a.highlightedColor == b.highlightedColor
            && a.pressedColor == b.pressedColor && a.disabledColor == b.disabledColor;
    }

    static void RemoveDestroyed(Dictionary<Selectable, ColorBlock> map)
    {
        List<Selectable> gone = new List<Selectable>();
        foreach (Selectable key in map.Keys)
        {
            if (!key)
                gone.Add(key);
        }
        foreach (Selectable key in gone)
            map.Remove(key);
    }

    static bool IsIcon(Graphic graphic)
    {
        if (s_tintedButtons.Contains(graphic))
            return true;    // Already in the menu color through its tints
        Image image = graphic as Image;
        return image && image.sprite && !s_frameSprites.Contains(image.sprite.name);
    }

    static void ApplyTo(Graphic graphic, bool wanted)
    {
        if (graphic is TMPro.TMP_Text || UITheme.IsKept(graphic))
            return;     // Text Mesh Pro has its own shader; color swatches show the color itself

        Material current = graphic.material;
        if (wanted && IsIcon(graphic))
            wanted = false;     // Takes it off anything that had it before icons were left out
        if (wanted)
        {
            // Unity's own UI material, or Moonscraper's faster copy of it that most panels and outlines use
            if (current == graphic.defaultMaterial || IsPlainUiMaterial(current))
            {
                if (current != graphic.defaultMaterial)
                    s_originalMaterials[graphic] = current;
                graphic.material = material;
            }
        }
        else if (s_material && current == s_material)
        {
            Material original;
            graphic.material = s_originalMaterials.TryGetValue(graphic, out original) ? original : null;
            s_originalMaterials.Remove(graphic);
        }
    }

    const string StarPowerIconSprite = "sp_bulb_full_square_centered";
    static Material s_starPowerIconMaterial;

    /// <summary>
    /// The Star Power tool's icon in the color profile's Star Power color, or its own blue when the profile has none.
    /// </summary>
    public static void RefreshStarPowerIcon()
    {
        Color starPower;
        Chart.GameMode gameMode = ChartEditor.InstanceExists && ChartEditor.Instance.currentChart != null ? ChartEditor.Instance.currentChart.gameMode : Chart.GameMode.Guitar;
        bool custom = CustomNoteColors.TryGetStarPowerColor(gameMode, out starPower)
            || CustomNoteColors.TryGetStarPowerColor(Chart.GameMode.Guitar, out starPower);

        if (custom)
        {
            Shader shader = Resources.Load<Shader>("Shaders/MenuColor");
            if (!shader)
                return;
            if (!s_starPowerIconMaterial)
            {
                s_starPowerIconMaterial = new Material(shader);
                s_starPowerIconMaterial.name = "Star Power icon";
            }
            float hueShift, saturationScale, valueScale;
            GetShift(starPower, out hueShift, out saturationScale, out valueScale);
            s_starPowerIconMaterial.SetFloat("_HueShift", hueShift);
            s_starPowerIconMaterial.SetFloat("_SaturationScale", saturationScale);
            s_starPowerIconMaterial.SetFloat("_ValueScale", Mathf.Clamp(valueScale, 0.35f, 1.3f));
        }

        foreach (Image image in Resources.FindObjectsOfTypeAll<Image>())
        {
            if (!image || !image.gameObject.scene.IsValid() || !image.sprite || image.sprite.name != StarPowerIconSprite)
                continue;
            if (custom)
                image.material = s_starPowerIconMaterial;
            else if (image.material == s_starPowerIconMaterial)
                image.material = null;
        }
    }

    static bool IsPlainUiMaterial(Material material)
    {
        if (!material || material == s_material || !material.shader)
            return false;
        string shader = material.shader.name;
        return shader == "UI/Default" || shader == "UI/Fast-Default";
    }

    /// <summary>A color of Moonscraper's look in the menu color, for things drawn without the UI shader.</summary>
    public static Color Shift(Color original)
    {
        if (!wantedOnUi)
            return original;

        float hue, saturation, value;
        Color.RGBToHSV(original, out hue, out saturation, out value);
        float inRange = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.40f, 0.45f, hue)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.72f, 0.77f, hue)));
        float colored = Mathf.Clamp01((saturation - 0.08f) / 0.1f);
        float amount = inRange * colored;
        if (amount <= 0)
            return original;

        float hueShift, saturationScale, valueScale;
        GetShift(out hueShift, out saturationScale, out valueScale);
        Color shifted = Color.HSVToRGB(Mathf.Repeat(hue + hueShift, 1), Mathf.Clamp01(saturation * saturationScale), Mathf.Clamp01(value * valueScale));
        if (darkNeutral)
        {
            float darkPart = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.10f, 0.16f, value));
            float grey = Mathf.Max(value * 0.9f, 0.06f);
            shifted = Color.Lerp(shifted, new Color(grey, grey, grey), darkPart);
        }
        shifted = Color.Lerp(original, shifted, amount);
        shifted.a = original.a;
        return shifted;
    }

    /// <summary>A copy of a texture in the menu color (the same texture back for the default).</summary>
    public static Texture2D Shift(Texture2D original)
    {
        return Shift(original, Color.white);
    }

    /// <summary>A copy of a texture in the menu color, multiplied by tint (such as the Dark theme's).</summary>
    public static Texture2D Shift(Texture2D original, Color tint)
    {
        if ((!wantedOnUi && tint == Color.white) || !original)
            return original;

        // The texture may not be readable, so read it back from the GPU
        RenderTexture target = RenderTexture.GetTemporary(original.width, original.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previous = RenderTexture.active;
        Graphics.Blit(original, target);
        RenderTexture.active = target;
        Texture2D copy = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, original.width, original.height), 0, 0);
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(target);

        Color[] pixels = copy.GetPixels();
        for (int i = 0; i < pixels.Length; ++i)
        {
            Color shifted = Shift(pixels[i]);
            pixels[i] = new Color(shifted.r * tint.r, shifted.g * tint.g, shifted.b * tint.b, shifted.a);
        }
        copy.SetPixels(pixels);
        copy.wrapMode = original.wrapMode;
        copy.filterMode = original.filterMode;
        copy.Apply();
        copy.name = original.name + " (menu color)";
        return copy;
    }
}
