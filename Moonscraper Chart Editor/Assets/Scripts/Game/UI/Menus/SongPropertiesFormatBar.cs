// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
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
    const float RowGap = 10;         // Between rows of buttons
    const float SectionGap = 20;
    const float PreviewHeight = 110;     // Around the color sliders and the help text

    static readonly string[] s_swatches = { "#FF0000", "#FFA500", "#FFFF00", "#00FF00", "#0000FF", "#800080", "#FFFFFF" };

    InputField[] m_fields;
    InputField m_target;
    int m_selectionStart, m_selectionEnd;
    // A button takes the focus away from the box being formatted. It's given back with the same letters selected, so
    // you can see what the next button will change.
    InputField m_refocus;
    InputField m_sizeField;
    InputField m_colorField;
    InputField m_gradientField;
    InputField m_wheelTarget;       // Which color box the sliders set: Color's, or the gradient's end
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
        public bool plainFits;          // The field itself isn't scrolling its text
        public bool shownFocused;
        // The highlight and caret drawn where the formatted letters are, and the underline and strikethrough lines
        public FormattedTextMarks behind, front;
        public Color selectionColor, caretColor;
        public bool customCaret;
    }
    readonly List<bool> m_underline = new List<bool>(), m_strike = new List<bool>();
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
        y -= RowHeight + RowGap;

        float x = Left;
        x = bar.TagButton(buttonTemplate, content, "<b>B</b>", x, y, 34, "b");
        x = bar.TagButton(buttonTemplate, content, "<i>I</i>", x, y, 34, "i");
        x = bar.TagButton(buttonTemplate, content, "U", x, y, 34, "u");
        x = bar.TagButton(buttonTemplate, content, "S", x, y, 34, "s");
        x = MakeButton(buttonTemplate, content, "Clear", x, y, 56, bar.ClearFormatting);

        bar.m_sizeField = MakeField(fieldTemplate, content, "150", x, y, 44);
        bar.m_sizeField.contentType = InputField.ContentType.IntegerNumber;
        bar.m_sizeField.characterLimit = 3;
        x += 48;
        MakeButton(buttonTemplate, content, "Size %", x, y, Right - x, bar.ApplySize);
        y -= RowHeight + RowGap;

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
        y -= RowHeight + RowGap;

        // A gradient: each letter fades from the Color box's color to this one
        x = Left;
        Label(labelTemplate, content, "Fade to", x, y, 76);
        x += 80;
        bar.m_gradientField = MakeField(fieldTemplate, content, "#0000FF", x, y, 76);
        bar.m_gradientField.characterLimit = 9;
        x += 80;
        MakeButton(buttonTemplate, content, "Gradient", x, y, 90, bar.ApplyGradient);
        y -= RowHeight + RowGap;

        // Any color: pick it with the sliders (as in Collab Mode), then press Color. The sliders set whichever of the
        // two color boxes was clicked last.
        bar.m_wheelTarget = bar.m_colorField;
        bar.m_wheel = RgbSliderPicker.Build(content, Left, y, Right - Left, labelTemplate.font, labelTemplate.color, null, false);
        bar.m_wheel.onPicked = picked => bar.m_wheelTarget.text = "#" + ColorUtility.ToHtmlStringRGB(picked);
        foreach (InputField colorField in new[] { bar.m_colorField, bar.m_gradientField })
        {
            InputField field = colorField;
            field.onValueChanged.AddListener(typed =>
            {
                Color parsed;
                string hex;
                if (field == bar.m_wheelTarget && RichTextIni.TryParseColor(typed, out hex) && ColorUtility.TryParseHtmlString(hex, out parsed) && !ColorsMatch(parsed, bar.m_wheel.color))
                    bar.m_wheel.SetColor(parsed);
            });
        }
        Color start;
        if (ColorUtility.TryParseHtmlString(bar.m_colorField.text, out start))
            bar.m_wheel.SetColor(start);
        y -= bar.m_wheel.height + SectionGap;

        bar.m_preview = Label(labelTemplate, content, string.Empty, Left, y, Right - Left);
        bar.m_preview.supportRichText = true;
        bar.m_preview.alignment = TextAnchor.UpperLeft;
        bar.m_preview.horizontalOverflow = HorizontalWrapMode.Wrap;
        // Shrinks to fit its box, so long help or look up messages never run over the Icon row below
        bar.m_preview.verticalOverflow = VerticalWrapMode.Truncate;
        bar.m_preview.resizeTextForBestFit = true;
        bar.m_preview.resizeTextMinSize = 10;
        bar.m_preview.resizeTextMaxSize = 16;
        Place(bar.m_preview.GetComponent<RectTransform>(), Left, y, Right - Left, PreviewHeight);

        bottom = y - PreviewHeight - SectionGap;
        return bar;
    }

    void Update()
    {
        foreach (InputField colorField in new[] { m_colorField, m_gradientField })
        {
            Color parsed;
            string hex;
            if (colorField && colorField.isFocused && colorField != m_wheelTarget)
            {
                m_wheelTarget = colorField;
                if (RichTextIni.TryParseColor(colorField.text, out hex) && ColorUtility.TryParseHtmlString(hex, out parsed))
                    m_wheel.SetColor(parsed);
            }
        }

        if (m_refocus && m_refocus.isFocused)
        {
            // Unity selects all of it on the way back in, so put back the letters that were selected
            m_refocus.selectionAnchorPosition = m_selectionStart;
            m_refocus.selectionFocusPosition = m_selectionEnd;
            m_refocus.ForceLabelUpdate();
            m_refocus = null;
        }

        bool typingInOtherBox = false;
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

        // Typing in a box that can't be formatted means the buttons no longer belong to the last one.
        // The bar's own size and color boxes keep it, as they're how a format is picked.
        GameObject selected = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
        InputField selectedField = selected ? selected.GetComponent<InputField>() : null;
        if (selectedField && selectedField.isFocused && Array.IndexOf(m_fields, selectedField) < 0
            && selectedField != m_sizeField && selectedField != m_colorField && selectedField != m_gradientField
            && !(m_wheel && selectedField.transform.IsChildOf(m_wheel.transform)))
        {
            typingInOtherBox = true;
            m_target = null;
        }

        if (!typingInOtherBox)
            UpdateHotkeys();

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
            m_preview.text = "Select text in a field above, then pick a format (Ctrl+B bold, Ctrl+I italic, Ctrl+U underline, Ctrl+S strikethrough, Ctrl+G gradient, Ctrl+R rainbow, Ctrl+C clear). Color: set it with the sliders or type it, then press Color. Gradient: set both colors, then press Gradient.";
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
        formatted.overlay.horizontalOverflow = field.multiLine ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
        formatted.overlay.verticalOverflow = VerticalWrapMode.Overflow;
        formatted.overlay.enabled = false;

        // Shrunk from its left edge when the text is wider than the box, so a long name still shows in full
        RectTransform rect = formatted.overlay.rectTransform;
        Vector2 offsetMin = rect.offsetMin, offsetMax = rect.offsetMax;
        rect.pivot = new Vector2(0, 0.5f);
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        formatted.selectionColor = field.selectionColor;
        formatted.caretColor = field.caretColor;
        formatted.customCaret = field.customCaretColor;
        formatted.behind = FormattedTextMarks.Make(formatted.overlay, "Formatted Selection", true);
        formatted.front = FormattedTextMarks.Make(formatted.overlay, "Formatted Caret and Lines", false);

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
        bool focused = field.isFocused;
        if (formatted.shown != formatted.tagged || formatted.shownWidth != width || formatted.shownFocused != focused)
        {
            formatted.shown = formatted.tagged;
            formatted.shownWidth = width;
            formatted.shownFocused = focused;
            formatted.overlay.horizontalOverflow = field.multiLine ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;    // An extended box wraps
            // While typing, bigger sizes are drawn at the field's size so the text stays close to where the caret
            // and the selection are
            int size = formatted.overlay.fontSize;
            formatted.overlay.text = RichTextIni.ToPreview(formatted.tagged, size, focused ? size : size * 3 / 2);
            // A multi-line box (the Loading phrase) wraps, and only shrinks if even that runs out of lines
            float room = field.multiLine ? rect.rect.height : width;
            float needed = field.multiLine ? formatted.overlay.preferredHeight : formatted.overlay.preferredWidth;
            formatted.overflows = room > 0 && needed > room;
            float plainNeeded = PlainSize(formatted.overlay, RichTextIni.Strip(formatted.tagged), field.multiLine, width);
            formatted.plainFits = room <= 0 || plainNeeded <= room;
            float scale = formatted.overflows ? room / needed : 1;
            rect.localScale = new Vector3(scale, scale, 1);
        }

        // Plain text that fits is left to the field itself. While typing the formatting shows live too, shrunk to fit
        // if bold or bigger letters make it too wide, unless the plain text itself runs past the box (the field
        // scrolls it then, and the formatted copy can't follow)
        bool show = focused ? formatted.plainFits && RichTextIni.HasTags(formatted.tagged)
            : formatted.overflows || RichTextIni.HasTags(formatted.tagged);
        if (formatted.overlay.enabled != show)
        {
            formatted.overlay.enabled = show;
            Color color = formatted.textColor;
            if (show)
                color.a = 0;    // The field's own plain text hides behind the formatted one
            field.textComponent.color = color;
        }

        UpdateMarks(field, formatted, show);
    }

    // How wide (or, wrapped, how tall) the field's own plain text is
    static float PlainSize(Text text, string plain, bool multiLine, float width)
    {
        TextGenerator generator = text.cachedTextGeneratorForLayout;
        float perUnit = text.pixelsPerUnit > 0 ? text.pixelsPerUnit : 1;
        if (multiLine)
            return generator.GetPreferredHeight(plain, text.GetGenerationSettings(new Vector2(width, 0))) / perUnit;
        return generator.GetPreferredWidth(plain, text.GetGenerationSettings(Vector2.zero)) / perUnit;
    }

    // While the formatted copy is shown, the highlight, caret, underline and strikethrough are drawn on it, at its own
    // letters: the field measures its plain letters, so its own highlight fell short of bold or bigger text
    void UpdateMarks(InputField field, Formatted formatted, bool show)
    {
        if (!formatted.behind || !formatted.front)
            return;

        bool ownMarks = show && field.isFocused;
        Color selection = formatted.selectionColor;
        if (ownMarks)
            selection.a = 0;
        if (field.selectionColor != selection)
            field.selectionColor = selection;
        field.customCaretColor = ownMarks || formatted.customCaret;
        field.caretColor = ownMarks ? Color.clear : formatted.caretColor;

        formatted.behind.Clear();
        formatted.front.Clear();
        formatted.behind.Follow(formatted.overlay);
        formatted.front.Follow(formatted.overlay);

        Text text = formatted.overlay;
        TextGenerator generator = text.cachedTextGenerator;
        if (!show || generator.characterCount == 0 || generator.lineCount == 0)
            return;

        string preview = text.text;
        List<int> visible = RichTextIni.VisibleIndices(preview);
        IList<UICharInfo> characters = generator.characters;
        IList<UILineInfo> lines = generator.lines;
        bool tagsCounted = characters.Count >= preview.Length;     // Unity 2018 keeps the tags' own characters, at no width
        float perUnit = text.pixelsPerUnit > 0 ? text.pixelsPerUnit : 1;

        // Where visible character v is: its left edge, width and line
        Func<int, int> indexOf = v => tagsCounted ? visible[v] : v;
        Func<int, Rect> box = v =>
        {
            int index = indexOf(v);
            if (index >= characters.Count)
                return new Rect();
            int line = 0;
            while (line + 1 < lines.Count && lines[line + 1].startCharIdx <= index)
                ++line;
            float top = lines[line].topY / perUnit, height = lines[line].height / perUnit;
            return new Rect(characters[index].cursorPos.x / perUnit, top - height, characters[index].charWidth / perUnit, height);
        };

        RichTextIni.Decorations(formatted.tagged, m_underline, m_strike);
        for (int v = 0; v < visible.Count && v < m_underline.Count; ++v)
        {
            if (!m_underline[v] && !m_strike[v])
                continue;
            Rect letter = box(v);
            float thickness = Mathf.Max(1, letter.height * 0.07f);
            if (m_underline[v])
                formatted.front.Add(new Rect(letter.x, letter.y + letter.height * 0.12f, letter.width, thickness), text.color);
            if (m_strike[v])
                formatted.front.Add(new Rect(letter.x, letter.y + letter.height * 0.45f, letter.width, thickness), text.color);
        }

        if (ownMarks && visible.Count > 0)
        {
            int start = Mathf.Clamp(Math.Min(field.selectionAnchorPosition, field.selectionFocusPosition), 0, visible.Count);
            int end = Mathf.Clamp(Math.Max(field.selectionAnchorPosition, field.selectionFocusPosition), 0, visible.Count);
            for (int v = start; v < end; ++v)
                formatted.behind.Add(box(v), formatted.selectionColor);

            float blinkRate = field.caretBlinkRate > 0 ? field.caretBlinkRate : 1;
            if (start == end && Mathf.Repeat(Time.unscaledTime * blinkRate, 1) < 0.5f)
            {
                int caret = Mathf.Clamp(field.caretPosition, 0, visible.Count);
                Rect at = caret < visible.Count ? box(caret) : box(visible.Count - 1);
                float x = caret < visible.Count ? at.x : at.xMax;
                Color caretColor = formatted.customCaret ? formatted.caretColor : formatted.textColor;
                formatted.front.Add(new Rect(x, at.y, Mathf.Max(1, field.caretWidth), at.height), caretColor);
            }
        }

        formatted.behind.Done();
        formatted.front.Done();
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

    // The box the buttons change: the one being typed in, or the last one that was. None until a box is clicked.
    bool TryTarget(out InputField field)
    {
        field = m_target;
        if (field)
            return true;
        SetStatus("Click in one of the boxes above (or select some of its text) first, then pick a format.");
        return false;
    }

    // Ctrl+B, Ctrl+I, Ctrl+U and Ctrl+S switch bold, italic, underline and strikethrough on or off for the selected
    // text (all of it with nothing selected), Ctrl+G fades it with the two colors like Gradient, Ctrl+R makes it rainbow
    // and Ctrl+C clears its formatting. Fixed keys, only while Song Properties is open.
    void UpdateHotkeys()
    {
        bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        bool other = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!ctrl || other)
            return;

        if (Input.GetKeyDown(KeyCode.B))
            Toggle("b");
        else if (Input.GetKeyDown(KeyCode.I))
            Toggle("i");
        else if (Input.GetKeyDown(KeyCode.U))
            Toggle("u");
        else if (Input.GetKeyDown(KeyCode.S))
            Toggle("s");
        else if (Input.GetKeyDown(KeyCode.G))
            ApplyGradient();
        else if (Input.GetKeyDown(KeyCode.R))
            ApplyRainbow();
        else if (Input.GetKeyDown(KeyCode.C))
            ClearFormatting();
    }

    void ApplyRainbow()
    {
        InputField field;
        if (!TryTarget(out field))
            return;
        SetFormatted(field, RichTextIni.RainbowVisible(GetTagged(field), m_selectionStart, m_selectionEnd));
    }

    void Wrap(string open, string close)
    {
        InputField field;
        if (!TryTarget(out field))
            return;
        SetFormatted(field, RichTextIni.WrapVisible(GetTagged(field), m_selectionStart, m_selectionEnd, open, close));
    }

    // On for the selection, or off if it all has it already
    void Toggle(string name)
    {
        InputField field;
        if (!TryTarget(out field))
            return;
        SetFormatted(field, RichTextIni.ToggleVisible(GetTagged(field), m_selectionStart, m_selectionEnd, name, "<" + name + ">", "</" + name + ">"));
    }

    void ClearFormatting()
    {
        InputField field;
        if (!TryTarget(out field))
            return;
        SetFormatted(field, RichTextIni.ClearVisible(GetTagged(field), m_selectionStart, m_selectionEnd));
    }

    // The words don't change, so the field doesn't tell the song: say so here
    void SetFormatted(InputField field, string tagged)
    {
        SetTagged(field, tagged);
        if (m_onChanged != null)
            m_onChanged();

        if (!field.isFocused)
        {
            field.ActivateInputField();
            m_refocus = field;
        }
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

    void ApplyGradient()
    {
        string from, to;
        if (!RichTextIni.TryParseColor(m_colorField.text, out from))
            m_colorField.text = from = "#FFA500";
        if (!RichTextIni.TryParseColor(m_gradientField.text, out to))
            m_gradientField.text = to = "#0000FF";

        InputField field;
        if (!TryTarget(out field))
            return;
        SetFormatted(field, RichTextIni.GradientVisible(GetTagged(field), m_selectionStart, m_selectionEnd, from, to));
    }

    static bool ColorsMatch(Color a, Color b)
    {
        return ColorUtility.ToHtmlStringRGB(a) == ColorUtility.ToHtmlStringRGB(b);
    }

    float TagButton(Button template, RectTransform parent, string label, float x, float y, float width, string name)
    {
        return MakeButton(template, parent, label, x, y, width, () => Toggle(name));
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
