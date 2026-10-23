// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using MoonscraperChartEditor.Song;

/// <summary>
/// A copy of the song as sent to someone joining: the chart in Moonscraper's own lossless format,
/// and the file name (without folder) of each audio stem.
/// </summary>
public class DuetSongSnapshot
{
    public string chartText;
    public Dictionary<Song.AudioInstrument, string> audioFileNames = new Dictionary<Song.AudioInstrument, string>();
}

/// <summary>
/// What a session needs from the song it's sharing. Implemented against the editor by DuetEditorSong.
/// </summary>
public interface IDuetSongHost
{
    // True while the local user is mid-edit (dragging, holding keys) or a song is loading.
    // Incoming messages wait until this is false, so they never land in the middle of a gesture.
    bool isBusy { get; }

    // Applies someone else's edit only if the song is exactly as the edit expects. All or nothing.
    bool TryApplyRemoteEdit(IList<SongEditChange> changes, out string failure);

    DuetSongSnapshot TakeSnapshot();
    void LoadSnapshot(DuetSongSnapshot snapshot, bool isResync);
}

public class DuetUser
{
    public int id;
    public string name;
}

/// <summary>
/// A Duet Mode session, either hosting or joined.
///
/// The host's copy of the song is the referee. Every edit gets a number from the host and everyone applies edits
/// in that order. Your own edits apply on your screen straight away and are sent to the host, which checks the song
/// is still as your edit expected before accepting it. If an edit clashes, the joiner's copy is replaced with a fresh
/// copy from the host.
/// </summary>
public class DuetSession
{
    public const int ProtocolVersion = 1;
    public const int DefaultPort = 47820;
    const int HostUserId = 0;
    const int MaxLogLines = 8;

    public enum Role { Host, Client }
    public enum State { Connecting, Joining, Connected, Ended }

    readonly IDuetSongHost m_song;
    readonly string m_username;

    public Role role { get; private set; }
    public State state { get; private set; }
    public string endReason { get; private set; }
    public int port { get; private set; }
    public List<DuetUser> users { get; private set; }
    public List<string> log { get; private set; }

    // Edit numbering. On the host this is the last number given out; on a joiner, the last one received.
    public int lastSeq { get; private set; }

    // Hosting
    DuetListener m_listener;
    readonly List<DuetConnection> m_clients = new List<DuetConnection>();
    readonly HashSet<DuetConnection> m_joinedClients = new HashSet<DuetConnection>();
    readonly Dictionary<DuetConnection, Dictionary<string, object>> m_heldMessages = new Dictionary<DuetConnection, Dictionary<string, object>>();
    int m_nextUserId = HostUserId + 1;

    // Joining
    DuetConnection m_host;
    Thread m_connectThread;
    volatile DuetConnection m_connectResult;
    volatile string m_connectError;
    Dictionary<string, object> m_heldHostMessage;
    int m_nextEditId = 1;
    readonly HashSet<int> m_unacknowledgedEdits = new HashSet<int>();
    bool m_awaitingSnapshot = false;

    public int unacknowledgedEditCount { get { return m_unacknowledgedEdits.Count; } }
    public bool isActive { get { return state != State.Ended; } }

    DuetSession(IDuetSongHost song, string username, Role role)
    {
        m_song = song;
        m_username = string.IsNullOrEmpty(username) ? "Charter" : username;
        this.role = role;
        users = new List<DuetUser>();
        log = new List<string>();
    }

    public static DuetSession Host(IDuetSongHost song, string username, int port)
    {
        DuetSession session = new DuetSession(song, username, Role.Host);
        session.m_listener = new DuetListener(port);
        session.port = session.m_listener.port;
        session.state = State.Connected;
        session.users.Add(new DuetUser { id = HostUserId, name = session.m_username });
        session.Log("Hosting on port " + session.port);
        return session;
    }

