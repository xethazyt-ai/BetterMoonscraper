// See LICENSE in project root for license information.

using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The formatting buttons under Song Properties > General: bold, italic, underline, strikethrough, size and color for
/// the song details Clone Hero can show formatted. They wrap the selected text (or all of it) in rich text tags, and
/// a preview shows roughly how it will look. Built in code from the panel's own controls.
/// </summary>
public class SongPropertiesFormatBar : MonoBehaviour
{
    const float Left = 52;
    const float Right = 381.3f;
    const float RowHeight = 30;

    static readonly string[] s_swatches = { "#FF4040", "#FF9900", "#FFE14D", "#40E040", "#40A0FF", "#B060FF", "#FFFFFF" };

    InputField[] m_fields;
    InputField m_target;
    int m_selectionStart, m_selectionEnd;
    InputField m_sizeField;
    InputField m_colorField;
    Text m_preview;
    string m_previewSource;

    // Builds the bar below the last row of content. Returns how far down it goes.
    public static SongPropertiesFormatBar Build(RectTransform content, float top, InputField[] fields, InputField fieldTemplate, Text labelTemplate, Button buttonTemplate, UnityEngine.Events.UnityAction onLookUp, out float bottom)
    {
        SongPropertiesFormatBar bar = content.gameObject.AddComponent<SongPropertiesFormatBar>();
        bar.m_fields = fields;

        float y = top;
        Label(labelTemplate, content, "Formatting:", Left, y, 160);
        MakeButton(buttonTemplate, content, "Fill from MusicBrainz", Right - 160, y, 160, onLookUp);
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
        bar.m_colorField = MakeField(fieldTemplate, content, "#FF9900", x, y, 76);
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
                button.targetGraphic.color = color;
            x += 26;
        }
        y -= RowHeight + 4;

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

        InputField shown = m_target ? m_target : m_fields[0];
        if (m_preview && shown && shown.text != m_previewSource)
        {
            m_previewSource = shown.text;
            m_preview.text = RichTextIni.HasTags(m_previewSource)
                ? "Preview: " + RichTextIni.ToPreview(m_previewSource, m_preview.fontSize)
                : "Select text in a field above, then pick a format. Clone Hero shows it from song.ini.";
        }
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
        field.text = RichTextIni.Wrap(field.text, ref m_selectionStart, ref m_selectionEnd, open, close);
    }

    void ClearFormatting()
    {
        InputField field = target;
        field.text = RichTextIni.Clear(field.text, ref m_selectionStart, ref m_selectionEnd);
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
            m_colorField.text = "#FF9900";
            return;
        }

        m_colorField.text = hex;
        Wrap("<color=" + hex + ">", "</color>");
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
