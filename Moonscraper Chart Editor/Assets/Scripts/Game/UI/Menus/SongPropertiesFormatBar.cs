// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The formatting buttons under Song Properties > General: bold, italic, underline, strikethrough, size and color for
/// the song details Clone Hero can show formatted. The fields show the text itself, drawn in its colors and styles
/// when not being typed in; the rich text tags behind it are only seen in the Advanced tab. Each field's tagged text
/// is kept here, and typing in a field makes the same change to it. Built in code from the panel's own controls.
/// </summary>
public class SongPropertiesFormatBar : MonoBehaviour
{
    const float Left = 52;
    public const float Right = 381.3f;
    const float RowHeight = 30;

    static readonly string[] s_swatches = { "#FF0000", "#FFA500", "#FFFF00", "#00FF00", "#0000FF", "#800080", "#FFFFFF" };

    InputField[] m_fields;
    InputField m_target;
    int m_selectionStart, m_selectionEnd;
    InputField m_sizeField;
    InputField m_colorField;
    RgbSliderPicker m_wheel;
    Text m_preview;
    string m_previewSource;
    Action m_onChanged;

    class Formatted
    {
        public string tagged = string.Empty;
        public Text overlay;            // Draws the formatted text over the field while it isn't being typed in
        public Color textColor;
        public string shown;
        public float shownWidth;
        public bool overflows;
    }
    readonly Dictionary<InputField, Formatted> m_formatted = new Dictionary<InputField, Formatted>();

    // Builds the bar below the last row of content. Returns how far down it goes.
    public static SongPropertiesFormatBar Build(RectTransform content, float top, InputField[] fields, InputField fieldTemplate, Text labelTemplate, Button buttonTemplate, UnityEngine.Events.UnityAction onLookUp, Action onChanged, out float bottom)
    {
        SongPropertiesFormatBar bar = content.gameObject.AddComponent<SongPropertiesFormatBar>();
        bar.m_fields = fields;
        bar.m_onChanged = onChanged;
        foreach (InputField field in fields)
            bar.AddOverlay(field);

        float y = top;
        Label(labelTemplate, content, "Formatting:", Left, y, 160);
        MakeButton(buttonTemplate, content, "Fill from name+artist", Right - 160, y, 160, onLookUp);
        y -= RowHeight + 2;

        float x = Left;
        x = bar.TagButton(buttonTemplate, content, "<b>B</b>", x, y, 34, "<b>", "</b>");
        x = bar.TagButton(buttonTemplate, content, "<i>I</i>", x, y, 34, "<i>", "</i>");
        x = bar.TagButton(buttonTemplate, content, "U", x, y, 34, "<u>", "</u>");
        x = bar.TagButton(buttonTemplate, content, "S", x, y, 34, "<s>", "</s>");
        x = MakeButton(buttonTemplate, content, "Clear", x, y, 56, bar.ClearFormatting);

        bar.m_sizeField = MakeField(fieldTemplate, content, "150", x, y, 44);
        bar.m_sizeField.contentType = InputField.ContentType.IntegerNumber;
        bar.m_sizeField.characterLimit = 3;
        x += 48;
        MakeButton(buttonTemplate, content, "Size %", x, y, Right - x, bar.ApplySize);
        y -= RowHeight + 4;

        x = Left;
        bar.m_colorField = MakeField(fieldTemplate, content, "#FFA500", x, y, 76);
        bar.m_colorField.characterLimit = 9;
        x += 80;
        x = MakeButton(buttonTemplate, content, "Color", x, y, 56, bar.ApplyColor);
        foreach (string swatch in s_swatches)
        {
            string hex = swatch;
            Button button = Instantiate(buttonTemplate, content);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => { bar.m_colorField.text = hex; bar.ApplyColor(); });
            Place(button.GetComponent<RectTransform>(), x, y, 22, 22);
            foreach (Text text in button.GetComponentsInChildren<Text>(true))
                text.text = string.Empty;
            Color color;
            if (button.targetGraphic && ColorUtility.TryParseHtmlString(hex, out color))
            {
                button.targetGraphic.color = color;
                UITheme.KeepColor(button.targetGraphic);    // Shows the color itself in any theme or menu color
            }
            x += 26;
        }
        y -= RowHeight + 4;

