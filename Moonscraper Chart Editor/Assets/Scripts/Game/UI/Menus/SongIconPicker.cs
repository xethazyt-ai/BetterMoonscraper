// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "Icon" row under Song Properties > General: search Clone Hero's source and charter icons by name and click one
/// to put it in song.ini's "icon". Shows the icon picked now. Built in code from the panel's own controls.
/// </summary>
public class SongIconPicker : MonoBehaviour
{
    const float Left = 52;
    const float RowHeight = 30;
    const float CellSize = 64;
    const float CellGap = 8;
    const float ImageSize = 40;
    const int ResultRows = 2;

    Func<string> m_getIcon;
    Action<string> m_setIcon;
    RawImage m_currentImage;
    Text m_currentText;
    InputField m_search;
    Text m_status;
    readonly List<Button> m_cells = new List<Button>();
    readonly List<RawImage> m_cellImages = new List<RawImage>();
    readonly List<Text> m_cellTexts = new List<Text>();
    readonly List<SongIcons.Icon> m_shown = new List<SongIcons.Icon>();
    int m_generation = 0;   // Pictures that arrive after the results changed are dropped

    public static SongIconPicker Build(RectTransform content, float top, float right, InputField fieldTemplate, Text labelTemplate, Button buttonTemplate,
        Func<string> getIcon, Action<string> setIcon, out float bottom)
    {
        SongIconPicker picker = content.gameObject.AddComponent<SongIconPicker>();
        picker.m_getIcon = getIcon;
        picker.m_setIcon = setIcon;

        float y = top;
        Text label = Instantiate(labelTemplate, content);
        label.text = "Icon:";
        SongPropertiesFormatBar.Place(label.rectTransform, Left, y, 70, RowHeight);

        picker.m_currentImage = MakeImage(content, "Current Icon");
        SongPropertiesFormatBar.Place(picker.m_currentImage.rectTransform, Left + 74, y, RowHeight, RowHeight);

        picker.m_currentText = Instantiate(labelTemplate, content);
        picker.m_currentText.fontSize = Mathf.Min(picker.m_currentText.fontSize, 16);
        picker.m_currentText.horizontalOverflow = HorizontalWrapMode.Wrap;
        picker.m_currentText.resizeTextForBestFit = false;
        SongPropertiesFormatBar.Place(picker.m_currentText.rectTransform, Left + 110, y, right - 70 - (Left + 110), RowHeight);

        Button none = Instantiate(buttonTemplate, content);
        none.name = "No Icon Button";
        none.onClick = new Button.ButtonClickedEvent();
        none.onClick.AddListener(() => picker.Pick(null));
        foreach (Text text in none.GetComponentsInChildren<Text>(true))
            text.text = "None";
        SongPropertiesFormatBar.Place(none.GetComponent<RectTransform>(), right - 66, y, 66, RowHeight);
        y -= RowHeight + 10;

        picker.m_search = Instantiate(fieldTemplate, content);
        picker.m_search.name = "Icon Search InputField";
        picker.m_search.onValueChanged = new InputField.OnChangeEvent();
        picker.m_search.onEndEdit = new InputField.SubmitEvent();
        picker.m_search.onValidateInput = null;
        picker.m_search.characterLimit = 64;
        picker.m_search.text = string.Empty;
        Text placeholder = picker.m_search.placeholder as Text;
        if (placeholder)
            placeholder.text = "Search icons (charter, setlist or source)";
        picker.m_search.onValueChanged.AddListener(query => picker.ShowResults());
        SongPropertiesFormatBar.Place(picker.m_search.GetComponent<RectTransform>(), Left, y, right - Left, RowHeight);
        y -= RowHeight + 10;

        int columns = Mathf.Max(1, Mathf.FloorToInt((right - Left + CellGap) / (CellSize + CellGap)));
        for (int row = 0; row < ResultRows; ++row)
        {
            for (int column = 0; column < columns; ++column)
                picker.AddCell(content, buttonTemplate, Left + column * (CellSize + CellGap), y - row * (CellSize + CellGap));
        }
        y -= ResultRows * (CellSize + CellGap);

        picker.m_status = Instantiate(labelTemplate, content);
        picker.m_status.fontSize = Mathf.Min(picker.m_status.fontSize, 14);
        picker.m_status.resizeTextForBestFit = false;
        picker.m_status.horizontalOverflow = HorizontalWrapMode.Wrap;
        picker.m_status.alignment = TextAnchor.UpperLeft;
        SongPropertiesFormatBar.Place(picker.m_status.rectTransform, Left, y, right - Left, 40);

        bottom = y - 60;
        return picker;
    }

