// See LICENSE in project root for license information.

using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Collab Mode color picker for Moonscraper's own menus: optional color buttons, then Red, Green and Blue sliders
/// and an optional hex box, built in code. Tells onPicked each time the color changes.
/// </summary>
public class RgbSliderPicker : MonoBehaviour
{
    public const float RowHeight = 24;
    const float LabelWidth = 52;

    // As the Collab Mode picker: red, orange, yellow, green, cyan, blue, purple and pink, as their web colors
    public static readonly string[] BasicColors = { "#FF0000", "#FFA500", "#FFFF00", "#00FF00", "#00FFFF", "#0000FF", "#800080", "#FFC0CB" };

    public Action<Color> onPicked;

    readonly Slider[] m_sliders = new Slider[3];
    InputField m_hex;
    Image m_preview;
    bool m_setting;

    public Color color
    {
        get { return new Color(m_sliders[0].value / 255f, m_sliders[1].value / 255f, m_sliders[2].value / 255f); }
    }

    public float height { get; private set; }

    // Builds the picker with its top left at (x, y) from the parent's top left. swatches and the hex box are optional.
    public static RgbSliderPicker Build(RectTransform parent, float x, float y, float width, Font font, Color textColor, string[] swatches, bool withHex)
    {
        GameObject root = new GameObject("Color Picker", typeof(RectTransform));
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.SetParent(parent, false);
        RgbSliderPicker picker = root.AddComponent<RgbSliderPicker>();

        float rowY = 0;
        if (swatches != null && swatches.Length > 0)
        {
            float gap = 4;
            float size = Mathf.Min(RowHeight, (width - gap * (swatches.Length - 1)) / swatches.Length);
            for (int i = 0; i < swatches.Length; ++i)
            {
                Color swatchColor;
                if (!ColorUtility.TryParseHtmlString(swatches[i], out swatchColor))
                    continue;

                Image swatch = MakeImage(rootRect, "Swatch " + swatches[i], i * (size + gap), rowY, size, size, swatchColor);
                Button button = swatch.gameObject.AddComponent<Button>();
                button.targetGraphic = swatch;
                Color picked = swatchColor;
                button.onClick.AddListener(() => picker.Pick(picked));
                UITheme.KeepColor(swatch);
            }
            rowY -= RowHeight + 4;
        }

        string[] names = { "Red", "Green", "Blue" };
        for (int i = 0; i < 3; ++i)
        {
            MakeText(rootRect, names[i], 0, rowY, LabelWidth, RowHeight, font, textColor);
            picker.m_sliders[i] = MakeSlider(rootRect, LabelWidth, rowY, width - LabelWidth - RowHeight - 6);
            picker.m_sliders[i].onValueChanged.AddListener(value => picker.OnSlider());
            rowY -= RowHeight;
        }

        // What the sliders make, next to them
        picker.m_preview = MakeImage(rootRect, "Preview", width - RowHeight, rowY + RowHeight * 3 - 2, RowHeight, RowHeight * 3 - 4, Color.white);
        picker.m_preview.raycastTarget = false;
        UITheme.KeepColor(picker.m_preview);

        if (withHex)
        {
            MakeText(rootRect, "Hex", 0, rowY, LabelWidth, RowHeight, font, textColor);
            picker.m_hex = MakeHexField(rootRect, LabelWidth, rowY - 1, 110, RowHeight - 2, font);
            picker.m_hex.onEndEdit.AddListener(typed => picker.OnHexTyped(typed));
            rowY -= RowHeight;
        }

        picker.height = -rowY;
        SongPropertiesFormatBar.Place(rootRect, x, y, width, picker.height);
        picker.Refresh(false);
        return picker;
    }

    public void SetColor(Color color)
    {
        m_setting = true;
        m_sliders[0].value = Mathf.Round(color.r * 255);
        m_sliders[1].value = Mathf.Round(color.g * 255);
        m_sliders[2].value = Mathf.Round(color.b * 255);
        m_setting = false;
        Refresh(false);
    }

    void Pick(Color color)
    {
        SetColor(color);
        Refresh(true);
    }