        // Any color: pick it with the sliders (as in Collab Mode), then press Color
        bar.m_wheel = RgbSliderPicker.Build(content, Left, y, Right - Left, labelTemplate.font, labelTemplate.color, null, false);
        bar.m_wheel.onPicked = picked => bar.m_colorField.text = "#" + ColorUtility.ToHtmlStringRGB(picked);
        bar.m_colorField.onValueChanged.AddListener(typed =>
        {
            Color parsed;
            string hex;
            if (RichTextIni.TryParseColor(typed, out hex) && ColorUtility.TryParseHtmlString(hex, out parsed) && !ColorsMatch(parsed, bar.m_wheel.color))
                bar.m_wheel.SetColor(parsed);
        });
        Color start;
        if (ColorUtility.TryParseHtmlString(bar.m_colorField.text, out start))
            bar.m_wheel.SetColor(start);
        y -= bar.m_wheel.height + 16;

        bar.m_preview = Label(labelTemplate, content, string.Empty, Left, y, Right - Left);
        bar.m_preview.supportRichText = true;
        bar.m_preview.alignment = TextAnchor.UpperLeft;
        bar.m_preview.horizontalOverflow = HorizontalWrapMode.Wrap;
        bar.m_preview.verticalOverflow = VerticalWrapMode.Overflow;
        Place(bar.m_preview.GetComponent<RectTransform>(), Left, y, Right - Left, 64);

