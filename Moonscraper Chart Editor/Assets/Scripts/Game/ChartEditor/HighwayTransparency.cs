// See LICENSE in project root for license information.

using UnityEngine;

/// <summary>
/// How see-through the highway is, from 0% (as normal) to 100% (gone, so only the background shows). Set from the
/// left panel under Gem Size; only this computer's view changes.
/// </summary>
public static class HighwayTransparency
{
    const string PrefPercent = "Display.HighwayTransparencyPercent";
    public const int MinPercent = 0, MaxPercent = 100, DefaultPercent = 0;

    static int? s_percent;
    static Material s_material;
    static Shader s_originalShader;
    static int s_originalRenderQueue;
    const int FadeRenderQueue = 2950;   // After the solid notes, before see-through things such as beat lines
    static Shader s_fadeShader;

    public static int percent
    {
        get
        {
            if (!s_percent.HasValue)
                s_percent = Mathf.Clamp(PlayerPrefs.GetInt(PrefPercent, DefaultPercent), MinPercent, MaxPercent);
            return s_percent.Value;
        }
        set
        {
            value = Mathf.Clamp(value, MinPercent, MaxPercent);
            if (value == percent)
                return;

            s_percent = value;
            PlayerPrefs.SetInt(PrefPercent, value);
            PlayerPrefs.Save();
            Apply();
        }
    }

    /// <summary>
    /// Puts the current setting on the highway. The fretboard's material is shared, and the highway picked in
    /// Settings > Customize, its tiling and its scrolling all go on that same material, so only its shader is swapped.
    /// </summary>
    public static void Apply()
    {
        if (!s_material)
        {
            FretboardWrapMovement fretboard = Object.FindObjectOfType<FretboardWrapMovement>();
            Renderer renderer = fretboard ? fretboard.GetComponent<Renderer>() : null;
            if (!renderer || !renderer.sharedMaterial)
                return;

            s_material = renderer.sharedMaterial;
            s_originalShader = s_material.shader;
            s_originalRenderQueue = s_material.renderQueue;
            s_fadeShader = Resources.Load<Shader>("Shaders/HighwayFade");
            if (!s_fadeShader)
                Debug.LogWarning("Couldn't find the highway transparency shader");
        }

        if (percent <= 0 || !s_fadeShader)
        {
            Restore();
            return;
        }

        if (s_material.shader != s_fadeShader)
            s_material.shader = s_fadeShader;
        s_material.renderQueue = FadeRenderQueue;   // The material sets its own queue, which would win over the shader's
        s_material.SetColor("_Color", new Color(1, 1, 1, 1 - percent / 100f));
    }

    // The material is a shared asset, so this also stops the change sticking in the Unity editor
    public static void Restore()
    {
        if (s_material && s_originalShader && s_material.shader != s_originalShader)
        {
            s_material.shader = s_originalShader;
            s_material.renderQueue = s_originalRenderQueue;
        }
    }
}
