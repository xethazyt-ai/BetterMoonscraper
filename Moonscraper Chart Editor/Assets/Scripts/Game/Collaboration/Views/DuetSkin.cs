// See LICENSE in project root for license information.

using UnityEngine;

/// <summary>
/// The look of the Collab Mode and Pattern Blueprints windows, made from Moonscraper's own Settings and Tools panels:
/// the same panel image, the title in a teal tab like "< Settings >", text in Yuri, Moonscraper's input boxes and
/// dark teal buttons. Follows the Light or Dark theme and the menu color picked in Settings > Customize.
/// </summary>
public static class DuetSkin
{
    static Color Panel { get { return MenuColor.Shift(new Color32(0, 7, 22, 245)); } }
    static Color PanelBorder { get { return MenuColor.Shift(new Color32(21, 90, 122, 255)); } }
    static Color Button { get { return MenuColor.Shift(new Color32(0, 44, 58, 255)); } }
    static Color ButtonHover { get { return MenuColor.Shift(new Color32(0, 74, 99, 255)); } }
    static Color ButtonPressed { get { return MenuColor.Shift(new Color32(21, 114, 148, 255)); } }
    static Color ButtonOn { get { return MenuColor.Shift(new Color32(17, 129, 220, 255)); } }    // As the Tools button
    static Color Title { get { return MenuColor.Shift(new Color32(0, 62, 82, 255)); } }    // As the Settings tab
    static bool dark { get { return UITheme.current == UITheme.Theme.Dark; } }
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
    // The Settings panel and its tab's image, at the size they show in Moonscraper (its sprites are drawn at half size)
    const string PanelResource = "Collab/Collab Settings Panel";
    const string TitleResource = "Collab/Collab Title";
    const string BoxResource = "Collab/Collab Box";
    static readonly RectOffset PanelBorderSlices = new RectOffset(8, 9, 9, 8);
    static readonly RectOffset TitleBorderSlices = new RectOffset(6, 5, 6, 7);
    const int TitleBarHeight = 34;
    public const float TitleHeight = 22;

    static GUISkin s_skin;
    static GUIStyle s_swatch;
    static GUIStyle s_title;
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

    // The window's title in a tab at the top middle, like the Settings panel's
    public static GUIStyle title
    {
        get
        {
            if (s_title == null)
            {
                s_title = new GUIStyle(skin.label);
                Texture2D tab = Resources.Load<Texture2D>(TitleResource);
                s_title.normal.background = tab != null ? Tinted(tab, Title) : Rounded(Title, Title, 0, 8);
                s_title.border = TitleBorderSlices;
                s_title.normal.textColor = Color.white;
                s_title.font = skin.font;      // Yuri, as the Settings panel's "< Settings >" tab
                s_title.fontSize = FontSize;
                s_title.alignment = TextAnchor.MiddleCenter;
                s_title.wordWrap = false;
                s_title.padding = new RectOffset(14, 14, 1, 1);
                s_title.margin = new RectOffset(0, 0, 0, 0);
            }
            return s_title;
        }
    }

    /// <summary>Draws the title tab centred at the top of a window this wide, in the window's own coordinates.</summary>
    public static void DrawTitle(string text, float windowWidth)
    {
        if (string.IsNullOrEmpty(text) || UnityEngine.Event.current.type != EventType.Repaint)
            return;
        GUIContent content = new GUIContent(text);
        float width = Mathf.Min(title.CalcSize(content).x, windowWidth - 16);
        title.Draw(new Rect((windowWidth - width) / 2, (TitleBarHeight - TitleHeight) / 2 + 3, width, TitleHeight), content, false, false, false, false);
    }

    // Made again with the menu color or theme picked in Settings > Customize
    public static void Reset()
    {
        s_skin = null;
        s_swatch = null;
        s_title = null;
    }

    // A white image in a color, keeping its alpha
    static Texture2D Tinted(Texture2D white, Color color)
    {
        Texture2D copy = MenuColor.Shift(white, new Color(color.r, color.g, color.b, 1));
        return copy;
    }

    static GUISkin Make()
    {
        GUISkin made = Object.Instantiate(GUI.skin);
        made.name = "Collab Mode";
        Font textFont = Resources.Load<Font>(TextFontResource);
        if (textFont != null)
            made.font = textFont;

        GUIStyle window = made.window;
        Texture2D panel = MenuColor.Shift(Resources.Load<Texture2D>(PanelResource), dark ? UITheme.DarkPanelTint : Color.white);
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
        // The title is drawn as a tab by DrawTitle, so the window draws none of its own
        window.normal.textColor = window.onNormal.textColor = new Color(0, 0, 0, 0);
        window.padding = new RectOffset(10, 10, TitleBarHeight + 6, 10);
        window.fontSize = 1;
        window.contentOffset = Vector2.zero;

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

        // White boxes, or dark ones with light writing in the Dark theme, as Moonscraper's own
        Texture2D box = Resources.Load<Texture2D>(BoxResource);
        if (box != null && dark)
            box = MenuColor.Shift(box, UITheme.DarkField);
        Color fieldText = dark ? UITheme.LightText : Color.black;
        foreach (GUIStyle field in new[] { made.textField, made.textArea })
        {
            field.normal.background = field.hover.background = field.focused.background = field.active.background = box != null ? box : Rounded(dark ? UITheme.DarkField : Field, FieldBorder, 1, 2);
            field.normal.textColor = field.hover.textColor = field.focused.textColor = field.active.textColor = fieldText;
            field.border = box != null ? new RectOffset(7, 7, 7, 7) : new RectOffset(3, 3, 3, 3);
            field.padding = new RectOffset(6, 6, 5, 5);
            field.fontSize = FontSize;
        }
        made.settings.cursorColor = fieldText;
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
