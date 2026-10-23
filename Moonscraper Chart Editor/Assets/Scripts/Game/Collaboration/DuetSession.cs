// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
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

    // The song's details (name, artist, charter and so on), field by field. See DuetSongInfo.
    Dictionary<string, string> ReadSongInfo();
    void ApplySongInfo(Dictionary<string, string> info);
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
///
/// Alongside edits, everyone sends their presence (cursor, ghost note, place in the song) and any change to the song's
/// details. Neither waits for the song to be free, and the newest one simply wins.
/// </summary>
public class DuetSession
{
    public const int ProtocolVersion = 1;
    public const int DefaultPort = 47820;
    const int HostUserId = 0;
    const int MaxLogLines = 8;
    const double PresenceIntervalSeconds = 0.05;
    const double SongInfoIntervalSeconds = 0.25;

    public enum Role { Host, Client }
    public enum State { Connecting, Joining, Connected, Ended }

    readonly IDuetSongHost m_song;
    readonly string m_username;

    public Role role { get; private set; }
    public State state { get; private set; }
    public string endReason { get; private set; }
    public int port { get; private set; }

    // The room code to share when hosting an online room, once the room exists
    public string roomCode { get { return m_listener != null ? m_listener.roomCode : null; } }
    public List<DuetUser> users { get; private set; }
    public List<string> log { get; private set; }

    // Everyone else's latest presence, by user id
    public Dictionary<int, DuetPresence> presences { get; private set; }

    // Our own user id: 0 when hosting, given by the host when joining
    public int localUserId { get; private set; }

    // Joiners only: true once the host's chart has arrived and replaced whatever was open
    public bool hasReceivedSong { get; private set; }

    // Edit numbering. On the host this is the last number given out; on a joiner, the last one received.
    public int lastSeq { get; private set; }

    // Hosting
    IDuetListener m_listener;
    readonly List<IDuetLink> m_clients = new List<IDuetLink>();
    readonly HashSet<IDuetLink> m_joinedClients = new HashSet<IDuetLink>();
    readonly Dictionary<IDuetLink, Dictionary<string, object>> m_heldMessages = new Dictionary<IDuetLink, Dictionary<string, object>>();
    int m_nextUserId = HostUserId + 1;

    // Joining
    IDuetLink m_host;
    IDuetConnector m_connector;
    Dictionary<string, object> m_heldHostMessage;
    int m_nextEditId = 1;
    readonly HashSet<int> m_unacknowledgedEdits = new HashSet<int>();
    bool m_awaitingSnapshot = false;

    // Presence and song details
    readonly System.Diagnostics.Stopwatch m_clock = System.Diagnostics.Stopwatch.StartNew();
    DuetPresence m_localPresence;
    string m_lastSentPresence;
    double m_lastPresenceTime = double.MinValue;
    Dictionary<string, string> m_knownSongInfo;  // As last sent or received; null until the song settles
    double m_lastSongInfoTime = double.MinValue;

    public int unacknowledgedEditCount { get { return m_unacknowledgedEdits.Count; } }
    public bool isActive { get { return state != State.Ended; } }

    DuetSession(IDuetSongHost song, string username, Role role)
    {
        m_song = song;
        m_username = string.IsNullOrEmpty(username) ? "Charter" : username;
        this.role = role;
        users = new List<DuetUser>();
        log = new List<string>();
        presences = new Dictionary<int, DuetPresence>();
    }

    // Hosts on a port that others connect to directly by address
    public static DuetSession Host(IDuetSongHost song, string username, int port)
    {
        DuetListener listener = new DuetListener(port);
        DuetSession session = Host(song, username, listener, "Hosting on port " + listener.port);
        session.port = listener.port;
        return session;
    }

    public static DuetSession Host(IDuetSongHost song, string username, IDuetListener listener, string logLine)
    {
        DuetSession session = new DuetSession(song, username, Role.Host);
        session.m_listener = listener;
        session.state = State.Connected;
        session.users.Add(new DuetUser { id = HostUserId, name = session.m_username });
        session.Log(logLine);
        return session;
    }

    public static DuetSession Join(IDuetSongHost song, string username, string address, int port)
    {
        DuetSession session = Join(song, username, new DuetTcpConnector(address, port), "Connecting to " + address + ":" + port);
        session.port = port;
        return session;
    }

