// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

/// <summary>
/// The Duet Mode window. Press F10 (rebindable in Controls) to show or hide it. Drawn on top of the editor, so the editor's own UI is unchanged.
/// </summary>
public class DuetPanel : MonoBehaviour
{
    const int WindowId = 0x4475;    // Any number unique among IMGUI windows
    const string PrefUsername = "Duet.Username";
    const string PrefPort = "Duet.Port";
    const string PrefAddress = "Duet.Address";
    const string PrefRejoin = "Duet.Rejoin";    // Set while joined, so closing Moonscraper mid-session rejoins next time
    const string PrefRehost = "Duet.Rehost";    // Set while hosting, and cleared on a normal close, so it's only left after a crash
    const string PrefUseRooms = "Duet.UseRooms";
    const string PrefColor = "Duet.Color";
    const string PrefShowGhosts = "Duet.ShowGhostNotes";
    const string PrefRainbowSpeed = "Duet.RainbowSpeed";

    static DuetPanel s_instance;

    // Read by the editor so typing in this window doesn't trigger shortcuts or clicks fall through to the highway
    public static bool isTyping { get; private set; }

    // True while in a session someone else is hosting. Only the host saves the chart or picks its audio.
    public static bool isGuest
    {
        get { return s_instance != null && s_instance.m_session != null && s_instance.m_session.isActive && s_instance.m_session.role == DuetSession.Role.Client; }
    }

    // The session in progress, if any, for drawing everyone's cursors
    public static DuetSession activeSession
    {
        get { return s_instance != null && s_instance.m_session != null && s_instance.m_session.isActive ? s_instance.m_session : null; }
    }

    // True from a mouse press on this window until the button is let go, even once the mouse leaves the window
    public static bool isMouseHeldOnPanel { get; private set; }

    // Closing Moonscraper asks here rather than with a Windows pop-up, which would freeze the editor and drop a Duet
    // session. Nothing to ask when there's no session and nothing unsaved. May be called off the main thread.
    static volatile bool s_quitPromptOpen = false;

    public static bool RequestQuitConfirmation()
    {
        if (s_instance == null)
            return false;
        if (activeSession == null && (!ChartEditor.isDirty || isGuest))
            return false;

        s_quitPromptOpen = true;
        return true;
    }

    public static int localColor { get { return s_instance != null ? s_instance.m_color : 0; } }
    public static bool showGhostNotes { get { return s_instance == null || s_instance.m_showGhosts; } }

    // True while a Moonscraper menu such as Settings is open and the Duet windows, cursors and markers should get out of the way
    public static bool isHudHidden
    {
        get { return s_instance != null && s_instance.m_editor != null && s_instance.m_editor.currentState == ChartEditor.State.Menu; }
    }

    public static bool IsPointerOverPanel()
    {
        if (DuetChat.IsPointerOver() || s_quitPromptOpen)
            return true;

        if (s_instance == null || !s_instance.m_visible || isHudHidden)
            return false;

        float scale = GuiScale;
        Vector2 pointer = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
        return s_instance.m_window.rect.Contains(pointer) && !DuetMenuCover.Covers(s_instance.m_window.rect, scale);
    }

    // IMGUI draws in screen pixels, so scale it up on screens taller than 1080 pixels to keep text readable
    public static float GuiScale
    {
        get { return Mathf.Max(1f, Screen.height / 1080f); }
    }

    ChartEditor m_editor;
    DuetEditorSong m_song;
    DuetSession m_session;

    bool m_visible = false;
    readonly DuetWindow m_window = new DuetWindow(WindowId, "Duet.Window", new Rect(0, 0, 360, 10), new Vector2(280, 160), false, true);
    bool m_windowPlaced = false;