    public static DuetSession Join(IDuetSongHost song, string username, string address, int port)
    {
        DuetSession session = new DuetSession(song, username, Role.Client);
        session.port = port;
        session.state = State.Connecting;
        session.Log("Connecting to " + address + ":" + port);

        session.m_connectThread = new Thread(() =>
        {
            try
            {
                session.m_connectResult = DuetConnection.Connect(address, port, 8000);
            }
            catch (Exception e)
            {
                session.m_connectError = e.Message;
            }
        }) { IsBackground = true, Name = "Duet connect" };
        session.m_connectThread.Start();

        return session;
    }

    public void Leave()
    {
        End("You left the session");
    }

    // Call every frame from the main thread
    public void Update()
    {
        if (state == State.Ended)
            return;

        if (role == Role.Host)
            UpdateHost();
        else
            UpdateClient();
    }

    // A finished edit made by the local user
    public void OnLocalEdit(IList<SongEditChange> changes)
    {
        if (state != State.Connected || changes.Count <= 0)
            return;

        if (role == Role.Host)
        {
            ++lastSeq;
            BroadcastApply(lastSeq, HostUserId, changes, null);
        }
        else
        {
            if (m_awaitingSnapshot)
                return;     // About to be replaced by the host's copy anyway

            int editId = m_nextEditId++;
            m_unacknowledgedEdits.Add(editId);
            m_host.Send(Message("edit",
                "id", (double)editId,
                "basedOn", (double)lastSeq,
                "changes", DuetSerialization.ToJson(changes)));
        }
    }

    // Replace our copy of the song with the host's. Joiners only.
    public void RequestResync(string reason)
    {
        if (role != Role.Client || state != State.Connected || m_awaitingSnapshot)
            return;

        Log(reason);
        m_awaitingSnapshot = true;
        m_unacknowledgedEdits.Clear();
        m_host.Send(Message("resync"));
    }

    #region Host

    void UpdateHost()
    {
        DuetConnection accepted;
        while (m_listener.TryAccept(out accepted))
        {
            m_clients.Add(accepted);
        }

        for (int i = m_clients.Count - 1; i >= 0; --i)
        {
            DuetConnection client = m_clients[i];

            ProcessHostMessages(client);

            if (client.isClosed)
            {
                m_clients.RemoveAt(i);
                m_heldMessages.Remove(client);

                if (m_joinedClients.Remove(client))
                {
                    DuetUser user = users.Find(u => u.id == client.id);
                    users.Remove(user);
                    Log((user != null ? user.name : "Someone") + " left (" + client.closeReason + ")");
                    BroadcastUsers();
                }
            }
        }
    }

    void ProcessHostMessages(DuetConnection client)
    {
        while (!m_song.isBusy)
        {
            Dictionary<string, object> message;
            if (m_heldMessages.TryGetValue(client, out message))
                m_heldMessages.Remove(client);
            else if (!client.TryReceive(out message))
                return;

            // A message that needs the song, such as an edit or a join, might arrive while the song is busy.
            // Hold it and handle it first next time.
            if (m_song.isBusy)
            {
                m_heldMessages[client] = message;
                return;
            }

            try
            {
                HandleHostMessage(client, message);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("Duet: bad message from " + client.remoteAddress + ": " + e);
                client.Close("Sent a message this version doesn't understand");
            }
        }
    }

