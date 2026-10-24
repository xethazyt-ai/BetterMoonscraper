// See LICENSE in project root for license information.

using UnityEngine;

/// <summary>
/// The look of the Collab Mode windows, matched to Moonscraper's own panels such as Settings: deep navy with a teal
/// rounded border, white text in Moonscraper's unispace font, dark teal buttons and white input boxes.
/// </summary>
public static class DuetSkin
{
    static readonly Color Panel = new Color32(0, 7, 22, 245);
    static readonly Color PanelBorder = new Color32(21, 90, 122, 255);
    static readonly Color Button = new Color32(0, 44, 58, 255);
    static readonly Color ButtonHover = new Color32(0, 74, 99, 255);
    static readonly Color ButtonPressed = new Color32(21, 114, 148, 255);
    static readonly Color ButtonOn = new Color32(17, 129, 220, 255);    // As the Tools button
    static readonly Color Field = new Color32(255, 255, 255, 255);
    static readonly Color FieldBorder = new Color32(120, 120, 120, 255);
    static readonly Color Track = new Color32(255, 255, 255, 255);
    static readonly Color Thumb = new Color32(185, 185, 185, 255);
    static readonly Color ScrollTrack = new Color32(0, 24, 40, 255);

    const string FontName = "unispace bold";   // Moonscraper's panels and inspectors use it

    static GUISkin s_skin;

    public static GUISkin skin
    {
        get
        {
            if (s_skin == null)
                s_skin = Make();
            return s_skin;
        }
    }

    // Moonscraper's font, if it's loaded (it always is once the editor is up)
    public static Font font
    {
        get
        {
            foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
            {
                if (f.name == FontName)
                    return f;
            }
            return null;
        }
    }

    static GUISkin Make()
    {
        GUISkin made = Object.Instantiate(GUI.skin);
        made.name = "Collab Mode";
        Font moonscraperFont = font;
        if (moonscraperFont != null)
            made.font = moonscraperFont;

        GUIStyle window = made.window;
        window.normal.background = window.onNormal.background = Rounded(Panel, PanelBorder, 3, 7);
        window.normal.textColor = window.onNormal.textColor = Color.white;
        window.border = new RectOffset(10, 10, 10, 10);
        window.padding = new RectOffset(10, 10, 28, 10);
        window.fontSize = 15;
        window.fontStyle = FontStyle.Normal;
        window.alignment = TextAnchor.UpperCenter;
        window.contentOffset = new Vector2(0, -20);

        foreach (GUIStyle text in new[] { made.label, made.toggle })
        {
            text.normal.textColor = text.hover.textColor = text.active.textColor = Color.white;
            text.onNormal.textColor = text.onHover.textColor = text.onActive.textColor = Color.white;
            text.fontSize = 13;
        }

        GUIStyle button = made.button;
        button.normal.background = Rounded(Button, PanelBorder, 1, 4);
        button.hover.background = Rounded(ButtonHover, PanelBorder, 1, 4);
        button.active.background = Rounded(ButtonPressed, PanelBorder, 1, 4);
        button.onNormal.background = button.onHover.background = Rounded(ButtonOn, PanelBorder, 1, 4);
        button.onActive.background = button.active.background;
        button.normal.textColor = button.hover.textColor = button.active.textColor = Color.white;
        button.onNormal.textColor = button.onHover.textColor = button.onActive.textColor = Color.white;
        button.border = new RectOffset(5, 5, 5, 5);
        button.fontSize = 13;

        foreach (GUIStyle field in new[] { made.textField, made.textArea })
        {
            field.normal.background = field.hover.background = field.focused.background = field.active.background = Rounded(Field, FieldBorder, 1, 2);
            field.normal.textColor = field.hover.textColor = field.focused.textColor = field.active.textColor = Color.black;
            field.border = new RectOffset(3, 3, 3, 3);
            field.padding = new RectOffset(5, 5, 3, 3);
            field.fontSize = 13;
        }
        made.settings.cursorColor = Color.black;
        made.settings.selectionColor = new Color32(17, 129, 220, 120);

        made.horizontalSlider.normal.background = Solid(Track);
        made.horizontalSlider.border = new RectOffset(0, 0, 0, 0);
        made.horizontalSlider.fixedHeight = 10;
        made.horizontalSlider.margin = new RectOffset(4, 4, 8, 8);
        GUIStyle thumb = made.horizontalSliderThumb;
        thumb.normal.background = thumb.hover.background = thumb.active.background = Solid(Thumb);
        thumb.border = new RectOffset(0, 0, 0, 0);
        thumb.fixedHeight = 10;
        thumb.fixedWidth = 26;

        made.verticalScrollbar.normal.background = Solid(ScrollTrack);
        made.verticalScrollbar.border = new RectOffset(0, 0, 0, 0);
        made.verticalScrollbarThumb.normal.background = Rounded(ButtonPressed, ButtonPressed, 0, 3);
        made.verticalScrollbarThumb.border = new RectOffset(4, 4, 4, 4);
        made.horizontalScrollbar.normal.background = Solid(ScrollTrack);
        made.horizontalScrollbarThumb.normal.background = Rounded(ButtonPressed, ButtonPressed, 0, 3);

        return made;
    }

    static Texture2D Solid(Color color)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;
        return texture;
    }

    // A rounded box with an outline, for nine-slicing (GUIStyle.border keeps the corners whole)
    static Texture2D Rounded(Color fill, Color outline, int outlineWidth, int radius)
    {
        const int Size = 24;
        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;

        // Signed distance to the rounded rectangle's edge: negative inside
        float half = Size / 2f;
        for (int x = 0; x < Size; ++x)
        {
            for (int y = 0; y < Size; ++y)
            {
                float qx = Mathf.Abs(x + 0.5f - half) - (half - radius);
                float qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude;
                float distance = outside + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;

                Color pixel = -distance <= outlineWidth ? outline : fill;
                pixel.a *= Mathf.Clamp01(0.5f - distance);     // Smooth edges
                texture.SetPixel(x, y, pixel);
            }
        }

        texture.Apply();
        return texture;
    }
}