        bottom = y - 68;
        return bar;
    }

    void Update()
    {
        foreach (InputField field in m_fields)
        {
            if (field && field.isFocused)
            {
                // Clicking a button takes the focus away and Unity forgets the selection, so keep it as it goes
                m_target = field;
                m_selectionStart = Math.Min(field.selectionAnchorPosition, field.selectionFocusPosition);
                m_selectionEnd = Math.Max(field.selectionAnchorPosition, field.selectionFocusPosition);
            }
        }

        foreach (InputField field in m_fields)
        {
            if (!field)
                continue;

            Sync(field);
            UpdateOverlay(field);
        }

        InputField shown = m_target ? m_target : m_fields[0];
        string source = shown ? GetTagged(shown) : string.Empty;
        if (m_preview && source != m_previewSource)
        {
            m_previewSource = source;
            m_preview.text = "Select text in a field above, then pick a format. For any color, set it with the sliders (or type it) and press Color. Underline and strikethrough only show in Clone Hero. The codes are in the Advanced tab.";
        }
    }

    void AddOverlay(InputField field)
    {
        Formatted formatted = new Formatted();
        formatted.tagged = field.text;
        m_formatted[field] = formatted;

        Text text = field.textComponent;
        if (!text)
            return;

        formatted.textColor = text.color;
        formatted.overlay = Instantiate(text, text.transform.parent);
        formatted.overlay.name = "Formatted Text";
        formatted.overlay.supportRichText = true;
        formatted.overlay.raycastTarget = false;
        formatted.overlay.horizontalOverflow = HorizontalWrapMode.Overflow;
        formatted.overlay.verticalOverflow = VerticalWrapMode.Overflow;
        formatted.overlay.enabled = false;

        // Shrunk from its left edge when the text is wider than the box, so a long name still shows in full
        RectTransform rect = formatted.overlay.rectTransform;
        Vector2 offsetMin = rect.offsetMin, offsetMax = rect.offsetMax;
        rect.pivot = new Vector2(0, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        if (!field.GetComponent<RectMask2D>())
            field.gameObject.AddComponent<RectMask2D>();    // Long or big text stays inside the box
    }

    void UpdateOverlay(InputField field)
    {
        Formatted formatted;
        if (!m_formatted.TryGetValue(field, out formatted) || !formatted.overlay)
            return;

        RectTransform rect = formatted.overlay.rectTransform;
        float width = rect.rect.width;
        if (formatted.shown != formatted.tagged || formatted.shownWidth != width)
        {
            formatted.shown = formatted.tagged;
            formatted.shownWidth = width;
            int size = formatted.overlay.fontSize;
            formatted.overlay.text = RichTextIni.ToPreview(formatted.tagged, size, size * 3 / 2);
            float needed = formatted.overlay.preferredWidth;
            formatted.overflows = width > 0 && needed > width;
            float scale = formatted.overflows ? width / needed : 1;
            rect.localScale = new Vector3(scale, scale, 1);
        }

        // Plain text that fits is left to the field itself
        bool show = !field.isFocused && (formatted.overflows || RichTextIni.HasTags(formatted.tagged));
        if (formatted.overlay.enabled != show)
        {
            formatted.overlay.enabled = show;
            Color color = formatted.textColor;
            if (show)
                color.a = 0;    // The field's own plain text hides behind the formatted one
            field.textComponent.color = color;
        }
    }

    // The field's text with its formatting, as it goes in song.ini
    public string GetTagged(InputField field)
    {
        Formatted formatted;
        if (!m_formatted.TryGetValue(field, out formatted))
            return field.text;

        Sync(field);
        return formatted.tagged;
    }

    // Shows tagged text in the field: the text itself, formatted
    public void SetTagged(InputField field, string tagged)
    {
        Formatted formatted;
        if (!m_formatted.TryGetValue(field, out formatted))
        {
            field.text = tagged;
            return;
        }

        formatted.tagged = RichTextIni.Normalise(tagged ?? string.Empty);
        string plain = RichTextIni.Strip(formatted.tagged);
        if (field.text != plain)
            field.text = plain;
        UpdateOverlay(field);
    }

    // Catches up with typing, pasting or anything else that changed the field's plain text
    void Sync(InputField field)
    {
        Formatted formatted;
        if (m_formatted.TryGetValue(field, out formatted) && RichTextIni.Strip(formatted.tagged) != field.text)
            formatted.tagged = RichTextIni.ApplyPlainEdit(formatted.tagged, field.text);
    }

    // Shows a message where the preview goes, until the text changes
    public void SetStatus(string message)
    {
        if (m_preview)
            m_preview.text = message;
    }

    InputField target { get { return m_target ? m_target : m_fields[0]; } }

    void Wrap(string open, string close)
    {
        InputField field = target;
        SetFormatted(field, RichTextIni.WrapVisible(GetTagged(field), m_selectionStart, m_selectionEnd, open, close));
    }

    void ClearFormatting()
    {
        InputField field = target;
        SetFormatted(field, RichTextIni.ClearVisible(GetTagged(field), m_selectionStart, m_selectionEnd));
    }

    // The words don't change, so the field doesn't tell the song: say so here
    void SetFormatted(InputField field, string tagged)
    {
        SetTagged(field, tagged);
        if (m_onChanged != null)
            m_onChanged();
    }

    void ApplySize()
    {
        int percent;
        if (!int.TryParse(m_sizeField.text, out percent) || percent <= 0)
            percent = 100;
        percent = Mathf.Clamp(percent, 10, 400);
        m_sizeField.text = percent.ToString();
        Wrap("<size=" + percent + "%>", "</size>");
    }

    void ApplyColor()
    {
        string hex;
        if (!RichTextIni.TryParseColor(m_colorField.text, out hex))
        {
            m_colorField.text = "#FFA500";
            return;
        }

        m_colorField.text = hex;
        Wrap("<color=" + hex + ">", "</color>");
    }

    static bool ColorsMatch(Color a, Color b)
    {
        return ColorUtility.ToHtmlStringRGB(a) == ColorUtility.ToHtmlStringRGB(b);
    }

    float TagButton(Button template, RectTransform parent, string label, float x, float y, float width, string open, string close)
    {
        return MakeButton(template, parent, label, x, y, width, () => Wrap(open, close));
    }

    static float MakeButton(Button template, RectTransform parent, string label, float x, float y, float width, UnityEngine.Events.UnityAction onClick)
    {
        Button button = Instantiate(template, parent);
        button.name = label + " Format Button";
        button.onClick = new Button.ButtonClickedEvent();     // Drop the template's own click handler
        button.onClick.AddListener(onClick);
        foreach (Text text in button.GetComponentsInChildren<Text>(true))
        {
            text.supportRichText = true;
            text.text = label;
        }
        Place(button.GetComponent<RectTransform>(), x, y, width, RowHeight);
        return x + width + 4;
    }

    static InputField MakeField(InputField template, RectTransform parent, string text, float x, float y, float width)
    {
        InputField field = Instantiate(template, parent);
        field.onValueChanged = new InputField.OnChangeEvent();     // These don't change the song
        field.onEndEdit = new InputField.SubmitEvent();
        field.text = text;
        Place(field.GetComponent<RectTransform>(), x, y, width, RowHeight);
        return field;
    }

    static Text Label(Text template, RectTransform parent, string text, float x, float y, float width)
    {
        Text label = Instantiate(template, parent);
        label.text = text;
        Place(label.rectTransform, x, y, width, RowHeight);
        return label;
    }

    // x is the left edge, y the top edge, both from the content's top left
    public static void Place(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }
}
