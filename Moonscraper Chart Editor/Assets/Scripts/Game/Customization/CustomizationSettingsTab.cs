// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Customize tab in Moonscraper's Settings: Clone Hero color profiles, highways and backgrounds.
/// The tab is built from copies of the existing Settings controls when the editor opens, so it matches the rest of
/// the window without changes to the Settings prefab.
/// </summary>
public class CustomizationSettingsTab : MonoBehaviour
{
    const float RowHeight = 32;
    const float ControlLeft = 300, ControlWidth = 250, ButtonWidth = 70, Gap = 8;

    static readonly ExtensionFilter s_imageFilter = new ExtensionFilter("Images", "png", "jpg", "jpeg", "gif");
    static readonly ExtensionFilter s_profileFilter = new ExtensionFilter("Clone Hero color profiles", "ini");

    class Choice
    {
        public CustomizationPrefs.Kind kind;
        public Dropdown dropdown;
        public List<string> paths = new List<string>();     // Matches the dropdown's options. Null is Moonscraper's default.
    }

    SettingsController m_settings;
    Text m_labelTemplate;
    Dropdown m_dropdownTemplate;
    Button m_buttonTemplate;
    RectTransform m_group;

    Text m_folderText;
    Text m_statusText;
    readonly Dictionary<CustomizationPrefs.Kind, Choice> m_choices = new Dictionary<CustomizationPrefs.Kind, Choice>();

    bool m_built = false;
    bool m_populating = false;
    int m_rows = 0;

    public static void AttachToSettingsMenus()
    {
        foreach (SettingsController settings in Resources.FindObjectsOfTypeAll<SettingsController>())
        {
            if (!settings.gameObject.scene.IsValid() || settings.GetComponent<CustomizationSettingsTab>())
                continue;   // The prefab asset itself, or already done

            CustomizationSettingsTab tab = settings.gameObject.AddComponent<CustomizationSettingsTab>();
            try
            {
                tab.Build(settings);
            }
            catch (Exception e)
            {
                Debug.LogError("Couldn't add the Customize tab to Settings. " + e);
            }
        }
    }

    void OnEnable()
    {
        if (m_built)
            Refresh();
    }

    void Build(SettingsController settings)
    {
        m_settings = settings;

        Transform panel = settings.transform.Find("BG Panel");
        Button graphicsButton = FindComponent<Button>(panel, "Graphics Settings Button");
        Button lastButton = FindComponent<Button>(panel, "Lyric Editor Settings Button");
        Transform content = panel ? panel.Find("Scroll View/Viewport/Content") : null;
        RectTransform graphicsGroup = content ? content.Find("Graphics Settings") as RectTransform : null;
        m_labelTemplate = FindComponent<Text>(graphicsGroup, "AA Level");
        m_dropdownTemplate = FindComponent<Dropdown>(graphicsGroup, "AA Level Dropdown");
        m_buttonTemplate = graphicsButton;

        AddAutosaveRow(content ? content.Find("Editor Settings") as RectTransform : null);

        if (!graphicsButton || !lastButton || !graphicsGroup || !m_labelTemplate || !m_dropdownTemplate)
        {
            Debug.LogWarning("Settings menu layout has changed, so the Customize tab wasn't added");
            return;
        }

        // Tab button, after the last tab with the same spacing as the others
        Button tabButton = Instantiate(graphicsButton, graphicsButton.transform.parent);
        tabButton.name = "Customize Settings Button";
        tabButton.onClick = new Button.ButtonClickedEvent();
        tabButton.interactable = true;
        SetButtonText(tabButton, "Customize");
        {
            RectTransform first = (RectTransform)graphicsButton.transform;
            RectTransform last = (RectTransform)lastButton.transform;
            RectTransform rect = (RectTransform)tabButton.transform;
            float spacing = (last.anchoredPosition.x - last.sizeDelta.x / 2) - (first.anchoredPosition.x + first.sizeDelta.x / 2);
            rect.anchoredPosition = new Vector2(last.anchoredPosition.x + last.sizeDelta.x / 2 + spacing + rect.sizeDelta.x / 2, last.anchoredPosition.y);
        }

        // Tab content, laid out like the Graphics tab
        GameObject groupObject = new GameObject("Customize Settings", typeof(RectTransform));
        groupObject.layer = graphicsGroup.gameObject.layer;
        m_group = (RectTransform)groupObject.transform;
        m_group.SetParent(content, false);
        m_group.anchorMin = graphicsGroup.anchorMin;
        m_group.anchorMax = graphicsGroup.anchorMax;
        m_group.pivot = graphicsGroup.pivot;
        m_group.anchoredPosition = graphicsGroup.anchoredPosition;
        m_group.localScale = graphicsGroup.localScale;

        // Clone Hero folder
        AddLabel("Clone Hero folder");
        m_folderText = AddText(ControlLeft, ControlWidth, RowHeight, TextAnchor.MiddleLeft, 14);
        AddButton("Browse", ControlLeft + ControlWidth + Gap, BrowseCloneHeroFolder);
        ++m_rows;

        AddChoiceRow("Color profile", CustomizationPrefs.Kind.ColorProfile, BrowseColorProfile);
        AddChoiceRow("Highway", CustomizationPrefs.Kind.Highway, () => BrowseImage(CustomizationPrefs.Kind.Highway));
        AddChoiceRow("Background", CustomizationPrefs.Kind.Background, () => BrowseImage(CustomizationPrefs.Kind.Background));
        AddThemeRow();

        // Reads every picked file again, for after editing a color profile or image while Moonscraper is open
        AddButton("Reload", ControlLeft, ReloadAll);
        ++m_rows;

        float statusLeft = m_labelTemplate.rectTransform.anchoredPosition.x - m_labelTemplate.rectTransform.sizeDelta.x / 2;
        // Ends where the dropdowns do, so a long line wraps instead of running off the window
        m_statusText = AddText(statusLeft, ControlLeft + ControlWidth - statusLeft, 72, TextAnchor.UpperLeft, 14);
        m_rows += 3;

        m_group.sizeDelta = new Vector2(graphicsGroup.sizeDelta.x, m_rows * RowHeight + 10);
        groupObject.SetActive(false);

        tabButton.onClick.AddListener(() =>
        {
            m_settings.SetSettingsGroup(m_group);
            m_settings.SetCurrentButton(tabButton);
        });

        m_built = true;
        Refresh();
    }