    public static DuetSession Join(IDuetSongHost song, string username, IDuetConnector connector, string logLine)
    {
        DuetSession session = new DuetSession(song, username, Role.Client);
        session.m_connector = connector;
        session.state = State.Connecting;
        session.Log(logLine);
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

        if (state == State.Connected)
        {
            SendPresenceIfChanged();
            SendSongInfoIfChanged();
        }
    }

    // Call every frame with where the local user is. Sent when it changes, at most 20 times a second.
    public void SetLocalPresence(DuetPresence presence)
    {
        m_localPresence = presence;
        if (state == State.Connected)
            SendPresenceIfChanged();
    }

    void SendPresenceIfChanged()
    {
        if (m_localPresence == null || m_clock.Elapsed.TotalSeconds - m_lastPresenceTime < PresenceIntervalSeconds)
            return;

        Dictionary<string, object> message = m_localPresence.ToJson();
        string json = DuetJson.Write(message);
        if (json == m_lastSentPresence)
            return;

        m_lastSentPresence = json;
        m_lastPresenceTime = m_clock.Elapsed.TotalSeconds;

        if (role == Role.Host)
        {
            message["user"] = (double)HostUserId;
            foreach (IDuetLink client in m_joinedClients)
                client.Send(message);
        }
        else
        {
            m_host.Send(message);
        }
    }

    // Checks a few times a second whether the local user changed the song's details, and sends only the fields that changed
    void SendSongInfoIfChanged()
    {
        if (m_song.isBusy || m_awaitingSnapshot || m_clock.Elapsed.TotalSeconds - m_lastSongInfoTime < SongInfoIntervalSeconds)
            return;

        m_lastSongInfoTime = m_clock.Elapsed.TotalSeconds;
        Dictionary<string, string> current = m_song.ReadSongInfo();
        if (m_knownSongInfo == null)
        {
            m_knownSongInfo = current;
            return;
        }

        Dictionary<string, object> changed = new Dictionary<string, object>();
        foreach (KeyValuePair<string, string> field in current)
        {
            string known;
            if (!m_knownSongInfo.TryGetValue(field.Key, out known) || known != field.Value)
            {
                changed[field.Key] = field.Value;
                m_knownSongInfo[field.Key] = field.Value;
            }
        }

        if (changed.Count <= 0)
            return;

        Dictionary<string, object> message = Message("songinfo", "fields", changed);
        if (role == Role.Host)
        {
            foreach (IDuetLink client in m_joinedClients)
                client.Send(message);
        }
        else
        {
            m_host.Send(message);
        }
    }

    // Applies song details someone else changed
    void ApplySongInfo(Dictionary<string, object> message)
    {
        Dictionary<string, object> fields = DuetJson.GetObject(message, "fields");
        if (fields == null)
            return;

        Dictionary<string, string> info = new Dictionary<string, string>();
        foreach (KeyValuePair<string, object> field in fields)
        {
            if (field.Value is string)
                info[field.Key] = (string)field.Value;
        }

        if (info.Count <= 0)
            return;

        m_song.ApplySongInfo(info);

        // Read back what the song actually holds, so a value the song tidied up isn't sent straight back
        Dictionary<string, string> applied = m_song.ReadSongInfo();
        if (m_knownSongInfo == null)
        {
            m_knownSongInfo = applied;
        }
        else
        {
            foreach (string key in info.Keys)
            {
                string value;
                if (applied.TryGetValue(key, out value))
                    m_knownSongInfo[key] = value;
            }
        }
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
        if (m_listener.error != null)
        {
            End(m_listener.error);
            return;
        }

        IDuetLink accepted;
        while (m_listener.TryAccept(out accepted))
        {
            m_clients.Add(accepted);
        }

        for (int i = m_clients.Count - 1; i >= 0; --i)
        {
            IDuetLink client = m_clients[i];

            ProcessHostMessages(client);

            if (client.isClosed)
            {
                m_clients.RemoveAt(i);
                m_heldMessages.Remove(client);

                if (m_joinedClients.Remove(client))
                {
                    DuetUser user = users.Find(u => u.id == client.id);
                    users.Remove(user);
                    presences.Remove(client.id);
                    Log((user != null ? user.name : "Someone") + " left (" + client.closeReason + ")");
                    BroadcastUsers();
                }
            }
        }
    }

