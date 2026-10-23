// See LICENSE in project root for license information.

// A one-off check that Epic Online Services runs inside this Unity version: start the SDK, sign in with an anonymous
// device ID, create a lobby under a room code, then find that lobby again by its code. Press F8 to run it.
//
// Only compiled when DUET_EOS is defined (Assets/csc.rsp), because the EOS SDK isn't part of the repository.
// Setup is in DuetTests/EOS-SPIKE.md.

#if DUET_EOS

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.Logging;
using Epic.OnlineServices.Platform;

public class DuetEosSpike : MonoBehaviour
{
    const KeyCode RunKey = KeyCode.F8;
    const string LibraryName = "EOSSDK-Win64-Shipping.dll";
    const string RoomCodeLetters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";    // No 0/O or 1/I/L, so codes are easy to read out

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateInstance()
    {
        GameObject go = new GameObject("Duet EOS spike");
        DontDestroyOnLoad(go);
        go.AddComponent<DuetEosSpike>();
    }

    // The EOS folder sits beside Assets in the editor, and beside the .exe in a build
    static string EosFolder { get { return Path.GetFullPath(Path.Combine(Application.dataPath, Path.Combine("..", "EOS"))); } }

    readonly List<string> m_log = new List<string>();
    bool m_running = false;
    bool m_sdkInitialized = false;
    IntPtr m_library = IntPtr.Zero;
    PlatformInterface m_platform;
    ProductUserId m_localUser;
    string m_roomCode;
    bool m_shutdownRequested = false;

    void Update()
    {
        if (Input.GetKeyDown(RunKey) && !m_running)
            Run();

        if (m_platform != null)
            m_platform.Tick();

        // Callbacks run inside Tick, so the platform is released here rather than from a callback
        if (m_shutdownRequested)
            Shutdown();
    }

    void OnGUI()
    {
        if (m_log.Count <= 0)
            return;

        GUILayout.BeginArea(new Rect(20, 60, 560, Screen.height - 80), GUI.skin.box);
        GUILayout.Label("EOS spike  (F8 to run again)");
        foreach (string line in m_log)
            GUILayout.Label(line);
        GUILayout.EndArea();
    }

    void Log(string line)
    {
        Debug.Log("EOS spike: " + line);
        m_log.Add(line);
        if (m_log.Count > 30)
            m_log.RemoveAt(0);
    }

    void Fail(string step, string reason)
    {
        Log("FAILED at " + step + ": " + reason);
        m_shutdownRequested = true;
    }

    void Run()
    {
        m_log.Clear();
        m_running = true;
        Log("Unity " + Application.unityVersion + ", " + (Application.isEditor ? "editor" : "build"));

        Dictionary<string, string> credentials;
        string error;
        if (!TryReadCredentials(out credentials, out error))
        {
            Fail("reading credentials", error);
            return;
        }

#if UNITY_EDITOR
        // The editor never unloads native plugins, and EOS can only start once per load, so load it by hand
        // and free it again in Shutdown
        string libraryPath = Path.Combine(EosFolder, LibraryName);
        m_library = LoadLibrary(libraryPath);
        if (m_library == IntPtr.Zero)
        {
            Fail("loading the SDK", "couldn't load " + libraryPath + " (Windows error " + Marshal.GetLastWin32Error() + ")");
            return;
        }
        Bindings.Hook(m_library, GetProcAddress);
        Log("Loaded " + LibraryName);
#endif

        InitializeOptions initializeOptions = new InitializeOptions
        {
            ProductName = "Moonscraper Duet",
            ProductVersion = "spike",
        };
        Result result = PlatformInterface.Initialize(ref initializeOptions);
        if (result != Result.Success && result != Result.AlreadyConfigured)
        {
            Fail("starting the SDK", result.ToString());
            return;
        }
        m_sdkInitialized = true;
        Log("SDK started (" + result + ")");

        LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Warning);
        LoggingInterface.SetCallback((ref LogMessage message) => Debug.LogWarning("EOS [" + message.Category + "] " + message.Message));

        Options options = new Options
        {
            ProductId = credentials["ProductId"],
            SandboxId = credentials["SandboxId"],
            DeploymentId = credentials["DeploymentId"],
            ClientCredentials = new ClientCredentials { ClientId = credentials["ClientId"], ClientSecret = credentials["ClientSecret"] },
            Flags = Application.isEditor ? PlatformFlags.LoadingInEditor : PlatformFlags.DisableOverlay,
            CacheDirectory = Path.Combine(Application.temporaryCachePath, "EOS"),
        };
        Directory.CreateDirectory(Path.Combine(Application.temporaryCachePath, "EOS"));

        m_platform = PlatformInterface.Create(ref options);
        if (m_platform == null)
        {
            Fail("creating the platform", "Create returned nothing. Check the five values in credentials.txt");
            return;
        }
        Log("Platform created");