    string m_username;
    string m_portText;
    string m_address;
    string m_password = string.Empty;
    const int MinPasswordLength = 4;   // Direct IP only. Not saved, except to rejoin after closing Moonscraper.
    string m_roomCodeInput = string.Empty;
    bool m_useRooms = true;
    bool m_sessionIsRoom = false;
    bool m_unloadedAfterSession = false;
    bool m_rejoinSaved = false;
    string m_joinedCode;
    bool m_rejoinPending = false;
    bool m_rehostPending = false;
    string m_savedRehost = null;
    bool m_quitHandled = false;
    int m_color = 0;
    string m_hexInput = string.Empty;
    bool m_showCustomColor = false;
    int m_rainbowSpeed = 5;
    bool m_showGhosts = true;
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
        m_useRooms = PlayerPrefs.GetInt(PrefUseRooms, 1) != 0;
        m_showGhosts = PlayerPrefs.GetInt(PrefShowGhosts, 1) != 0;

        // A random color the first time, so two people who never pick one still look different
        if (!PlayerPrefs.HasKey(PrefColor))
            PlayerPrefs.SetInt(PrefColor, new System.Random().Next(DuetPresence.ColorCount));
        m_color = PlayerPrefs.GetInt(PrefColor, 0);
        if (!DuetPresence.IsValidColor(m_color))
            m_color = 0;
        m_hexInput = SliderHexOf(m_color);
        m_rainbowSpeed = Mathf.Clamp(PlayerPrefs.GetInt(PrefRainbowSpeed, 5), 1, DuetPresence.MaxRainbowSpeed);

        m_rejoinPending = PlayerPrefs.HasKey(PrefRejoin);
        m_rehostPending = PlayerPrefs.HasKey(PrefRehost);

