// See LICENSE in project root for license information.

using UnityEngine;

/// <summary>
/// The look of the Collab Mode windows, made from Moonscraper's own panels such as the Note inspector: its panel image
/// with the blue title bar, the title in unispace, other text in Yuri like the Settings panel, Moonscraper's white
/// input boxes, and dark teal buttons.
/// </summary>
public static class DuetSkin
{
    static Color Panel { get { return MenuColor.Shift(new Color32(0, 7, 22, 245)); } }
    static Color PanelBorder { get { return MenuColor.Shift(new Color32(21, 90, 122, 255)); } }
    static Color Button { get { return MenuColor.Shift(new Color32(0, 44, 58, 255)); } }
    static Color ButtonHover { get { return MenuColor.Shift(new Color32(0, 74, 99, 255)); } }
    static Color ButtonPressed { get { return MenuColor.Shift(new Color32(21, 114, 148, 255)); } }
    static Color ButtonOn { get { return MenuColor.Shift(new Color32(17, 129, 220, 255)); } }    // As the Tools button
    static readonly Color Field = new Color32(255, 255, 255, 255);
    static readonly Color FieldBorder = new Color32(120, 120, 120, 255);
    static readonly Color Track = new Color32(255, 255, 255, 255);
    static readonly Color Thumb = new Color32(185, 185, 185, 255);
    static Color ScrollTrack { get { return MenuColor.Shift(new Color32(0, 24, 40, 255)); } }

    public const int FontSize = 15;    // As the Settings panel

    // Copies of Moonscraper's own art. Its unispace bold is imported at a fixed size (32 pixels), which IMGUI can't
    // scale, so the copy is imported as a dynamic font. The panel and box images keep Moonscraper's slicing.
    const string FontResource = "Collab/Collab Font";
    const string TextFontResource = "Collab/Collab Text Font";
    const string PanelResource = "Collab/Collab Panel";
    const string BoxResource = "Collab/Collab Box";
    static readonly RectOffset PanelBorderSlices = new RectOffset(3, 4, 28, 4);    // As its sprite: title bar on top
    const int TitleBarHeight = 28;

    static GUISkin s_skin;
    static GUIStyle s_swatch;
    static Font s_font;

    public static GUISkin skin
    {
        get
        {
            if (s_skin == null)
                s_skin = Make();
            return s_skin;
        }
    }

    // Moonscraper's font, at a size of our choosing
    public static Font font
    {
        get
        {
            if (s_font == null)
                s_font = Resources.Load<Font>(FontResource);
            return s_font;
        }
    }

    // A plain color box for the color picker, tinted by GUI.backgroundColor. White outline when picked.
    public static GUIStyle swatch
    {
        get
        {
            if (s_swatch == null)
            {
                s_swatch = new GUIStyle(skin.button);
                s_swatch.normal.background = s_swatch.hover.background = s_swatch.active.background = Rounded(Color.white, new Color32(150, 150, 150, 255), 1, 4);
                s_swatch.onNormal.background = s_swatch.onHover.background = s_swatch.onActive.background = Rounded(Color.white, Color.white, 3, 4);
                s_swatch.normal.textColor = s_swatch.hover.textColor = s_swatch.active.textColor = Color.black;
                s_swatch.onNormal.textColor = s_swatch.onHover.textColor = s_swatch.onActive.textColor = Color.black;
            }
            return s_swatch;
        }
    }

    // Made again with the menu color picked in Settings > Customize
    public static void Reset()
    {
        s_skin = null;
        s_swatch = null;
    }

    static GUISkin Make()
    {
        GUISkin made = Object.Instantiate(GUI.skin);
        made.name = "Collab Mode";
        Font textFont = Resources.Load<Font>(TextFontResource);
        if (textFont != null)
            made.font = textFont;

        GUIStyle window = made.window;
        Texture2D panel = MenuColor.Shift(Resources.Load<Texture2D>(PanelResource));
        if (panel != null)
        {
            window.normal.background = window.onNormal.background = panel;
            window.border = PanelBorderSlices;
        }
        else
        {
            window.normal.background = window.onNormal.background = Rounded(Panel, PanelBorder, 3, 7);
            window.border = new RectOffset(10, 10, 10, 10);
        }
        window.normal.textColor = window.onNormal.textColor = Color.white;
        window.padding = new RectOffset(8, 8, TitleBarHeight + 6, 8);
        if (font != null)
            window.font = font;
        window.fontSize = FontSize + 1;
        window.fontStyle = FontStyle.Normal;
        window.alignment = TextAnchor.UpperCenter;
        // The title is placed from the top of the contents, so this lifts it into the middle of the title bar
        window.contentOffset = new Vector2(0, -(TitleBarHeight + 6) + (TitleBarHeight - (FontSize + 5)) / 2);

        foreach (GUIStyle text in new[] { made.label, made.toggle })
        {
            text.normal.textColor = text.hover.textColor = text.active.textColor = Color.white;
            text.onNormal.textColor = text.onHover.textColor = text.onActive.textColor = Color.white;
            text.fontSize = FontSize;
            text.wordWrap = true;
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
        button.fontSize = FontSize;
        button.wordWrap = true;     // A long name goes onto two lines rather than past the window's edge

        Texture2D box = Resources.Load<Texture2D>(BoxResource);
        foreach (GUIStyle field in new[] { made.textField, made.textArea })
        {
            field.normal.background = field.hover.background = field.focused.background = field.active.background = box != null ? box : Rounded(Field, FieldBorder, 1, 2);
            field.normal.textColor = field.hover.textColor = field.focused.textColor = field.active.textColor = Color.black;
            field.border = box != null ? new RectOffset(7, 7, 7, 7) : new RectOffset(3, 3, 3, 3);
            field.padding = new RectOffset(6, 6, 5, 5);
            field.fontSize = FontSize;
        }
        made.settings.cursorColor = Color.black;
        made.settings.selectionColor = MenuColor.Shift(new Color32(17, 129, 220, 120));

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
