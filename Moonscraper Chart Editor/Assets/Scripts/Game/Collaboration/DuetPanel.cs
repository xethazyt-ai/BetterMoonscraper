// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

/// <summary>
/// The Duet Mode window. Press F10 to show or hide it. Drawn on top of the editor, so the editor's own UI is unchanged.
/// </summary>
public class DuetPanel : MonoBehaviour
{
    const KeyCode ToggleKey = KeyCode.F10;
    const int WindowId = 0x4475;    // Any number unique among IMGUI windows
    const string PrefUsername = "Duet.Username";
    const string PrefPort = "Duet.Port";
    const string PrefAddress = "Duet.Address";
    const string PrefAudioFolder = "Duet.AudioFolder";
    const string PrefUseRooms = "Duet.UseRooms";

    static DuetPanel s_instance;

    // Read by the editor so typing in this window doesn't trigger shortcuts or clicks fall through to the highway
    public static bool isTyping { get; private set; }

    public static bool IsPointerOverPanel()
    {
        if (s_instance == null || !s_instance.m_visible)
            return false;

        float scale = GuiScale;
        Vector2 pointer = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
        return s_instance.m_windowRect.Contains(pointer);
    }

    // IMGUI draws in screen pixels, so scale it up on screens taller than 1080 pixels to keep text readable
    static float GuiScale
    {
        get { return Mathf.Max(1f, Screen.height / 1080f); }
    }

    ChartEditor m_editor;
    DuetEditorSong m_song;
    DuetSession m_session;

    bool m_visible = false;
    Rect m_windowRect = new Rect(0, 0, 360, 10);
    bool m_windowPlaced = false;

    string m_username;
    string m_portText;
    string m_address;
    string m_audioFolder;
    string m_roomCodeInput = string.Empty;
    bool m_useRooms = true;
    bool m_sessionIsRoom = false;
    string m_error = null;
    List<string> m_localAddresses = null;

    void Start()
    {
        s_instance = this;
        m_editor = ChartEditor.Instance;
        m_song = new DuetEditorSong(m_editor, m_editor.songEditGestureTracker);
        m_editor.songEditGestureTracker.onEditFinished.Register(OnLocalEditFinished);

        m_username = PlayerPrefs.GetString(PrefUsername, Environment.UserName);
        m_portText = PlayerPrefs.GetInt(PrefPort, DuetSession.DefaultPort).ToString();
        m_address = PlayerPrefs.GetString(PrefAddress, string.Empty);
        m_audioFolder = PlayerPrefs.GetString(PrefAudioFolder, string.Empty);
        m_useRooms = PlayerPrefs.GetInt(PrefUseRooms, 1) != 0;
    }

    bool useRooms { get { return m_useRooms && DuetRooms.isAvailable; } }

    void OnDestroy()
    {
        if (m_session != null)
            m_session.Leave();

        isTyping = false;
    }

    void OnLocalEditFinished(in SongEditGestureTracker.FinishedEdit edit)
    {
        if (m_session != null)
            m_session.OnLocalEdit(edit.changes);
    }

    void Update()
    {
        if (Input.GetKeyDown(ToggleKey))
        {
            m_visible = !m_visible;
            if (!m_visible)
                isTyping = false;
        }

        if (m_session != null)
            m_session.Update();
    }

    void OnGUI()
    {
        if (!m_visible)
            return;

        float scale = GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        if (!m_windowPlaced)
        {
            m_windowRect.x = Screen.width / scale - m_windowRect.width - 20;
            m_windowRect.y = 60;
            m_windowPlaced = true;
        }

        // Clicking anywhere outside the window gives keyboard input back to the editor
        if (Event.current.type == EventType.MouseDown && !m_windowRect.Contains(Event.current.mousePosition))
            GUIUtility.keyboardControl = 0;

        m_windowRect = GUILayout.Window(WindowId, m_windowRect, DrawWindow, "Duet Mode  (F10 to hide)");
        isTyping = GUIUtility.keyboardControl != 0;
    }

    void DrawWindow(int id)
    {
        if (m_session == null || !m_session.isActive)
            DrawSetup();
        else
            DrawSession();

        GUI.DragWindow();
    }

    void DrawSetup()
    {
        if (m_session != null && !m_session.isActive)
            GUILayout.Label("Session ended: " + m_session.endReason);

        GUILayout.Label("Your name (shown to the other person)");
        m_username = GUILayout.TextField(m_username, 24);

        if (DuetRooms.isAvailable)
        {
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            bool rooms = GUILayout.Toggle(m_useRooms, "Room code", GUI.skin.button);
            bool direct = GUILayout.Toggle(!m_useRooms, "Direct IP", GUI.skin.button);
            GUILayout.EndHorizontal();

            // The two buttons act as tabs: clicking the other one switches, clicking the current one does nothing
            bool chosen = m_useRooms;
            if (rooms && !m_useRooms)
                chosen = true;
            else if (direct && m_useRooms)
                chosen = false;

            if (chosen != m_useRooms)
            {
                m_useRooms = chosen;
                PlayerPrefs.SetInt(PrefUseRooms, m_useRooms ? 1 : 0);
                m_error = null;
            }
        }

        if (!useRooms)
        {
            GUILayout.Space(8);
            GUILayout.Label("Port");
            m_portText = GUILayout.TextField(m_portText, 5);
        }

        GUILayout.Space(8);
        GUILayout.Label("HOST: share the chart that's open now");
        if (GUILayout.Button("Host session"))
            StartHosting();

        GUILayout.Space(8);
        GUILayout.Label("JOIN: get the host's chart");
        if (useRooms)
        {
            GUILayout.Label("Room code from the host");
            m_roomCodeInput = GUILayout.TextField(m_roomCodeInput, 12);
        }
        else
        {
            GUILayout.Label("Host's address (IP or name)");
            m_address = GUILayout.TextField(m_address, 128);
        }

        GUILayout.Label("Your folder with this song's audio (optional)");
        GUILayout.BeginHorizontal();
        m_audioFolder = GUILayout.TextField(m_audioFolder, 512);
        if (GUILayout.Button("Browse", GUILayout.Width(70)))
        {
            string folder;
            if (FileExplorer.OpenFolderPanel(out folder))
                m_audioFolder = folder;
        }
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Join session"))
            StartJoining();

        if (!string.IsNullOrEmpty(m_error))
            GUILayout.Label(m_error);

        if (useRooms)
            GUILayout.Label(DuetRooms.provider.status);
    }