    // "Autosave chart" in the Editor tab, under New Song Resolution, copied from the BG swap time row above it
    static void AddAutosaveRow(RectTransform editorGroup)
    {
        Text labelTemplate = FindComponent<Text>(editorGroup, "BG Swap Time Text");
        Dropdown dropdownTemplate = FindComponent<Dropdown>(editorGroup, "BG Swap Dropdown");
        RectTransform lastRow = editorGroup ? editorGroup.Find("New Song Resolution") as RectTransform : null;
        if (!labelTemplate || !dropdownTemplate || !lastRow)
        {
            Debug.LogWarning("Settings menu layout has changed, so the autosave setting wasn't added");
            return;
        }

        const float RowStep = 32;
        float labelY = lastRow.anchoredPosition.y - RowStep;

        Text label = Instantiate(labelTemplate, editorGroup);
        label.name = "Autosave Chart Text";
        label.text = "Autosave chart";
        label.rectTransform.anchoredPosition = new Vector2(labelTemplate.rectTransform.anchoredPosition.x, labelY);

        RectTransform templateRect = (RectTransform)dropdownTemplate.transform;
        Dropdown dropdown = Instantiate(dropdownTemplate, editorGroup);
        dropdown.name = "Autosave Chart Dropdown";
        dropdown.onValueChanged = new Dropdown.DropdownEvent();
        ((RectTransform)dropdown.transform).anchoredPosition = new Vector2(templateRect.anchoredPosition.x,
            labelY + templateRect.anchoredPosition.y - labelTemplate.rectTransform.anchoredPosition.y);

        List<string> options = new List<string>();
        int selected = 0;
        for (int i = 0; i < AutoSaveSystem.ChartMinuteChoices.Length; ++i)
        {
            int minutes = AutoSaveSystem.ChartMinuteChoices[i];
            options.Add(minutes <= 0 ? "Off" : minutes == 1 ? "1 minute" : minutes + " minutes");
            if (minutes == AutoSaveSystem.chartAutosaveMinutes)
                selected = i;
        }
        dropdown.ClearOptions();
        dropdown.AddOptions(options);
        dropdown.value = selected;
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener((index) => AutoSaveSystem.chartAutosaveMinutes = AutoSaveSystem.ChartMinuteChoices[index]);

        editorGroup.sizeDelta = new Vector2(editorGroup.sizeDelta.x, editorGroup.sizeDelta.y + RowStep);
    }

