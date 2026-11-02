// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MoonscraperEngine.Input;
using MoonscraperEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ControlsMenuInterface : MonoBehaviour
{
    [SerializeField]
    ActionBindingsMenu actionBindingsMenu;
    [SerializeField]
    Text actionCategoryText;
    [SerializeField]
    Text deviceText;

    [Header("Default Controls Editor")]
    [SerializeField]
    bool isDefaultControlsEditor = false;
    [SerializeField]
    TextAsset defaultControlsFile;

    int actionCategoryIndex = 0;
    int deviceIndex = 0;

    MSChartEditorInput.MSChartEditorActionContainer actionsToEdit = null;

    enum ActionCategory
    {
        Editor,
        Gameplay_Guitar,
        Gameplay_Drums,
        Gameplay_ProDrums,
    }

    private void Awake()
    {
        SetActionsToEdit();
        actionBindingsMenu.Setup(GetCurrentInputDevice(), actionsToEdit, GetCurrentActionCategoryMask());
    }

    private void Start()
    {
        InputManager.Instance.disconnectEvent.Register(OnDeviceDisconnect);
    }

    void SetActionsToEdit()
    {
        if (actionsToEdit == null)
        {
            if (isDefaultControlsEditor)
            {
                actionsToEdit = JsonUtility.FromJson<MSChartEditorInput.MSChartEditorActionContainer>(defaultControlsFile.text);
                actionsToEdit.LoadFromSaveData(actionsToEdit);
                Globals.LoadExternalControls(actionsToEdit);
            }
            else
            {
                actionsToEdit = Globals.gameSettings.controls;
            }
        }
    }

    void OnDeviceDisconnect(in IInputDevice device)
    {
        deviceIndex = 0;
        RefreshActionBindingsMenu();
    }

    private void OnEnable()
    {
        SetActionsToEdit();
        if (!m_headerBuilt)
            BuildHeader();
        RefreshActionBindingsMenu();
    }

    // The category and device are picked from dropdowns rather than stepped through with the < > arrows.
    // Built from the Settings menu's own dropdown, so they look like the
    // rest of Moonscraper's menus.
    bool m_headerBuilt = false;
    bool m_settingDropdowns = false;
    Dropdown m_categoryDropdown;
    Dropdown m_deviceDropdown;
    readonly List<string> m_deviceNames = new List<string>();

    void BuildHeader()
    {
        m_headerBuilt = true;

        Transform parent = actionCategoryText ? actionCategoryText.transform.parent : null;
        Dropdown dropdownTemplate = null;
        foreach (SettingsController settings in Resources.FindObjectsOfTypeAll<SettingsController>())
        {
            if (settings.gameObject.scene.IsValid())
            {
                dropdownTemplate = settings.antiAliasingLevel;
            }
        }
        if (!parent || !dropdownTemplate || !deviceText)
        {
            Debug.LogWarning("Controls menu layout has changed, so the category and device dropdowns weren't added");
            return;
        }

        float categoryLeft, categoryRight;
        m_categoryDropdown = MakeDropdown(dropdownTemplate, parent, "Category Dropdown", actionCategoryText.rectTransform,
            "Cycle Category -", "Cycle Category +", out categoryLeft, out categoryRight);
        List<string> categories = new List<string>();
        for (int i = 0; i < EnumX<ActionCategory>.Count; ++i)
            categories.Add(GetActionCategoryName((ActionCategory)i));
        m_categoryDropdown.AddOptions(categories);
        m_categoryDropdown.onValueChanged.AddListener(index =>
        {
            if (m_settingDropdowns)
                return;
            actionCategoryIndex = index;
            RefreshActionBindingsMenu();
        });

        float deviceLeft, deviceRight;
        m_deviceDropdown = MakeDropdown(dropdownTemplate, parent, "Device Dropdown", deviceText.rectTransform,
            "Cycle Device -", "Cycle Device +", out deviceLeft, out deviceRight);
        m_deviceDropdown.onValueChanged.AddListener(index =>
        {
            if (m_settingDropdowns)
                return;
            deviceIndex = Mathf.Clamp(index, 0, InputManager.Instance.devices.Count - 1);
            RefreshActionBindingsMenu();
        });

    }

    // A dropdown over the row's label and its two arrows, which are hidden
    Dropdown MakeDropdown(Dropdown template, Transform parent, string name, RectTransform label, string minusName, string plusName, out float left, out float right)
    {
        RectTransform minus = parent.Find(minusName) as RectTransform;
        RectTransform plus = parent.Find(plusName) as RectTransform;
        left = label.anchoredPosition.x - label.sizeDelta.x / 2;
        right = label.anchoredPosition.x + label.sizeDelta.x / 2;
        if (minus && plus)
        {
            left = minus.anchoredPosition.x - minus.sizeDelta.x / 2;
            right = plus.anchoredPosition.x + plus.sizeDelta.x / 2;
            minus.gameObject.SetActive(false);
            plus.gameObject.SetActive(false);
        }
        label.gameObject.SetActive(false);

        Dropdown dropdown = Instantiate(template, parent);
        dropdown.name = name;
        dropdown.onValueChanged = new Dropdown.DropdownEvent();     // Drops the anti-aliasing dropdown's own listener
        dropdown.ClearOptions();
        dropdown.gameObject.SetActive(true);

        RectTransform rect = (RectTransform)dropdown.transform;
        rect.anchorMin = rect.anchorMax = label.anchorMin;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(right - left, rect.sizeDelta.y);
        rect.anchoredPosition = new Vector2((left + right) / 2, label.anchoredPosition.y);
        return dropdown;
    }

    // Shows the current category and device in the dropdowns, and lists the devices connected now
    void RefreshDropdowns()
    {
        m_settingDropdowns = true;
        if (m_categoryDropdown)
        {
            m_categoryDropdown.value = (int)GetCurrentActionCategory();
            m_categoryDropdown.RefreshShownValue();
        }

        if (m_deviceDropdown)
        {
            bool changed = m_deviceNames.Count != InputManager.Instance.devices.Count;
            for (int i = 0; i < InputManager.Instance.devices.Count; ++i)
            {
                string deviceName = InputManager.Instance.devices[i].GetDeviceName();
                if (i < m_deviceNames.Count && m_deviceNames[i] != deviceName)
                    changed = true;
            }
            if (changed)
            {
                m_deviceNames.Clear();
                foreach (IInputDevice device in InputManager.Instance.devices)
                    m_deviceNames.Add(device.GetDeviceName());
                m_deviceDropdown.ClearOptions();
                m_deviceDropdown.AddOptions(m_deviceNames);
            }
            m_deviceDropdown.value = deviceIndex;
            m_deviceDropdown.RefreshShownValue();
        }
        m_settingDropdowns = false;
    }

    public void SetDefaultControls()
    {
        SetDefaultControls(GetCurrentInputDevice(), GetCurrentActionCategoryMask());
    }

    void SetDefaultControls(IInputDevice device, int categoryMask)
    {
        InputRebinder.SetToDefault(actionsToEdit, InputManager.Instance.defaultControls, categoryMask, device);

        actionBindingsMenu.OnRebindComplete();
    }

    public void CycleDeviceForwards()
    {
        ++deviceIndex;
        if (deviceIndex >= InputManager.Instance.devices.Count)
        {
            deviceIndex = 0;
        }

        RefreshActionBindingsMenu();
    }

    public void CycleDeviceBackwards()
    {
        --deviceIndex;
        if (deviceIndex < 0)
        {
            deviceIndex = InputManager.Instance.devices.Count - 1;
        }

        RefreshActionBindingsMenu();
    }

    public void CycleActionCategoryForwards()
    {
        ++actionCategoryIndex;
        RefreshActionBindingsMenu();
    }

    public void CycleActionCategoryBackwards()
    {
        --actionCategoryIndex;
        if (actionCategoryIndex < 0)
        {
            actionCategoryIndex = EnumX<ActionCategory>.Count - 1;
        }

        RefreshActionBindingsMenu();
    }

    IInputDevice GetCurrentInputDevice()
    {
        // Todo, enum for current DISPLAYED device
        return InputManager.Instance.devices[deviceIndex];
    }

    ActionCategory GetCurrentActionCategory()
    {
        return (ActionCategory)(actionCategoryIndex % EnumX<ActionCategory>.Count);
    }

    int GetCurrentActionCategoryMask()
    {
        switch (GetCurrentActionCategory())
        {
            case ActionCategory.Editor:
                return MSChartEditorInput.Category.kEditorCategoryMask;

            case ActionCategory.Gameplay_Guitar:
                return MSChartEditorInput.Category.kGameplayGuitarCategoryMask;

            case ActionCategory.Gameplay_Drums:
                return MSChartEditorInput.Category.kGameplayDrumsCategoryMask;

            case ActionCategory.Gameplay_ProDrums:
                return MSChartEditorInput.Category.kGameplayDrumsProCategoryMask;

            default:
                break;
        }

        return MSChartEditorInput.Category.kEditorCategoryMask;
    }

    string GetCurrentActionCategoryName()
    {
        return GetActionCategoryName(GetCurrentActionCategory());
    }

    static string GetActionCategoryName(ActionCategory category)
    {
        switch (category)
        {
            case ActionCategory.Editor:
                return "Editor";

            case ActionCategory.Gameplay_Guitar:
                return "Gameplay - Guitar";

            case ActionCategory.Gameplay_Drums:
                return "Gameplay - Drums";

            case ActionCategory.Gameplay_ProDrums:
                return "Gameplay - Pro Drums";

            default:
                break;
        }

        return "";
    }

    void RefreshActionBindingsMenu()
    {
        actionCategoryText.text = GetCurrentActionCategoryName();
        deviceText.text = GetCurrentInputDevice().GetDeviceName();
        RefreshDropdowns();
        actionBindingsMenu.Setup(GetCurrentInputDevice(), actionsToEdit, GetCurrentActionCategoryMask());
    }

    public void SaveActionsToDefaultControlsFile()
    {
#if UNITY_EDITOR
        actionsToEdit.UpdateSaveData(true);

        string filepath = AssetDatabase.GetAssetPath(defaultControlsFile);
        System.IO.File.WriteAllText(filepath, JsonUtility.ToJson(actionsToEdit, true));
#endif
    }
}