    void ProcessHostMessages(IDuetLink client)
    {
        while (true)
        {
            Dictionary<string, object> message;
            if (m_heldMessages.TryGetValue(client, out message))
                m_heldMessages.Remove(client);
            else if (!client.TryReceive(out message))
                return;

            // Cursors keep moving even while the song is busy
            if (DuetJson.GetString(message, "type") == "presence")
            {
                if (m_joinedClients.Contains(client))
                    RelayPresence(client, message);
                continue;
            }

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

    void HandleHostMessage(IDuetLink client, Dictionary<string, object> message)
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

            // The snapshot carries most song details, but not the custom ini settings, so send them all
            Dictionary<string, object> songInfo = new Dictionary<string, object>();
            foreach (KeyValuePair<string, string> field in m_song.ReadSongInfo())
                songInfo[field.Key] = field.Value;
            client.Send(Message("songinfo", "fields", songInfo));

            // Show the newcomer where everyone already is
            if (m_lastSentPresence != null)
            {
                Dictionary<string, object> hostPresence = m_localPresence.ToJson();
                hostPresence["user"] = (double)HostUserId;
                client.Send(hostPresence);
            }
            foreach (KeyValuePair<int, DuetPresence> pair in presences)
            {
                Dictionary<string, object> other = pair.Value.ToJson();
                other["user"] = (double)pair.Key;
                client.Send(other);
            }

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

            case "songinfo":
                ApplySongInfo(message);
                foreach (IDuetLink other in m_joinedClients)
                {
                    if (other != client)
                        other.Send(message);
                }
                break;
        }
    }

    void RelayPresence(IDuetLink client, Dictionary<string, object> message)
    {
        presences[client.id] = DuetPresence.FromJson(message);

        message["user"] = (double)client.id;    // Whatever the sender claimed, it's this user
        foreach (IDuetLink other in m_joinedClients)
        {
            if (other != client)
                other.Send(message);
        }
    }

    string UniqueName(string name)
    {
        string unique = name;
        for (int n = 2; users.Exists(u => u.name == unique); ++n)
            unique = name + " " + n;
        return unique;
    }

    void BroadcastApply(int seq, int authorId, IList<SongEditChange> changes, IDuetLink except)
    {
        Dictionary<string, object> message = Message("apply",
            "seq", (double)seq,
            "author", (double)authorId,
            "changes", DuetSerialization.ToJson(changes));

        foreach (IDuetLink client in m_joinedClients)
        {
            if (client != except)
                client.Send(message);
        }
    }

    void BroadcastUsers()
    {
        Dictionary<string, object> message = Message("users", "users", UsersJson());
        foreach (IDuetLink client in m_joinedClients)
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
            IDuetLink link;
            string error;
            if (!m_connector.TryFinish(out link, out error))
                return;

            if (link == null)
            {
                End("Couldn't connect: " + error);
                return;
            }

            m_host = link;
            m_connector = null;
            state = State.Joining;
            m_host.Send(Message("hello", "protocol", (double)ProtocolVersion, "name", m_username));
            Log("Connected, waiting for the host's chart");
        }

        while (state != State.Ended)
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

            // Cursors keep moving even while the song is busy
            if (DuetJson.GetString(message, "type") == "presence")
            {
                int userId = (int)DuetJson.GetNumber(message, "user", -1);
                if (userId >= 0 && userId != localUserId && users.Exists(u => u.id == userId))
                    presences[userId] = DuetPresence.FromJson(message);
                continue;
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
                localUserId = (int)DuetJson.GetNumber(message, "you");
                LoadSnapshot(message, false);
                hasReceivedSong = true;
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

            case "songinfo":
                ApplySongInfo(message);
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
        m_knownSongInfo = null;     // Starts again from the loaded song once it settles
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

        List<int> gone = new List<int>();
        foreach (int id in presences.Keys)
        {
            if (!users.Exists(u => u.id == id))
                gone.Add(id);
        }
        foreach (int id in gone)
            presences.Remove(id);
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

        foreach (IDuetLink client in m_clients)
            client.Close("The host ended the session");
        m_clients.Clear();
        m_joinedClients.Clear();

        if (m_host != null)
            m_host.Close(reason);
        else if (m_connector != null)
            m_connector.Cancel();
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