    void AddChoiceRow(string label, CustomizationPrefs.Kind kind, Action browse)
    {
        AddLabel(label);

        Choice choice = new Choice();
        choice.kind = kind;
        choice.dropdown = Instantiate(m_dropdownTemplate, m_group);
        choice.dropdown.name = label + " Dropdown";
        choice.dropdown.onValueChanged = new Dropdown.DropdownEvent();
        choice.dropdown.onValueChanged.AddListener((index) => OnChoiceChanged(choice, index));
        Place((RectTransform)choice.dropdown.transform, ControlLeft, ControlWidth, ((RectTransform)m_dropdownTemplate.transform).anchoredPosition.y);
        m_choices[kind] = choice;

        AddButton("Browse", ControlLeft + ControlWidth + Gap, browse);
        ++m_rows;
    }

    // Default or Dark look for the menus and panels
    void AddThemeRow()
    {
        AddLabel("Theme");

        Dropdown dropdown = Instantiate(m_dropdownTemplate, m_group);
        dropdown.name = "Theme Dropdown";
        dropdown.onValueChanged = new Dropdown.DropdownEvent();
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string> { "Default", "Dark" });
        dropdown.value = (int)UITheme.current;
        dropdown.RefreshShownValue();
        dropdown.onValueChanged.AddListener((index) => UITheme.current = (UITheme.Theme)index);
        Place((RectTransform)dropdown.transform, ControlLeft, ControlWidth, ((RectTransform)m_dropdownTemplate.transform).anchoredPosition.y);
        ++m_rows;
    }

    void AddLabel(string label)
    {
        Text text = Instantiate(m_labelTemplate, m_group);
        text.name = label;
        text.text = label;
        RectTransform rect = text.rectTransform;
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y - m_rows * RowHeight);
    }

    Text AddText(float left, float width, float height, TextAnchor alignment, int fontSize)
    {
        Text text = Instantiate(m_labelTemplate, m_group);
        text.name = "Info";
        text.text = string.Empty;
        text.alignment = alignment;
        text.fontSize = fontSize;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;

        RectTransform rect = text.rectTransform;
        rect.sizeDelta = new Vector2(width, height);
        Place(rect, left, width, m_labelTemplate.rectTransform.anchoredPosition.y - (height - RowHeight) / 2);
        return text;
    }

    void AddButton(string label, float left, Action onClick)
    {
        Button button = Instantiate(m_buttonTemplate, m_group);
        button.name = label + " Button";
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => onClick());
        button.interactable = true;
        SetButtonText(button, label);

        RectTransform rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(ButtonWidth, ((RectTransform)m_dropdownTemplate.transform).sizeDelta.y);
        Place(rect, left, ButtonWidth, ((RectTransform)m_dropdownTemplate.transform).anchoredPosition.y);
    }

    // Puts a control's left edge at `left` on the current row. yOnFirstRow is where it would sit on the first row.
    void Place(RectTransform rect, float left, float width, float yOnFirstRow)
    {
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        rect.anchoredPosition = new Vector2(left + width * rect.pivot.x, yOnFirstRow - m_rows * RowHeight);
    }

    static void SetButtonText(Button button, string label)
    {
        Text text = button.GetComponentInChildren<Text>(true);
        if (text)
            text.text = label;
    }

    static T FindComponent<T>(Transform parent, string path) where T : Component
    {
        if (!parent)
            return null;
        Transform child = parent.Find(path);
        return child ? child.GetComponent<T>() : null;
    }

    string cloneHeroFolder
    {
        get
        {
            string saved = CloneHeroFolders.NormaliseCustomFolder(CustomizationPrefs.cloneHeroFolder);
            return saved ?? CloneHeroFolders.FindCustomFolder();
        }
    }

    void Refresh()
    {
        if (!m_built)
            return;

        string folder = cloneHeroFolder;
        m_folderText.text = folder ?? "Not found. Use Browse to pick it.";

        Populate(m_choices[CustomizationPrefs.Kind.ColorProfile], CloneHeroFolders.ListColorProfiles(folder), "Default colors");
        Populate(m_choices[CustomizationPrefs.Kind.Highway], CloneHeroFolders.ListHighways(folder), "Default highway");
        Populate(m_choices[CustomizationPrefs.Kind.Background], CloneHeroFolders.ListBackgrounds(folder), "Default background");
    }

    void Populate(Choice choice, List<CloneHeroFolders.Item> items, string defaultName)
    {
        List<string> names = new List<string>();
        choice.paths.Clear();

        names.Add(defaultName);
        choice.paths.Add(null);

        foreach (CloneHeroFolders.Item item in items)
        {
            names.Add(item.name);
            choice.paths.Add(item.path);
        }

        // Something picked with Browse, or from a Clone Hero folder that's no longer there
        int selected = 0;
        string source = CustomizationPrefs.GetSource(choice.kind);
        if (!string.IsNullOrEmpty(source) && CustomizationPrefs.GetStored(choice.kind) != null)
        {
            selected = choice.paths.FindIndex(path => path != null && string.Equals(SafeFullPath(path), SafeFullPath(source), StringComparison.OrdinalIgnoreCase));
            if (selected < 0)
            {
                names.Add(DisplayName(choice.kind, source));
                choice.paths.Add(source);
                selected = choice.paths.Count - 1;
            }
        }

        m_populating = true;
        choice.dropdown.ClearOptions();
        choice.dropdown.AddOptions(names);
        choice.dropdown.value = selected;
        choice.dropdown.RefreshShownValue();
        m_populating = false;
    }

    static string DisplayName(CustomizationPrefs.Kind kind, string source)
    {
        return Path.GetFileNameWithoutExtension(source) + " (picked)";
    }

    static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    void OnChoiceChanged(Choice choice, int index)
    {
        if (m_populating || index < 0 || index >= choice.paths.Count)
            return;

        Choose(choice.kind, choice.paths[index]);
    }

    // Saves the choice (null for Moonscraper's default) and shows it straight away
    void Choose(CustomizationPrefs.Kind kind, string source)
    {
        string error = null;

        if (source == null)
        {
            CustomizationPrefs.Clear(kind);
        }
        else
        {
            try
            {
                CustomizationPrefs.Store(kind, source);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Couldn't save a copy of " + source + ". " + e.Message);
                error = "Couldn't copy that file. " + e.Message;
            }
        }

        CustomizationManager manager = CustomizationManager.instance;
        if (error == null && manager)
        {
            switch (kind)
            {
                case CustomizationPrefs.Kind.ColorProfile: error = manager.ApplyColorProfile(); break;
                case CustomizationPrefs.Kind.Highway: error = manager.ApplyHighway(); break;
                case CustomizationPrefs.Kind.Background: error = manager.ApplyBackground(); break;
            }
        }

        if (error != null)
        {
            // Don't keep something that doesn't load
            CustomizationPrefs.Clear(kind);
            if (manager)
            {
                switch (kind)
                {
                    case CustomizationPrefs.Kind.ColorProfile: manager.ApplyColorProfile(); break;
                    case CustomizationPrefs.Kind.Highway: manager.ApplyHighway(); break;
                    case CustomizationPrefs.Kind.Background: manager.ApplyBackground(); break;
                }
            }
        }

        m_statusText.text = error ?? string.Empty;

        Refresh();
    }

    void BrowseCloneHeroFolder()
    {
        string picked;
        if (!FileExplorer.OpenFolderPanel(out picked))
            return;

        string folder = CloneHeroFolders.NormaliseCustomFolder(picked);
        if (folder == null)
        {
            m_statusText.text = "That folder doesn't exist.";
            return;
        }

        CustomizationPrefs.cloneHeroFolder = folder;
        m_statusText.text = string.Empty;
        Refresh();
    }

    void BrowseColorProfile()
    {
        string path;
        if (FileExplorer.OpenFilePanel(s_profileFilter, "ini", out path))
            Choose(CustomizationPrefs.Kind.ColorProfile, path);
    }

    void BrowseImage(CustomizationPrefs.Kind kind)
    {
        string path;
        if (FileExplorer.OpenFilePanel(s_imageFilter, "png", out path))
            Choose(kind, path);
    }

    void ReloadAll()
    {
        List<string> problems = new List<string>();
        foreach (CustomizationPrefs.Kind kind in new[] { CustomizationPrefs.Kind.ColorProfile, CustomizationPrefs.Kind.Highway, CustomizationPrefs.Kind.Background })
        {
            // Copy the original again when it's still there, so changes made to it since show up
            string source = CustomizationPrefs.GetSource(kind);
            if (!string.IsNullOrEmpty(source) && File.Exists(source))
            {
                try
                {
                    CustomizationPrefs.Store(kind, source);
                }
                catch (Exception e)
                {
                    problems.Add("Couldn't read " + Path.GetFileName(source) + " again (" + e.Message + "), so the copy from before is kept.");
                }
            }
        }

        CustomizationManager manager = CustomizationManager.instance;
        if (manager)
        {
            foreach (string error in new[] { manager.ApplyColorProfile(), manager.ApplyHighway(), manager.ApplyBackground() })
            {
                if (error != null)
                    problems.Add(error);
            }
        }

        Refresh();
        m_statusText.text = problems.Count > 0 ? string.Join(" ", problems.ToArray()) : "Reloaded.";
    }
}
