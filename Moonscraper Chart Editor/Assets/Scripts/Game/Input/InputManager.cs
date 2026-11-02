// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

#define SDL_INPUT

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MoonscraperEngine.Input;
using System;
using SDL2;
using System.Threading.Tasks;
using System.Threading;

/// <summary>
/// Handles controller connection events and updates controller inputs
/// </summary>
[UnitySingleton(UnitySingletonAttribute.Type.LoadedFromResources, false, "Prefabs/InputManager")]
public class InputManager : UnitySingleton<InputManager>
{
    const string PrefConnectedNotice = "Controls.ConnectedNotice";

    /// <summary>
    /// Settings > Editor: the "... CONNECTED" notice when a controller or guitar is plugged in. Off unless turned on.
    /// </summary>
    public static bool connectedNotice
    {
        get { return PlayerPrefs.GetInt(PrefConnectedNotice, 0) != 0; }
        set { PlayerPrefs.SetInt(PrefConnectedNotice, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    static void ShowConnectedNotice(string message)
    {
        if (connectedNotice && ChartEditor.InstanceExists && ChartEditor.Instance.services != null && ChartEditor.Instance.services.notificationBar)
            ChartEditor.Instance.services.notificationBar.PushNotification(message, 5, true);
    }

    const int SDL_INIT_TIMEOUT_MILLISECONDS = 10000;

    [SerializeField]
    TextAsset inputPropertiesJson;
    InputConfig _inputProperties;
    public InputConfig inputProperties
    {
        get
        {
            if (_inputProperties == null)
            {
                _inputProperties = new InputConfig();
                InputConfig.LoadFromTextAsset(inputPropertiesJson, _inputProperties);
            }

            return _inputProperties;
        }
    }

    [SerializeField]
    TextAsset defaultControlsJson;
    [HideInInspector]
    MSChartEditorInput.MSChartEditorActionContainer _defaultControls;

    volatile bool sdlInitialised = false;

    public MSChartEditorInput.MSChartEditorActionContainer defaultControls
    {
        get
        {
            if (_defaultControls == null)
            {
                _defaultControls = JsonUtility.FromJson<MSChartEditorInput.MSChartEditorActionContainer>(defaultControlsJson.text);
                _defaultControls.LoadFromSaveData(_defaultControls);
            }

            return _defaultControls;
        }
    }
    public MoonscraperEngine.Event<IInputDevice> disconnectEvent = new MoonscraperEngine.Event<IInputDevice>();

    public List<IInputDevice> devices = new List<IInputDevice>() { new KeyboardDevice() };

    public List<GamepadDevice> controllers = new List<GamepadDevice>();
    public List<JoystickDevice> joysticks = new List<JoystickDevice>();

    private void Start()
    {
        InitSdl();
    }

    async void InitSdl()
    {
        using (CancellationTokenSource cancellationTokenSource = new CancellationTokenSource())
        {
            Task task = Task.Factory.StartNew(() =>
            {
#if SDL_INPUT
                const uint SDL_FLAGS = SDL.SDL_INIT_GAMECONTROLLER | SDL.SDL_INIT_JOYSTICK;
#else
                // Having sporatic boot issues, disable if necessary
                const uint SDL_FLAGS = 0;
#endif
                Debug.LogFormat("Initialising SDL input {0}", SDL_FLAGS);

                SDL.SDL_SetMainReady();
                SetGuitarFriendlyHints();

                Debug.Log("SDL input main ready");

                if (SDL.SDL_Init(SDL_FLAGS) != 0)
                {
                    Debug.LogError("SDL could not initialise! SDL Error: " + SDL.SDL_GetError());
                }
                else
                {
                    sdlInitialised = true;

                    SDL.SDL_GetVersion(out var sdlVersion);

                    Debug.Log($"Successfully initialised SDL input, version {sdlVersion.major}.{sdlVersion.minor}.{sdlVersion.patch}");
                }

            }, cancellationTokenSource.Token);

            var firstCompleteTask = await Task.WhenAny(task, Task.Delay(SDL_INIT_TIMEOUT_MILLISECONDS));
            if (firstCompleteTask != task)
            {
                cancellationTokenSource.Cancel();

                string message = "Controller input failed to initialise due to time out.";
                NativeMessageBox.Show(message, "Error", NativeMessageBox.Type.OK, ChartEditor.Instance.windowHandleManager.nativeWindow);
            }
            else if (firstCompleteTask.IsFaulted)
            {
                foreach (var ex in task.Exception?.InnerExceptions)
                {
                    if (ex is DllNotFoundException dlNotFoundException)
                    {
#if (UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX)
        string message = "SDL2 Linux runtime dependency not found. Please install required dependencies (ffmpeg, sdl2, libx11-6, libgtk-3-0) and restart the application.";
        NativeMessageBox.Show(message, "Oops", NativeMessageBox.Type.OK, ChartEditor.Instance.windowHandleManager.nativeWindow);

        ChartEditor.Instance.ForceQuit();
#else
                        throw dlNotFoundException;
#endif
                    }
                    else
                    {
                        throw ex;
                    }
                }
            }
        }
    }

    // Set before SDL starts, so every kind of guitar is found:
    // - XInput guitars (Xbox 360 guitars, most Arduino/Pico guitars such as Santroller) stay on Windows' XInput, which
    //   is what Clone Hero reads them with. HIDAPI's Xbox driver and Raw Input are kept off so they can't take them over.
    // - DirectInput guitars and adapters (PS3/Wii dongles, Raphnet, Arduino guitars in HID mode) come in as joysticks.
    // - SDL is started on a worker thread, so plugging in is watched on SDL's own thread (newer SDL versions) and
    //   controllers keep working while another window has focus.
    // Hints this SDL version doesn't know are ignored.
    static void SetGuitarFriendlyHints()
    {
        SDL.SDL_SetHint("SDL_XINPUT_ENABLED", "1");
        SDL.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        SDL.SDL_SetHint("SDL_JOYSTICK_THREAD", "1");
        SDL.SDL_SetHint("SDL_JOYSTICK_RAWINPUT", "0");
        SDL.SDL_SetHint("SDL_JOYSTICK_HIDAPI_XBOX", "0");
    }

    // Update is called once per frame
    void Update () 
    {
        UpdateSdl();
        RescanForMissedDevices();
    }

    // A guitar whose plug-in message SDL never sent (seen with some adapters, and ones plugged in while BetterMoonscraper
    // was starting) is picked up here: every couple of seconds, anything SDL lists that isn't open yet is opened.
    const float RescanSeconds = 2;
    float m_nextRescan = 0;

    void RescanForMissedDevices()
    {
        if (!sdlInitialised || Time.unscaledTime < m_nextRescan)
            return;
        m_nextRescan = Time.unscaledTime + RescanSeconds;

        int count = SDL.SDL_NumJoysticks();
        for (int index = 0; index < count; ++index)
        {
            if (IsOpen(SDL.SDL_JoystickGetDeviceInstanceID(index)))
                continue;
            Debug.Log("Found a controller SDL hadn't announced, opening it: " + DescribeIndex(index));
            if (SDL.SDL_IsGameController(index) == SDL.SDL_bool.SDL_TRUE)
                OnControllerConnect(index);
            else
                OnJoystickConnect(index);
        }
    }

    bool IsOpen(int instanceId)
    {
        if (instanceId < 0)
            return true;    // Already gone again
        foreach (GamepadDevice gamepad in controllers)
        {
            if (SDL.SDL_JoystickInstanceID(SDL.SDL_GameControllerGetJoystick(gamepad.sdlHandle)) == instanceId)
                return true;
        }
        foreach (JoystickDevice joystick in joysticks)
        {
            if (SDL.SDL_JoystickInstanceID(joystick.sdlHandle) == instanceId)
                return true;
        }
        return false;
    }

    // For the log, so a guitar that won't work can be looked up: its name, USB IDs and what SDL takes it for
    static string DescribeIndex(int index)
    {
        try
        {
            return string.Format("\"{0}\" (USB {1:X4}:{2:X4}, {3}, {4})", SDL.SDL_JoystickNameForIndex(index),
                SDL.SDL_JoystickGetDeviceVendor(index), SDL.SDL_JoystickGetDeviceProduct(index), SDL.SDL_JoystickGetDeviceType(index),
                SDL.SDL_IsGameController(index) == SDL.SDL_bool.SDL_TRUE ? "XInput/known controller" : "DirectInput joystick");
        }
        catch (Exception e)
        {
            return "device " + index + " (" + e.Message + ")";
        }
    }

    void UpdateSdl()
    {
        if (!sdlInitialised)
        {
            return;
        }

        SDL.SDL_Event sdlEvent;
        while (SDL.SDL_PollEvent(out sdlEvent) > 0)
        {
            switch (sdlEvent.type)
            {
                case SDL.SDL_EventType.SDL_JOYDEVICEADDED:
                    {
                        int index = sdlEvent.jdevice.which;
                        if (SDL.SDL_IsGameController(sdlEvent.jdevice.which) == SDL.SDL_bool.SDL_TRUE)
                        {
                            OnControllerConnect(index);
                        }
                        else
                        {
                            OnJoystickConnect(index);
                        }
                        break;
                    }
                case SDL.SDL_EventType.SDL_JOYDEVICEREMOVED:
                    {
                        IntPtr removedController = SDL.SDL_GameControllerFromInstanceID(sdlEvent.jdevice.which);

                        if (removedController != IntPtr.Zero)
                        {
                            OnControllerDisconnect(sdlEvent.jdevice.which);
                        }
                        else
                        {
                            OnJoystickDisconnect(sdlEvent.jdevice.which);
                        }
                        break;
                    }

                default: break;
            }
        }

        foreach (GamepadDevice gamepad in controllers)
        {
            gamepad.Update(ChartEditor.hasFocus);
        }

        foreach (JoystickDevice joystick in joysticks)
        {
            joystick.Update(ChartEditor.hasFocus);
        }
    }

    void OnControllerConnect(int index)
    {
        if (IsOpen(SDL.SDL_JoystickGetDeviceInstanceID(index)))
            return;     // Opened already by the rescan
        Debug.Log("Controller plugged in: " + DescribeIndex(index));

        IntPtr gameController = SDL.SDL_GameControllerOpen(index);
        if (gameController != IntPtr.Zero)
        {
            Debug.Log("Added controller device " + index);

            GamepadDevice gamepad = new GamepadDevice(gameController);
            Debug.Log("Controller " + gamepad.GetDeviceName() + " (XInput or a known controller, so it uses the gamepad controls)");
            ShowConnectedNotice(gamepad.GetDeviceName().ToUpperInvariant() + " CONNECTED");
            controllers.Add(gamepad);
            devices.Add(gamepad);

            Debug.Log("Controller count = " + controllers.Count);
            Debug.Log("Device count = " + devices.Count);
        }
        else
        {
            Debug.LogError("Failed to get SDL Game Controller address " + index + ". " + SDL.SDL_GetError());
        }
    }

    void OnJoystickConnect(int index)
    {
        if (IsOpen(SDL.SDL_JoystickGetDeviceInstanceID(index)))
            return;     // Opened already by the rescan
        Debug.Log("Joystick plugged in: " + DescribeIndex(index));

        IntPtr joystick = SDL.SDL_JoystickOpen(index);
        if (joystick != IntPtr.Zero)
        {
            Debug.Log("Added joystick device " + index);

            JoystickDevice gamepad = new JoystickDevice(joystick);
            Debug.Log("Joystick " + gamepad.Describe() + ". Bind it in Settings > Controls with that device picked.");
            ShowConnectedNotice(gamepad.GetDeviceName().ToUpperInvariant() + " CONNECTED: PICK IT IN SETTINGS > CONTROLS TO BIND IT");
            joysticks.Add(gamepad);
            devices.Add(gamepad);

            Debug.Log("Joystick count = " + joysticks.Count);
            Debug.Log("Device count = " + devices.Count);
        }
        else
        {
            Debug.LogError("Failed to get SDL Joystick address " + index + ". " + SDL.SDL_GetError());
        }
    }

    void OnControllerDisconnect(int instanceId)
    {
        Debug.Log("Removed controller device " + instanceId);

        IntPtr removedController = SDL.SDL_GameControllerFromInstanceID(instanceId);

        Debug.Assert(removedController != IntPtr.Zero);

        for (int i = 0; i < controllers.Count; ++i)
        {
            if (controllers[i].sdlHandle == removedController)
            {
                IInputDevice device = controllers[i];

                controllers[i].Disconnect();
                SDL.SDL_GameControllerClose(removedController);

                bool deviceRemoved = devices.Remove(controllers[i]);
                Debug.Assert(deviceRemoved);
                controllers.RemoveAt(i);
                disconnectEvent.Fire(device);

                Debug.Log("Controller count = " + controllers.Count);
                Debug.Log("Device count = " + devices.Count);

                return;
            }
        }

        Debug.Assert(false, "Unable to find controller " + instanceId + " to remove");
    }

    void OnJoystickDisconnect(int instanceId)
    {
        Debug.Log("Removed joystick device " + instanceId);

        IntPtr removedController = SDL.SDL_JoystickFromInstanceID(instanceId);

        Debug.Assert(removedController != IntPtr.Zero);

        for (int i = 0; i < joysticks.Count; ++i)
        {
            if (joysticks[i].sdlHandle == removedController)
            {
                IInputDevice device = joysticks[i];

                joysticks[i].Disconnect();
                SDL.SDL_JoystickClose(removedController);

                bool deviceRemoved = devices.Remove(joysticks[i]);
                Debug.Assert(deviceRemoved);
                joysticks.RemoveAt(i);
                disconnectEvent.Fire(device);

                Debug.Log("Joystick count = " + joysticks.Count);
                Debug.Log("Device count = " + devices.Count);

                return;
            }
        }

        Debug.Assert(false, "Unable to find joystick " + instanceId + " to remove");
    }

    public void Dispose()
    {
        if (!sdlInitialised)
        {
            return;
        }

        foreach (GamepadDevice gamepad in controllers)
        {
            SDL.SDL_GameControllerClose(gamepad.sdlHandle);
        }

        controllers.Clear();
    }
}