        CreateDeviceIdOptions deviceOptions = new CreateDeviceIdOptions { DeviceModel = SystemInfo.deviceModel };
        m_platform.GetConnectInterface().CreateDeviceId(ref deviceOptions, null, OnDeviceIdCreated);
    }

    void OnDeviceIdCreated(ref CreateDeviceIdCallbackInfo info)
    {
        // DuplicateNotAllowed means this PC already has a device ID, which is fine
        if (info.ResultCode != Result.Success && info.ResultCode != Result.DuplicateNotAllowed)
        {
            Fail("creating a device ID", info.ResultCode.ToString());
            return;
        }
        Log("Device ID ready (" + info.ResultCode + ")");

        LoginOptions loginOptions = new LoginOptions
        {
            Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken, Token = null },
            UserLoginInfo = new UserLoginInfo { DisplayName = "Duet spike" },
        };
        m_platform.GetConnectInterface().Login(ref loginOptions, null, OnLoggedIn);
    }

    void OnLoggedIn(ref LoginCallbackInfo info)
    {
        if (info.ResultCode != Result.Success)
        {
            Fail("signing in", info.ResultCode.ToString());
            return;
        }

        m_localUser = info.LocalUserId;
        Log("Signed in as " + m_localUser);

        m_roomCode = NewRoomCode();
        CreateLobbyOptions lobbyOptions = new CreateLobbyOptions
        {
            LocalUserId = m_localUser,
            MaxLobbyMembers = 4,
            PermissionLevel = LobbyPermissionLevel.Publicadvertised,
            PresenceEnabled = false,
            AllowInvites = false,
            BucketId = "duet",
            LobbyId = "duet" + m_roomCode,
            EnableJoinById = true,
        };
        m_platform.GetLobbyInterface().CreateLobby(ref lobbyOptions, null, OnLobbyCreated);
    }

    void OnLobbyCreated(ref CreateLobbyCallbackInfo info)
    {
        if (info.ResultCode != Result.Success)
        {
            Fail("creating a lobby", info.ResultCode.ToString());
            return;
        }
        Log("Lobby created with room code " + m_roomCode + " (lobby " + info.LobbyId + ")");

        CreateLobbySearchOptions searchOptions = new CreateLobbySearchOptions { MaxResults = 1 };
        LobbySearch search;
        Result result = m_platform.GetLobbyInterface().CreateLobbySearch(ref searchOptions, out search);
        if (result != Result.Success)
        {
            Fail("starting a lobby search", result.ToString());
            return;
        }

        LobbySearchSetLobbyIdOptions idOptions = new LobbySearchSetLobbyIdOptions { LobbyId = "duet" + m_roomCode };
        search.SetLobbyId(ref idOptions);

        LobbySearchFindOptions findOptions = new LobbySearchFindOptions { LocalUserId = m_localUser };
        search.Find(ref findOptions, search, OnLobbyFound);
    }

    void OnLobbyFound(ref LobbySearchFindCallbackInfo info)
    {
        LobbySearch search = (LobbySearch)info.ClientData;
        if (info.ResultCode != Result.Success)
        {
            search.Release();
            Fail("finding the lobby by its code", info.ResultCode.ToString());
            return;
        }

        LobbySearchGetSearchResultCountOptions countOptions = new LobbySearchGetSearchResultCountOptions();
        uint count = search.GetSearchResultCount(ref countOptions);
        search.Release();
        if (count != 1)
        {
            Fail("finding the lobby by its code", count + " results");
            return;
        }
        Log("Found the lobby again by its room code");

        LeaveLobbyOptions leaveOptions = new LeaveLobbyOptions { LocalUserId = m_localUser, LobbyId = "duet" + m_roomCode };
        m_platform.GetLobbyInterface().LeaveLobby(ref leaveOptions, null, OnLobbyLeft);
    }

    void OnLobbyLeft(ref LeaveLobbyCallbackInfo info)
    {
        Log("Left the lobby (" + info.ResultCode + ")");
        Log("PASSED: EOS works in this Unity version");
        m_shutdownRequested = true;
    }

    static string NewRoomCode()
    {
        System.Random random = new System.Random();
        char[] code = new char[6];
        for (int i = 0; i < code.Length; ++i)
            code[i] = RoomCodeLetters[random.Next(RoomCodeLetters.Length)];
        return new string(code);
    }

    // EOS/credentials.txt holds lines of Name=Value for the five values from the Epic Developer Portal
    static bool TryReadCredentials(out Dictionary<string, string> credentials, out string error)
    {
        credentials = new Dictionary<string, string>();
        string path = Path.Combine(EosFolder, "credentials.txt");
        if (!File.Exists(path))
        {
            error = "no file at " + path;
            return false;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            int split = line.IndexOf('=');
            if (split > 0)
                credentials[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
        }

        foreach (string name in new[] { "ProductId", "SandboxId", "DeploymentId", "ClientId", "ClientSecret" })
        {
            string value;
            if (!credentials.TryGetValue(name, out value) || value.Length == 0)
            {
                error = name + " is missing from " + path;
                return false;
            }
        }

        error = null;
        return true;
    }

    void Shutdown()
    {
        if (m_platform != null)
        {
            m_platform.Release();
            m_platform = null;
        }

        if (m_sdkInitialized)
        {
            PlatformInterface.Shutdown();
            m_sdkInitialized = false;
        }

#if UNITY_EDITOR
        if (m_library != IntPtr.Zero)
        {
            Bindings.Unhook();
            FreeLibrary(m_library);
            m_library = IntPtr.Zero;
        }
#endif

        m_running = false;
        m_shutdownRequested = false;
    }

    void OnApplicationQuit()
    {
        Shutdown();
    }

#if UNITY_EDITOR
    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32", CharSet = CharSet.Ansi)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);
#endif
}

#endif
