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

    // Guitar Controller: the guitar actions for whichever controller was last pressed, so a guitar can be bound
    // without picking it from the device list first
    IInputDevice m_guitarDevice;
    static readonly InputAction.Properties s_anyInput = new InputAction.Properties() { allowSameFrameMultiInput = false };

    MSChartEditorInput.MSChartEditorActionContainer actionsToEdit = null;

    enum ActionCategory
    {
        Editor,
        Guitar_Controller,
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
        if (m_guitarDevice == device)
            m_guitarDevice = null;
        RefreshActionBindingsMenu();
    }

    void Update()
    {
        if (GetCurrentActionCategory() != ActionCategory.Guitar_Controller || actionBindingsMenu.isRebinding)
            return;

        // Plugged in, unplugged, or another controller pressed: show that one
        IInputDevice pressed = null;
        foreach (IInputDevice device in InputManager.Instance.devices)
        {
            if (device.Type != MoonscraperEngine.Input.DeviceType.Keyboard && device.Connected && device != m_guitarDevice && device.GetCurrentInput(s_anyInput) != null)
            {
                pressed = device;
                break;
            }
        }

        if (pressed != null || (m_guitarDevice != null && !m_guitarDevice.Connected) || (m_guitarDevice == null && FirstController() != null))
        {
            m_guitarDevice = pressed ?? FirstController();
            RefreshActionBindingsMenu();
        }
    }

    static IInputDevice FirstController()
    {
        IInputDevice found = null;
        foreach (IInputDevice device in InputManager.Instance.devices)
        {
            if (device.Type != MoonscraperEngine.Input.DeviceType.Keyboard && device.Connected)
                found = device;     // The last one, which is the newest plugged in
        }
        return found;
    }

    // Guitar Controller's device arrows step through the controllers only
    void CycleGuitarDevice(int step)
    {
        List<IInputDevice> controllers = new List<IInputDevice>();
        foreach (IInputDevice device in InputManager.Instance.devices)
        {
            if (device.Type != MoonscraperEngine.Input.DeviceType.Keyboard && device.Connected)
                controllers.Add(device);
        }

        if (controllers.Count > 0)
        {
            int index = controllers.IndexOf(m_guitarDevice);
            index = ((index < 0 ? 0 : index + step) % controllers.Count + controllers.Count) % controllers.Count;
            m_guitarDevice = controllers[index];
        }
        RefreshActionBindingsMenu();
    }

    private void OnEnable()
    {
        SetActionsToEdit();
        RefreshActionBindingsMenu();
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
        if (GetCurrentActionCategory() == ActionCategory.Guitar_Controller)
        {
            CycleGuitarDevice(1);
            return;
        }

        ++deviceIndex;
        if (deviceIndex >= InputManager.Instance.devices.Count)
        {
            deviceIndex = 0;
        }

        RefreshActionBindingsMenu();
    }

    public void CycleDeviceBackwards()
    {
        if (GetCurrentActionCategory() == ActionCategory.Guitar_Controller)
        {
            CycleGuitarDevice(-1);
            return;
        }

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
        if (GetCurrentActionCategory() == ActionCategory.Guitar_Controller)
        {
            if (m_guitarDevice == null || !m_guitarDevice.Connected)
                m_guitarDevice = FirstController();
            if (m_guitarDevice != null)
                return m_guitarDevice;
        }

        // Todo, enum for current DISPLAYED device
        if (deviceIndex >= InputManager.Instance.devices.Count)
            deviceIndex = 0;
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

            case ActionCategory.Guitar_Controller:
                return GetCurrentInputDevice().Type == MoonscraperEngine.Input.DeviceType.Keyboard ? 0 : MSChartEditorInput.Category.kGameplayGuitarCategoryMask;

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
        switch (GetCurrentActionCategory())
        {
            case ActionCategory.Editor:
                return "Editor";

            case ActionCategory.Guitar_Controller:
                return "Guitar Controller";

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
        if (GetCurrentActionCategory() == ActionCategory.Guitar_Controller)
        {
            deviceText.text = GetCurrentInputDevice().Type == MoonscraperEngine.Input.DeviceType.Keyboard
                ? "Plug in your guitar (XInput, HID or adapter)"
                : GetCurrentInputDevice().GetDeviceName() + " (press any button to switch guitars)";
        }
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