    void HandleHostMessage(DuetConnection client, Dictionary<string, object> message)
    {
        string type = DuetJson.GetString(message, "type");
        bool joined = m_joinedClients.Contains(client);

        if (type == "hello" && !joined)
        {
            int protocol = (int)DuetJson.GetNumber(message, "protocol");
            if (protocol != ProtocolVersion)
            {
                client.Send(Message("rejected", "reason", string.Format("Version mismatch: the host has Duet Mode version {0} and you have version {1}. You both need the same build.", ProtocolVersion, protocol)));
                client.CloseAfterSending("Version mismatch");
                return;
            }

            client.id = m_nextUserId++;
            DuetUser user = new DuetUser { id = client.id, name = UniqueName(DuetJson.GetString(message, "name", "Charter")) };
            users.Add(user);
            m_joinedClients.Add(client);

            Dictionary<string, object> welcome = SnapshotMessage("welcome", m_song.TakeSnapshot());
            welcome["you"] = (double)client.id;
            welcome["users"] = UsersJson();
            client.Send(welcome);

            Log(user.name + " joined from " + client.remoteAddress);
            BroadcastUsers();
            return;
        }

        if (!joined)
            return;

        switch (type)
        {
            case "edit":
                {
                    double editId = DuetJson.GetNumber(message, "id");
                    List<SongEditChange> changes = DuetSerialization.ChangesFromJson(DuetJson.GetArray(message, "changes"));

                    string failure;
                    if (m_song.TryApplyRemoteEdit(changes, out failure))
                    {
                        ++lastSeq;
                        client.Send(Message("ack", "id", editId, "seq", (double)lastSeq));
                        BroadcastApply(lastSeq, client.id, changes, client);
                    }
                    else
                    {
                        client.Send(Message("nack", "id", editId, "reason", failure));
                    }
                    break;
                }

            case "resync":
                client.Send(SnapshotMessage("snapshot", m_song.TakeSnapshot()));
                break;
        }
    }

    string UniqueName(string name)
    {
        string unique = name;
        for (int n = 2; users.Exists(u => u.name == unique); ++n)
            unique = name + " " + n;
        return unique;
    }

    void BroadcastApply(int seq, int authorId, IList<SongEditChange> changes, DuetConnection except)
    {
        Dictionary<string, object> message = Message("apply",
            "seq", (double)seq,
            "author", (double)authorId,
            "changes", DuetSerialization.ToJson(changes));

        foreach (DuetConnection client in m_joinedClients)
        {
            if (client != except)
                client.Send(message);
        }
    }

    void BroadcastUsers()
    {
        Dictionary<string, object> message = Message("users", "users", UsersJson());
        foreach (DuetConnection client in m_joinedClients)
            client.Send(message);
    }

    Dictionary<string, object> SnapshotMessage(string type, DuetSongSnapshot snapshot)
    {
        Dictionary<string, object> audio = new Dictionary<string, object>();
        foreach (KeyValuePair<Song.AudioInstrument, string> pair in snapshot.audioFileNames)
            audio[pair.Key.ToString()] = pair.Value;

        return Message(type,
            "seq", (double)lastSeq,
            "chart", snapshot.chartText,
            "audio", audio);
    }

    List<object> UsersJson()
    {
        List<object> list = new List<object>();
        foreach (DuetUser user in users)
            list.Add(Message("user", "id", (double)user.id, "name", user.name));
        return list;
    }

    #endregion

    #region Client

    void UpdateClient()
    {
        if (state == State.Connecting)
        {
            if (m_connectError != null)
            {
                End("Couldn't connect: " + m_connectError);
                return;
            }

            if (m_connectResult == null)
                return;

            m_host = m_connectResult;
            state = State.Joining;
            m_host.Send(Message("hello", "protocol", (double)ProtocolVersion, "name", m_username));
            Log("Connected, waiting for the host's chart");
        }

        while (!m_song.isBusy && state != State.Ended)
        {
            Dictionary<string, object> message;
            if (m_heldHostMessage != null)
            {
                message = m_heldHostMessage;
                m_heldHostMessage = null;
            }
            else if (!m_host.TryReceive(out message))
            {
                break;
            }

            if (m_song.isBusy)
            {
                m_heldHostMessage = message;
                break;
            }

            try
            {
                HandleClientMessage(message);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("Duet: bad message from host: " + e);
                RequestResync("Got a message from the host this version doesn't understand. Getting a fresh copy.");
            }
        }

        if (state != State.Ended && m_host.isClosed && m_heldHostMessage == null)
        {
            End("Disconnected from host: " + m_host.closeReason);
        }
    }