    static RawImage MakeImage(Transform parent, string name)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RawImage image = go.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    void AddCell(RectTransform content, Button template, float x, float y)
    {
        Button cell = Instantiate(template, content);
        cell.name = "Icon Result";
        cell.onClick = new Button.ButtonClickedEvent();
        int index = m_cells.Count;
        cell.onClick.AddListener(() =>
        {
            if (index < m_shown.Count)
                Pick(m_shown[index].id);
        });
        SongPropertiesFormatBar.Place(cell.GetComponent<RectTransform>(), x, y, CellSize, CellSize);

        Text text = null;
        foreach (Text child in cell.GetComponentsInChildren<Text>(true))
        {
            if (text == null)
                text = child;
            else
                child.text = string.Empty;
        }
        if (text)
        {
            text.resizeTextForBestFit = false;
            text.fontSize = 11;
            text.alignment = TextAnchor.LowerCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(2, 1);
            textRect.offsetMax = new Vector2(-2, -1);
        }

        RawImage image = MakeImage(cell.transform, "Icon");
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(ImageSize, ImageSize);
        rect.anchoredPosition = new Vector2(0, -4);

        cell.gameObject.SetActive(false);
        m_cells.Add(cell);
        m_cellImages.Add(image);
        m_cellTexts.Add(text);
    }

    /// <summary>
    /// Shows the song's icon and gets the list if it isn't here yet. Called when the menu opens.
    /// </summary>
    public void Refresh()
    {
        ShowCurrent();
        ShowResults();
        if (SongIcons.icons == null && !SongIcons.isLoading)
        {
            m_status.text = "Getting Clone Hero's icon list...";
            SongIcons.Run(SongIcons.LoadList(() =>
            {
                if (!this)
                    return;
                ShowCurrent();
                ShowResults();
            }));
        }
    }

    void Pick(string id)
    {
        m_setIcon(id);
        ShowCurrent();
    }

    void ShowCurrent()
    {
        string id = m_getIcon();
        SongIcons.Icon icon = SongIcons.Find(id);
        if (string.IsNullOrEmpty(id))
            m_currentText.text = "None";
        else
            m_currentText.text = id + (icon != null && icon.source != null ? "  (" + icon.source + ")" : icon == null && SongIcons.icons != null ? "  (not in Clone Hero's list)" : string.Empty);

        m_currentImage.enabled = false;
        if (icon != null)
        {
            int generation = ++m_currentGeneration;
            SongIcons.Run(SongIcons.GetTexture(icon, texture =>
            {
                if (!this || generation != m_currentGeneration || !m_currentImage)
                    return;
                m_currentImage.texture = texture;
                m_currentImage.enabled = texture != null;
            }));
        }
    }
    int m_currentGeneration = 0;

    void ShowResults()
    {
        ++m_generation;
        m_shown.Clear();
        string query = m_search.text.Trim();
        if (query.Length > 0)
            m_shown.AddRange(SongIcons.Search(query, m_cells.Count));

        for (int i = 0; i < m_cells.Count; ++i)
        {
            bool used = i < m_shown.Count;
            m_cells[i].gameObject.SetActive(used);
            m_cellImages[i].enabled = false;
            if (!used)
                continue;

            SongIcons.Icon icon = m_shown[i];
            if (m_cellTexts[i])
                m_cellTexts[i].text = icon.id;
            int generation = m_generation;
            RawImage image = m_cellImages[i];
            SongIcons.Run(SongIcons.GetTexture(icon, texture =>
            {
                if (!this || generation != m_generation || !image)
                    return;
                image.texture = texture;
                image.enabled = texture != null;
            }));
        }

        if (SongIcons.isLoading)
            m_status.text = "Getting Clone Hero's icon list...";
        else if (SongIcons.icons == null)
            m_status.text = SongIcons.status ?? string.Empty;
        else if (query.Length == 0)
            m_status.text = SongIcons.icons.Count + " Clone Hero icons. Type a charter, setlist or source to find one, then click it.";
        else if (m_shown.Count == 0)
            m_status.text = "No icon matches \"" + query + "\".";
        else
            m_status.text = "Click an icon to use it. It goes in song.ini as icon = name.";
    }
}