    void DrawSession()
    {
        bool isHost = m_session.role == DuetSession.Role.Host;

        switch (m_session.state)
        {
            case DuetSession.State.Connecting:
                GUILayout.Label("Connecting...");
                break;
            case DuetSession.State.Joining:
                GUILayout.Label("Connected. Receiving the host's chart...");
                break;
            default:
                GUILayout.Label(isHost ? (m_sessionIsRoom ? "Hosting an online room" : "Hosting on port " + m_session.port) : "Joined. Edit number " + m_session.lastSeq);
                break;
        }

        if (isHost && m_sessionIsRoom)
        {
            string code = m_session.roomCode;
            if (code == null)
            {
                GUILayout.Label("Opening an online room...  " + DuetRooms.provider.status);
            }
            else
            {
                GUILayout.Label("Room code:  " + code.Substring(0, 3) + " " + code.Substring(3));
                if (GUILayout.Button("Copy room code"))
                    GUIUtility.systemCopyBuffer = code;
                GUILayout.Label("Your buddy picks Room code in their Duet window, types this in and joins. Works from any network.");
            }
        }
        else if (isHost)
        {
            if (m_localAddresses == null)
                m_localAddresses = LocalAddresses();

            GUILayout.Label("Your buddy joins with one of these addresses:");
            foreach (string address in m_localAddresses)
                GUILayout.Label("    " + address);
            GUILayout.Label("Over the internet they need your public IP with port " + m_session.port + " forwarded, or a shared Tailscale, ZeroTier or Radmin VPN network.");
        }

        GUILayout.Space(8);
        GUILayout.Label("In this session:");
        foreach (DuetUser user in m_session.users)
            GUILayout.Label("    " + user.name + (user.id == 0 ? " (host)" : string.Empty));

        if (!isHost && m_song.missingAudioFiles.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label("Not found in your audio folder: " + string.Join(", ", m_song.missingAudioFiles.ToArray()));
        }

        GUILayout.Space(8);
        foreach (string line in m_session.log)
            GUILayout.Label(line);

        GUILayout.Space(8);
        if (!isHost && m_session.state == DuetSession.State.Connected && GUILayout.Button("Get a fresh copy from the host"))
            m_session.RequestResync("Getting a fresh copy from the host.");

        if (GUILayout.Button(isHost ? "End session" : "Leave session"))
            m_session.Leave();
    }

    bool TryReadSettings(out int port)
    {
        m_error = null;
        port = 0;

        m_username = m_username.Trim();
        if (m_username.Length == 0)
        {
            m_error = "Enter a name first.";
            return false;
        }
        PlayerPrefs.SetString(PrefUsername, m_username);

        if (useRooms)
            return true;

        if (!int.TryParse(m_portText, out port) || port < 1 || port > 65535)
        {
            m_error = "The port must be a number from 1 to 65535.";
            return false;
        }
        PlayerPrefs.SetInt(PrefPort, port);
        return true;
    }

    void StartHosting()
    {
        int port;
        if (!TryReadSettings(out port))
            return;

        m_sessionIsRoom = useRooms;
        if (m_sessionIsRoom)
        {
            m_session = DuetSession.Host(m_song, m_username, DuetRooms.provider.CreateRoom(), "Opening an online room");
            return;
        }

        try
        {
            m_session = DuetSession.Host(m_song, m_username, port);
            m_localAddresses = null;
        }
        catch (SocketException e)
        {
            m_error = "Couldn't host on port " + port + ": " + e.Message + ". Another program may be using it; try a different port.";
        }
    }

    void StartJoining()
    {
        int port;
        if (!TryReadSettings(out port))
            return;

        string code = DuetRooms.NormalizeCode(m_roomCodeInput);
        m_address = m_address.Trim();
        if (useRooms && code.Length == 0)
        {
            m_error = "Enter the room code from the host.";
            return;
        }
        if (!useRooms && m_address.Length == 0)
        {
            m_error = "Enter the host's address.";
            return;
        }

        if (!m_editor.EditCheck())
            return;     // The user cancelled when asked about unsaved changes

        PlayerPrefs.SetString(PrefAudioFolder, m_audioFolder);
        m_song.localAudioFolder = m_audioFolder.Trim();

        m_sessionIsRoom = useRooms;
        if (m_sessionIsRoom)
        {
            m_session = DuetSession.Join(m_song, m_username, DuetRooms.provider.JoinRoom(code), "Looking for room " + code);
        }
        else
        {
            PlayerPrefs.SetString(PrefAddress, m_address);
            m_session = DuetSession.Join(m_song, m_username, m_address, port);
        }
    }

    static List<string> LocalAddresses()
    {
        List<string> addresses = new List<string>();
        try
        {
            foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
            {
                if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                    addresses.Add(address.ToString());
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Duet: couldn't list local addresses: " + e.Message);
        }

        if (addresses.Count <= 0)
            addresses.Add("(couldn't find this computer's address)");

        return addresses;
    }
}
