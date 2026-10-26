// See LICENSE in project root for license information.

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
            if (value.HasValue != s_applied)
                Apply();
            DuetSkin.Reset();
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => Apply();
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
    }

    static void GetShift(out float hueShift, out float saturationScale, out float valueScale)
    {
        Color picked = color ?? DefaultColor;
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
        bool wanted = color.HasValue && material;
        foreach (Graphic graphic in Resources.FindObjectsOfTypeAll<Graphic>())
        {
            if (graphic && graphic.gameObject.scene.IsValid())
                ApplyTo(graphic, wanted);
        }
        s_applied = wanted;
    }

    static void ApplyTo(Graphic graphic, bool wanted)
    {
        if (graphic is TMPro.TMP_Text || UITheme.IsKept(graphic))
            return;     // Text Mesh Pro has its own shader; color swatches show the color itself

        Material current = graphic.material;
        if (wanted)
        {
            if (current == graphic.defaultMaterial)
                graphic.material = material;
        }
        else if (s_material && current == s_material)
        {
            graphic.material = null;
        }
    }

    /// <summary>A color of Moonscraper's look in the menu color, for things drawn without the UI shader.</summary>
    public static Color Shift(Color original)
    {
        if (!color.HasValue)
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
        shifted = Color.Lerp(original, shifted, amount);
        shifted.a = original.a;
        return shifted;
    }

    /// <summary>A copy of a texture in the menu color (the same texture back for the default).</summary>
    public static Texture2D Shift(Texture2D original)
    {
        if (!color.HasValue || !original)
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
            pixels[i] = Shift(pixels[i]);
        copy.SetPixels(pixels);
        copy.wrapMode = original.wrapMode;
        copy.filterMode = original.filterMode;
        copy.Apply();
        copy.name = original.name + " (menu color)";
        return copy;
    }
}