    void HandleClientMessage(Dictionary<string, object> message)
    {
        switch (DuetJson.GetString(message, "type"))
        {
            case "rejected":
                End(DuetJson.GetString(message, "reason", "The host refused the connection"));
                break;

            case "welcome":
                LoadSnapshot(message, false);
                ReadUsers(message);
                state = State.Connected;
                Log("Joined. You now have the host's chart.");
                break;

            case "users":
                ReadUsers(message);
                break;

            case "ack":
                m_unacknowledgedEdits.Remove((int)DuetJson.GetNumber(message, "id"));
                if (!m_awaitingSnapshot)
                    lastSeq = (int)DuetJson.GetNumber(message, "seq");
                break;

            case "nack":
                RequestResync("The host couldn't apply one of your edits because it clashed with another edit. Getting a fresh copy.");
                break;

            case "apply":
                {
                    if (m_awaitingSnapshot)
                        break;  // Already covered by the snapshot on its way

                    List<SongEditChange> changes = DuetSerialization.ChangesFromJson(DuetJson.GetArray(message, "changes"));
                    string failure;
                    if (m_song.TryApplyRemoteEdit(changes, out failure))
                    {
                        lastSeq = (int)DuetJson.GetNumber(message, "seq");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning("Duet: " + failure);
                        RequestResync("An edit from " + UserName((int)DuetJson.GetNumber(message, "author")) + " clashed with yours. Getting a fresh copy.");
                    }
                    break;
                }

            case "snapshot":
                LoadSnapshot(message, true);
                m_awaitingSnapshot = false;
                m_unacknowledgedEdits.Clear();
                Log("Back in sync with the host.");
                break;
        }
    }

    void LoadSnapshot(Dictionary<string, object> message, bool isResync)
    {
        DuetSongSnapshot snapshot = new DuetSongSnapshot();
        snapshot.chartText = DuetJson.GetString(message, "chart", string.Empty);

        Dictionary<string, object> audio = DuetJson.GetObject(message, "audio");
        if (audio != null)
        {
            foreach (KeyValuePair<string, object> pair in audio)
            {
                Song.AudioInstrument instrument;
                if (pair.Value is string && Enum.TryParse(pair.Key, out instrument))
                    snapshot.audioFileNames[instrument] = (string)pair.Value;
            }
        }

        lastSeq = (int)DuetJson.GetNumber(message, "seq");
        m_song.LoadSnapshot(snapshot, isResync);
    }

    void ReadUsers(Dictionary<string, object> message)
    {
        users = new List<DuetUser>();
        foreach (object item in DuetJson.GetArray(message, "users"))
        {
            Dictionary<string, object> user = item as Dictionary<string, object>;
            if (user != null)
                users.Add(new DuetUser { id = (int)DuetJson.GetNumber(user, "id"), name = DuetJson.GetString(user, "name", "Charter") });
        }
    }

    #endregion

    string UserName(int id)
    {
        DuetUser user = users.Find(u => u.id == id);
        return user != null ? user.name : "someone";
    }

    void End(string reason)
    {
        if (state == State.Ended)
            return;

        state = State.Ended;
        endReason = reason;
        Log(reason);

        if (m_listener != null)
            m_listener.Stop();

        foreach (DuetConnection client in m_clients)
            client.Close("The host ended the session");
        m_clients.Clear();
        m_joinedClients.Clear();

        if (m_host != null)
            m_host.Close(reason);
        else if (m_connectResult != null)
            m_connectResult.Close(reason);
    }

    void Log(string line)
    {
        log.Add(line);
        if (log.Count > MaxLogLines)
            log.RemoveAt(0);
        UnityEngine.Debug.Log("Duet: " + line);
    }

    static Dictionary<string, object> Message(string type, params object[] keyValues)
    {
        Dictionary<string, object> message = new Dictionary<string, object>();
        message["type"] = type;
        for (int i = 0; i + 1 < keyValues.Length; i += 2)
            message[(string)keyValues[i]] = keyValues[i + 1];
        return message;
    }
}