    void OnSlider()
    {
        if (!m_setting)
            Refresh(true);
    }

    void OnHexTyped(string typed)
    {
        Color parsed;
        string hex = typed.Trim();
        if (!hex.StartsWith("#"))
            hex = "#" + hex;
        if (ColorUtility.TryParseHtmlString(hex, out parsed))
            Pick(parsed);
        else
            Refresh(false);     // Put the current color back
    }

    void Refresh(bool tell)
    {
        Color now = color;
        if (m_preview)
            m_preview.color = now;
        if (m_hex && !m_hex.isFocused)
            m_hex.text = "#" + ColorUtility.ToHtmlStringRGB(now);
        if (tell && onPicked != null)
            onPicked(now);
    }

    static Image MakeImage(RectTransform parent, string name, float x, float y, float width, float height, Color color)
    {
        GameObject made = new GameObject(name, typeof(RectTransform));
        made.transform.SetParent(parent, false);
        Image image = made.AddComponent<Image>();
        image.color = color;
        SongPropertiesFormatBar.Place(image.rectTransform, x, y, width, height);
        return image;
    }

    static Text MakeText(RectTransform parent, string text, float x, float y, float width, float height, Font font, Color color)
    {
        GameObject made = new GameObject(text, typeof(RectTransform));
        made.transform.SetParent(parent, false);
        Text label = made.AddComponent<Text>();
        label.font = font;
        label.fontSize = 15;
        label.color = color;
        label.text = text;
        label.alignment = TextAnchor.MiddleLeft;
        label.raycastTarget = false;
        SongPropertiesFormatBar.Place(label.rectTransform, x, y, width, height);
        return label;
    }

    // A thin white track with a grey handle, like the Collab Mode sliders
    static Slider MakeSlider(RectTransform parent, float x, float y, float width)
    {
        GameObject made = new GameObject("Slider", typeof(RectTransform));
        made.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)made.transform;
        SongPropertiesFormatBar.Place(rect, x, y, width, RowHeight);

        // A clear background, so the whole row takes clicks
        Image clickArea = made.AddComponent<Image>();
        clickArea.color = new Color(0, 0, 0, 0);

        Image track = MakeImage(rect, "Track", 0, 0, 0, 0, Color.white);
        track.raycastTarget = false;
        Stretch(track.rectTransform, 0, 0.5f, 1, 0.5f, new Vector2(0, 3));
        UITheme.KeepColor(track);

        // The slider stretches the handle to the area's height, so the area is the handle's height
        GameObject areaObject = new GameObject("Handle Slide Area", typeof(RectTransform));
        areaObject.transform.SetParent(rect, false);
        RectTransform area = (RectTransform)areaObject.transform;
        Stretch(area, 0, 0.5f, 1, 0.5f, new Vector2(-14, 10));

        Image handle = MakeImage(area, "Handle", 0, 0, 0, 0, new Color32(185, 185, 185, 255));
        Stretch(handle.rectTransform, 0, 0, 0, 1, new Vector2(14, 0));
        UITheme.KeepColor(handle);

        Slider slider = made.AddComponent<Slider>();
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0;
        slider.maxValue = 255;
        slider.wholeNumbers = true;
        ColorBlock colors = slider.colors;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1);
        slider.colors = colors;
        return slider;
    }

    static InputField MakeHexField(RectTransform parent, float x, float y, float width, float height, Font font)
    {
        Image box = MakeImage(parent, "Hex", x, y, width, height, Color.white);
        InputField field = box.gameObject.AddComponent<InputField>();
        field.targetGraphic = box;
        field.characterLimit = 7;

        Text text = MakeText(box.rectTransform, string.Empty, 0, 0, 0, 0, font, new Color(0.1f, 0.1f, 0.1f));
        text.supportRichText = false;
        Stretch(text.rectTransform, 0, 0, 1, 1, new Vector2(-10, -2));
        field.textComponent = text;
        return field;
    }

    static void Stretch(RectTransform rect, float minX, float minY, float maxX, float maxY, Vector2 sizeDelta)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = sizeDelta;
    }
}