        DuetRooms.quitting += OnQuitting;
        Application.quitting += OnQuitting;
    }

    // Closing Moonscraper normally ends the session on purpose, so joiners aren't left waiting as if it crashed.
    // Online rooms call this before their connections close; Application.quitting covers direct IP.
    void OnQuitting()
    {
        if (m_quitHandled)
            return;
        m_quitHandled = true;

        PlayerPrefs.DeleteKey(PrefRehost);
        PlayerPrefs.Save();

        if (m_session != null && m_session.isActive)
        {
            bool directHost = m_session.role == DuetSession.Role.Host && !m_sessionIsRoom;
            m_session.Leave();
            if (directHost)
                System.Threading.Thread.Sleep(200);     // Time for the goodbye to go out before Moonscraper closes
        }
    }

    // Saves what's needed to open the same session again if Moonscraper crashes while hosting
    void RememberHosting()
    {
        string info = null;
        if (m_session.isActive)
        {
            if (m_session.state != DuetSession.State.Connected || (m_sessionIsRoom && m_session.roomCode == null))
                return;     // Still opening the room

            string settings = m_session.maxUsers + "\n" + (m_session.isLocked ? 1 : 0) + "\n" + (m_editor.currentChartFile ?? string.Empty);
            info = m_sessionIsRoom ? "room\n" + m_session.roomCode + "\n" + settings : "ip\n" + m_session.port + "\n" + m_password + "\n" + settings;
        }

        if (info == m_savedRehost)
            return;

        m_savedRehost = info;
        if (info != null)
            PlayerPrefs.SetString(PrefRehost, info);
        else
            PlayerPrefs.DeleteKey(PrefRehost);     // Ended on purpose
        PlayerPrefs.Save();
    }

    // Moonscraper crashed while hosting: open the chart again and the same room, and get the latest chart back
    // from whoever is still waiting in it
    System.Collections.IEnumerator RehostAfterCrash()
    {
        m_rehostPending = false;
        string[] saved = PlayerPrefs.GetString(PrefRehost, string.Empty).Split('\n');
        PlayerPrefs.DeleteKey(PrefRehost);     // Saved again once hosting, so a crash while starting doesn't repeat forever
        PlayerPrefs.Save();

        bool room = saved.Length == 5 && saved[0] == "room";
        bool ip = saved.Length == 6 && saved[0] == "ip";
        if (!room && !ip)
            yield break;

        int offset = room ? 2 : 3;
        int maxUsers;
        int.TryParse(saved[offset], out maxUsers);
        bool locked = saved[offset + 1] == "1";
        string chart = saved[offset + 2];

        m_editor.services.notificationBar.PushNotification("RESTARTING YOUR COLLAB SESSION", 3, true);
        if (!string.IsNullOrEmpty(chart) && System.IO.File.Exists(chart))
        {
            yield return m_editor.StartCoroutine(m_editor._Load(chart));
            while (m_editor.services.loadingTasksManager.isRunningTask)
                yield return null;
        }

        if (room && DuetRooms.isAvailable)
        {
            m_sessionIsRoom = true;
            m_useRooms = true;
            m_session = DuetSession.Host(m_song, m_username, DuetRooms.provider.CreateRoom(saved[1]), "Opening room " + saved[1] + " again");
        }
        else if (ip)
        {
            int port;
            int.TryParse(saved[1], out port);
            m_sessionIsRoom = false;
            m_useRooms = false;
            m_password = saved[2];
            m_portText = saved[1];
            try
            {
                m_session = DuetSession.Host(m_song, m_username, port, m_password);
                m_localAddresses = null;
            }
            catch (SocketException e)
            {
                m_error = "Couldn't host on port " + port + " again: " + e.Message;
                yield break;
            }
        }
        else
        {
            yield break;
        }

        m_session.maxUsers = maxUsers;
        m_session.isLocked = locked;
        m_session.ExpectRecovery();
        m_savedRehost = null;
        m_visible = true;
    }

    // A joiner who closed Moonscraper without leaving goes back into the session, if the host still has it open
    void RejoinIfLeftOpen()
    {
        m_rejoinPending = false;
        string[] saved = PlayerPrefs.GetString(PrefRejoin, string.Empty).Split('\n');
        PlayerPrefs.DeleteKey(PrefRejoin);

        // Each ends with the host's rejoin secret, which gets you back in even if the session was locked meanwhile
        string token;
        if (saved.Length == 3 && saved[0] == "room" && DuetRooms.isAvailable)
        {
            m_useRooms = true;
            m_roomCodeInput = saved[1];
            token = saved[2];
        }
        else if (saved.Length == 5 && saved[0] == "ip")
        {
            m_useRooms = false;
            m_address = saved[1];
            m_portText = saved[2];
            m_password = saved[3];
            token = saved[4];
        }
        else
        {
            return;
        }

        m_editor.services.notificationBar.PushNotification("REJOINING YOUR COLLAB SESSION", 3, true);
        StartJoining();
        if (m_session != null && m_session.role == DuetSession.Role.Client)
            m_session.rejoinToken = token;
    }

    bool useRooms { get { return m_useRooms && DuetRooms.isAvailable; } }

    void OnDestroy()
    {
        DuetRooms.quitting -= OnQuitting;
        Application.quitting -= OnQuitting;
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
        if (MSChartEditorInput.GetInputDown(MSChartEditorInputActions.DuetToggleWindow))
        {
            m_visible = !m_visible;
            if (!m_visible)
                isTyping = false;
        }

        if ((Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) && IsPointerOverPanel())
            isMouseHeldOnPanel = true;
        else if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1))
            isMouseHeldOnPanel = false;

        if (m_rehostPending && m_session == null && m_editor.currentState == ChartEditor.State.Editor)
            StartCoroutine(RehostAfterCrash());
        else if (m_rejoinPending && m_session == null && m_editor.currentState == ChartEditor.State.Editor)
            RejoinIfLeftOpen();

        if (m_session != null)
        {
            m_session.Update();

            if (m_session.role == DuetSession.Role.Host)
                RememberHosting();

            // Remember the session while in it. Ending it any way other than closing Moonscraper forgets it again.
            if (m_session.role == DuetSession.Role.Client)
            {
                if (m_session.isActive && m_session.state == DuetSession.State.Connected && !m_rejoinSaved)
                {
                    m_rejoinSaved = true;
                    PlayerPrefs.SetString(PrefRejoin, (m_sessionIsRoom ? "room\n" + m_joinedCode : "ip\n" + m_address + "\n" + m_session.port + "\n" + m_password)
                        + "\n" + (m_session.rejoinToken ?? string.Empty));
                    PlayerPrefs.Save();
                }
                else if (!m_session.isActive && m_rejoinSaved)
                {
                    m_rejoinSaved = false;
                    PlayerPrefs.DeleteKey(PrefRejoin);
                    PlayerPrefs.Save();
                }
            }

            // A joiner's copy of the chart goes away with the session, however it ended
            if (!m_session.isActive && m_session.role == DuetSession.Role.Client && m_session.hasReceivedSong && !m_unloadedAfterSession)
            {
                m_unloadedAfterSession = true;
                m_editor.UnloadCollaborationSong();
            }
        }
    }

    void OnGUI()
    {
        if (s_quitPromptOpen)
        {
            DrawQuitPrompt();
            return;
        }

        DrawHostLostNotice();

        if (!m_visible || isHudHidden)
        {
            if (isTyping)
            {
                isTyping = false;
                GUIUtility.keyboardControl = 0;
            }
            return;
        }

        float scale = GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        if (!m_windowPlaced)
        {
            m_window.rect.x = Screen.width / scale - m_window.rect.width - 20;
            m_window.rect.y = 60;
            m_windowPlaced = true;
        }

        // Clicking anywhere outside the window gives keyboard input back to the editor
        if (Event.current.type == EventType.MouseDown && !m_window.rect.Contains(Event.current.mousePosition) && !DuetChat.IsPointerOver())
            GUIUtility.keyboardControl = 0;

        // An open Moonscraper menu goes in front of the window, so the window steps out of its way
        if (DuetMenuCover.Covers(m_window.rect, scale))
            return;

        m_window.Draw("Collab Mode", DrawWindow, scale);
        isTyping = GUIUtility.keyboardControl != 0;
    }

    // Shown to joiners, whether or not the Duet window is open, while a host that crashed is away
    void DrawHostLostNotice()
    {
        if (m_session != null && m_session.role == DuetSession.Role.Client && m_session.isActive && m_session.hostLostSeconds > 0
            && m_session.state != DuetSession.State.Connected && !isHudHidden)
            DuetHostLostNotice.Draw(m_session.hostLostSeconds, GuiScale);
    }

    const int QuitPromptId = 0x4477;
    const float QuitPromptWidth = 400;
    const float QuitPromptHeight = 130;

    void DrawQuitPrompt()
    {
        isTyping = true;    // Keeps the editor's shortcuts quiet until it's answered

        float scale = GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        Rect rect = new Rect((Screen.width / scale - QuitPromptWidth) / 2, (Screen.height / scale - QuitPromptHeight) / 2, QuitPromptWidth, QuitPromptHeight);
        DuetInputBlocker.Block(QuitPromptId, new Rect(0, 0, Screen.width / scale, Screen.height / scale), scale);    // Nothing else can be clicked until it's answered
        GUISkin oldSkin = GUI.skin;
        GUI.skin = DuetSkin.skin;
        GUI.ModalWindow(QuitPromptId, rect, DrawQuitPromptWindow, "Quit Moonscraper?");
        GUI.skin = oldSkin;
    }

    void DrawQuitPromptWindow(int id)
    {
        bool inSession = activeSession != null;
        bool askToSave = !isGuest && ChartEditor.isDirty;

        // A plain modal window doesn't lay out its contents, so keep them clear of the title and edges
        GUILayout.BeginArea(new Rect(10, 24, QuitPromptWidth - 20, QuitPromptHeight - 34));

        if (inSession)
            GUILayout.Label(isGuest ? "You'll leave the Collab session." : "Everyone in your Collab session will be disconnected.");
        if (askToSave)
            GUILayout.Label("You have unsaved changes. Do you want to save them first?");

        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if (askToSave && GUILayout.Button("Save and quit"))
        {
            CloseQuitPrompt();
            if (m_editor._Save())
                StartCoroutine(QuitWhenSaved());
        }
        if (GUILayout.Button(askToSave ? "Quit without saving" : "Quit"))
        {
            CloseQuitPrompt();
            m_editor.QuitAfterDuetPrompt();
        }
        if (GUILayout.Button("Cancel"))
            CloseQuitPrompt();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void CloseQuitPrompt()
    {
        s_quitPromptOpen = false;
        isTyping = false;
    }

    System.Collections.IEnumerator QuitWhenSaved()
    {
        yield return null;
        while (m_editor.isSaving)
            yield return null;
        m_editor.QuitAfterDuetPrompt();
    }

    void DrawWindow(int id)
    {
        if (m_session == null || !m_session.isActive)
            DrawSetup();
        else
            DrawSession();
    }

    void DrawSetup()
    {
        if (m_session != null && !m_session.isActive)
            GUILayout.Label("Session ended: " + m_session.endReason);

        GUILayout.Label("Your name (shown to the other person)");
        m_username = GUILayout.TextField(m_username, 24);

        GUILayout.Space(8);
        DrawColorPicker();

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
            GUILayout.Label("Password (the host picks it, joiners type the same)");
            m_password = GUILayout.PasswordField(m_password, '*', 64);
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

        GUILayout.Label("The host sends the song's audio and other files when you join.");

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
                GUILayout.Label(m_session.hostLostSeconds > 0 ? "The host disconnected. Trying to reach them again..." : "Connecting...");
                break;
            case DuetSession.State.Joining:
                GUILayout.Label(m_session.hostLostSeconds > 0 ? "The host is back. Catching up..." : "Connected. Receiving the host's chart...");
                break;
            case DuetSession.State.HostLost:
                GUILayout.Label("The host disconnected. Don't leave: you'll be reconnected as soon as they're back.");
                break;
            default:
                GUILayout.Label(isHost ? (m_sessionIsRoom ? "Hosting an online room" : "Hosting on port " + m_session.port) : "Joined " + HostName() + "'s session");
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
                GUILayout.Label("Your buddy picks Room code in their Collab window, types this in and joins. Works from any network.");
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

        if (isHost)
        {
            bool locked = GUILayout.Toggle(m_session.isLocked, " Lock the session (nobody new can join)");
            if (locked != m_session.isLocked)
                m_session.isLocked = locked;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Room size: " + m_session.maxUsers, GUILayout.Width(130));
            float size = GUILayout.HorizontalSlider(m_session.maxUsers, 2, DuetRooms.MaxMembers);
            m_session.maxUsers = Mathf.RoundToInt(size);
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(8);
        string count = m_session.users.Count + "/" + m_session.maxUsers;
        GUILayout.Label("In this session (" + count + "):" + (m_session.isLocked ? "  (locked)" : string.Empty));
        int kick = -1;
        foreach (DuetUser user in m_session.users)
        {
            // In each person's own color, as on their cursor
            DuetPresence presence;
            Color content = GUI.contentColor;
            if (user.id == m_session.localUserId)
                GUI.contentColor = DuetPresence.GetColor(m_color);
            else if (m_session.presences.TryGetValue(user.id, out presence))
                GUI.contentColor = DuetPresence.GetColor(presence.color);

            GUILayout.BeginHorizontal();
            GUILayout.Label("    " + user.name + (user.id == 0 ? " (host)" : string.Empty));
            GUI.contentColor = content;
            if (isHost && user.id != 0 && GUILayout.Button("Kick", GUILayout.Width(50)))
                kick = user.id;
            GUILayout.EndHorizontal();
        }
        if (kick >= 0)
            m_session.Kick(kick);   // After the loop, since kicking changes the list

        GUILayout.Space(8);
        DrawColorPicker();

        DuetFileReceiver files = m_session.songFiles;
        if (!isHost && files != null && !files.isComplete)
        {
            GUILayout.Space(8);
            GUILayout.Label(string.Format("Getting the song's files: {0:0.0} of {1:0.0} MB", files.receivedBytes / 1048576.0, files.totalBytes / 1048576.0));
        }
        else if (!isHost && files != null && m_song.missingAudioFiles.Count > 0)
        {
            GUILayout.Space(8);
            GUILayout.Label("Couldn't get from the host: " + string.Join(", ", m_song.missingAudioFiles.ToArray()));
        }

        GUILayout.Space(8);
        foreach (string line in m_session.log)
            GUILayout.Label(line);

        GUILayout.Space(8);
        if (!isHost && m_session.state == DuetSession.State.Connected && GUILayout.Button("Get a fresh copy from the host"))
            m_session.RequestResync("Getting a fresh copy from the host.");

        if (!isHost)
            GUILayout.Label("Only the host can save the chart or change the audio. Leaving closes the chart.");

        if (isHost && m_session.isAwaitingRecoveryChart)
            GUILayout.Label("Back after a crash. When someone from the session comes back, their copy of the chart (with every edit) replaces this one.");

        bool waiting = !isHost && m_session.hostLostSeconds > 0 && m_session.state != DuetSession.State.Connected;
        if (GUILayout.Button(isHost ? "End session" : waiting ? "Stop waiting and leave" : "Leave session"))
            m_session.Leave();
    }

    string HostName()
    {
        DuetUser host = m_session.users.Find(u => u.id == 0);
        return host != null ? host.name : "the host";
    }

    void SetColor(int color, bool updateHex = true)
    {
        m_color = color;
        PlayerPrefs.SetInt(PrefColor, m_color);
        if (updateHex)
            m_hexInput = SliderHexOf(m_color);
    }

    // Where the custom color sliders start: a rainbow has no one color, so it starts them on white
    static Color SliderColorOf(int color)
    {
        return DuetPresence.IsRainbow(color) ? Color.white : DuetPresence.GetColor(color);
    }

    static string SliderHexOf(int color)
    {
        return DuetPresence.IsRainbow(color) ? "#FFFFFF" : HexOf(color);
    }

    static string HexOf(int color)
    {
        return "#" + ColorUtility.ToHtmlStringRGB(DuetPresence.GetColor(color));
    }

    static float ColorSlider(string label, float value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(50));
        float result = GUILayout.HorizontalSlider(value, 0f, 1f);
        GUILayout.EndHorizontal();
        return Mathf.Round(result * 255) / 255f;
    }

    // Your color for your name, cursor and song bar marker, and whether to show other people's ghost notes
    void DrawColorPicker()
    {
        GUILayout.Label("Your color: " + DuetPresence.GetColorName(m_color));
        GUILayout.BeginHorizontal();
        Color background = GUI.backgroundColor;
        for (int i = 0; i < DuetPresence.ColorCount; ++i)
        {
            GUI.backgroundColor = DuetPresence.GetColor(i);
            if (GUILayout.Toggle(i == m_color, i == m_color ? "*" : " ", DuetSkin.swatch, GUILayout.MinWidth(16), GUILayout.ExpandWidth(true)) && i != m_color)
                SetColor(i);
        }
        GUI.backgroundColor = background;
        GUILayout.EndHorizontal();

        // Any color at all, by sliders or by typing its hex code, or a rainbow
        bool custom = DuetPresence.IsCustomColor(m_color);
        bool rainbow = DuetPresence.IsRainbow(m_color);
        GUILayout.BeginHorizontal();
        // The button stays white until a slider or the hex code picks a custom color. The sliders start on the preset
        // you picked, so it can be fine tuned, or on white for a rainbow.
        bool customLook = custom || m_showCustomColor;
        GUI.backgroundColor = custom ? DuetPresence.GetColor(m_color) : customLook ? Color.white : background;
        bool showCustomColor = GUILayout.Toggle(m_showCustomColor, custom ? "* Custom color" : "Custom color", customLook ? DuetSkin.swatch : GUI.skin.button);
        if (showCustomColor && !m_showCustomColor)
            m_hexInput = SliderHexOf(m_color);
        m_showCustomColor = showCustomColor;
        GUI.backgroundColor = rainbow ? DuetPresence.GetColor(m_color) : background;
        if (GUILayout.Toggle(rainbow, rainbow ? "* Rainbow" : "Rainbow", rainbow ? DuetSkin.swatch : GUI.skin.button) && !rainbow)
            SetColor(DuetPresence.MakeRainbow(m_rainbowSpeed));
        GUI.backgroundColor = background;
        GUILayout.EndHorizontal();

        if (rainbow)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Speed " + DuetPresence.RainbowSpeed(m_color), GUILayout.Width(85));
            int speed = Mathf.RoundToInt(GUILayout.HorizontalSlider(DuetPresence.RainbowSpeed(m_color), 1, DuetPresence.MaxRainbowSpeed));
            GUILayout.EndHorizontal();
            if (speed != DuetPresence.RainbowSpeed(m_color))
            {
                m_rainbowSpeed = speed;
                PlayerPrefs.SetInt(PrefRainbowSpeed, speed);
                SetColor(DuetPresence.MakeRainbow(speed));
            }
        }

        if (m_showCustomColor)
        {
            Color current = SliderColorOf(m_color);
            float r = ColorSlider("Red", current.r);
            float g = ColorSlider("Green", current.g);
            float b = ColorSlider("Blue", current.b);
            // Compared at the sliders' own precision, so only a real slider move picks a custom color
            if (r != Mathf.Round(current.r * 255) / 255f || g != Mathf.Round(current.g * 255) / 255f || b != Mathf.Round(current.b * 255) / 255f)
                SetColor(DuetPresence.MakeCustomColor(new Color(r, g, b)));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Hex", GUILayout.Width(50));
            string hex = GUILayout.TextField(m_hexInput, 7);
            GUILayout.EndHorizontal();
            if (hex != m_hexInput)
            {
                m_hexInput = hex;
                Color typed;
                if (ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out typed) && hex.TrimStart('#').Length == 6)
                    SetColor(DuetPresence.MakeCustomColor(typed), false);
            }
        }

        bool showGhosts = GUILayout.Toggle(m_showGhosts, " Show other people's ghost notes");
        if (showGhosts != m_showGhosts)
        {
            m_showGhosts = showGhosts;
            PlayerPrefs.SetInt(PrefShowGhosts, m_showGhosts ? 1 : 0);
        }
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
        if (m_password.Length < MinPasswordLength)
        {
            m_error = "Direct IP needs a password of at least " + MinPasswordLength + " characters.";
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
            m_savedRehost = null;
            return;
        }

        try
        {
            m_session = DuetSession.Host(m_song, m_username, port, m_password);
            m_localAddresses = null;
            m_savedRehost = null;
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


        m_sessionIsRoom = useRooms;
        m_unloadedAfterSession = false;
        m_rejoinSaved = false;
        m_joinedCode = code;
        if (m_sessionIsRoom)
        {
            m_session = DuetSession.Join(m_song, m_username, DuetRooms.provider.JoinRoom(code), "Looking for room " + code, code);
            m_session.reconnect = () => DuetRooms.provider.JoinRoom(code);
        }
        else
        {
            PlayerPrefs.SetString(PrefAddress, m_address);
            m_session = DuetSession.Join(m_song, m_username, m_address, port, m_password);
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
