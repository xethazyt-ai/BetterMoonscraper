// See LICENSE in project root for license information.

// Online rooms for Duet Mode over Epic Online Services. The host makes a lobby whose ID is a short room code and
// joiners find it by that code. Messages then go straight between the two PCs over EOS P2P, which falls back to
// Epic's relay servers when a direct connection isn't possible. Everyone signs in anonymously with a device ID.
//
// Only compiled when DUET_EOS is defined (Assets/csc.rsp), because the EOS SDK isn't part of the repository.
// Setup is in DuetTests/EOS-SETUP.md.

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
using Epic.OnlineServices.P2P;
using Epic.OnlineServices.Platform;

public class DuetEos : MonoBehaviour, IDuetRoomProvider
{
    const string LibraryName = "EOSSDK-Win64-Shipping.dll";
    const string SocketName = "DuetMode";
    const string LobbyIdPrefix = "duet";
    const string BucketId = "duet";
    const int RoomCodeLength = 6;
    const string RoomCodeLetters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";    // No 0/O or 1/I/L, so codes are easy to read out
    const int MaxPacketsPerFrame = 4096;
    const int ShutdownGraceMs = 400;

    enum Phase { Idle, SigningIn, Ready, Failed }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Register()
    {
        GameObject go = new GameObject("Duet EOS");
        DontDestroyOnLoad(go);
        DuetRooms.provider = go.AddComponent<DuetEos>();
    }

    // The EOS folder sits beside Assets in the editor, and beside the .exe in a build
    static string EosFolder { get { return Path.GetFullPath(Path.Combine(Application.dataPath, Path.Combine("..", "EOS"))); } }

    Phase m_phase = Phase.Idle;
    string m_failure;
    IntPtr m_library = IntPtr.Zero;
    bool m_sdkInitialized = false;
    PlatformInterface m_platform;
    ProductUserId m_localUser;
    SocketId m_socket;
    ulong m_requestNotification = 0;
    ulong m_closedNotification = 0;
    ulong m_memberNotification = 0;
    ulong m_authNotification = 0;

    // Epic's sign-in only lasts about an hour. It warns before then, and signing in again with the same device ID
    // keeps the same user, rooms and connections. Without this, sending fails with InvalidAuth after an hour.
    bool m_reloginRunning = false;
    float m_reloginAt = -1;
    const float ReloginRetrySeconds = 15;

    readonly Dictionary<string, Link> m_links = new Dictionary<string, Link>();
    Room m_room;
    readonly byte[] m_receiveBuffer = new byte[P2PInterface.MAX_PACKET_SIZE];
    readonly List<Dictionary<string, object>> m_receivedMessages = new List<Dictionary<string, object>>();

    // Hosting: people who asked to connect before this computer saw them join the room. They're let in once they
    // show up as a member, and not before, so only people in the room can reach the session at all.
    readonly HashSet<string> m_waitingPeers = new HashSet<string>();
    const int MaxWaitingPeers = 32;

    // EOS calls back from inside Tick, and DuetKeepAlive ticks from a background thread while a Windows pop-up is
    // open. Callbacks wait here and run on the main thread once it's free, so nothing in Duet runs on two threads.
    readonly Queue<Action> m_deferred = new Queue<Action>();
    int m_mainThreadId;

    public string status
    {
        get
        {
            switch (m_phase)
            {
                case Phase.SigningIn: return "Signing in to Epic Online Services...";
                case Phase.Ready: return "Online rooms ready";
                case Phase.Failed: return "Online rooms unavailable: " + m_failure;
                default: return "Online rooms ready";
            }
        }
    }

    public IDuetListener CreateRoom(string code = null)
    {
        if (m_room != null)
            m_room.Stop();

        StartIfNeeded();
        m_waitingPeers.Clear();
        m_room = new Room(this, code == null ? null : DuetRooms.NormalizeCode(code));
        return m_room;
    }

    public IDuetConnector JoinRoom(string roomCode)
    {
        StartIfNeeded();
        return new Join(this, DuetRooms.NormalizeCode(roomCode));
    }

    #region Start and stop

    void StartIfNeeded()
    {
        if (m_phase == Phase.Failed)
        {
            // Try again from scratch, for example after fixing credentials.txt
            Shutdown();
            m_phase = Phase.Idle;
        }

        if (m_phase != Phase.Idle)
            return;

        m_phase = Phase.SigningIn;
        string error = StartPlatform();
        if (error != null)
        {
            Fail(error);
            return;
        }

        CreateDeviceIdOptions deviceOptions = new CreateDeviceIdOptions { DeviceModel = SystemInfo.deviceModel };
        m_platform.GetConnectInterface().CreateDeviceId(ref deviceOptions, null, (ref CreateDeviceIdCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            OnMainThread(() => OnDeviceIdCreated(result));
        });
    }

    string StartPlatform()
    {
        Dictionary<string, string> credentials;
        string error;
        if (!TryReadCredentials(out credentials, out error))
            return error;

        // EOS can only start once each time its DLL is loaded, and the editor never unloads native plugins,
        // so load the DLL by hand and free it on shutdown. Assets/csc.rsp defines EOS_DYNAMIC_BINDINGS so builds do the same.
        string libraryPath = Path.Combine(EosFolder, LibraryName);
        m_library = LoadLibrary(libraryPath);
        if (m_library == IntPtr.Zero)
            return "couldn't load " + libraryPath + " (Windows error " + Marshal.GetLastWin32Error() + ")";
        Bindings.Hook(m_library, GetProcAddress);

        InitializeOptions initializeOptions = new InitializeOptions { ProductName = "Moonscraper Duet", ProductVersion = DuetSession.ProtocolVersion.ToString() };
        Result result = PlatformInterface.Initialize(ref initializeOptions);
        if (result != Result.Success && result != Result.AlreadyConfigured)
            return "the SDK didn't start (" + result + ")";
        m_sdkInitialized = true;

        LoggingInterface.SetLogLevel(LogCategory.AllCategories, LogLevel.Warning);
        LoggingInterface.SetCallback((ref LogMessage message) =>
        {
            try { Debug.LogWarning("EOS [" + message.Category + "] " + message.Message); }
            catch (Exception) { }   // Never let an error escape into EOS's own code
        });

        string cacheDirectory = Path.Combine(Application.temporaryCachePath, "EOS");
        Directory.CreateDirectory(cacheDirectory);

        Options options = new Options
        {
            ProductId = credentials["ProductId"],
            SandboxId = credentials["SandboxId"],
            DeploymentId = credentials["DeploymentId"],
            ClientCredentials = new ClientCredentials { ClientId = credentials["ClientId"], ClientSecret = credentials["ClientSecret"] },
            Flags = Application.isEditor ? PlatformFlags.LoadingInEditor : PlatformFlags.DisableOverlay,
            CacheDirectory = cacheDirectory,
        };

        m_platform = PlatformInterface.Create(ref options);
        if (m_platform == null)
            return "the platform couldn't be created. Check the values in " + Path.Combine(EosFolder, "credentials.txt");

        DuetKeepAlive.tick = TickPlatform;

        return null;
    }

    void OnDeviceIdCreated(Result result)
    {
        if (m_platform == null)
            return;

        // DuplicateNotAllowed means this PC already has a device ID, which is fine
        if (result != Result.Success && result != Result.DuplicateNotAllowed)
        {
            Fail("couldn't create a device ID (" + result + ")");
            return;
        }

        LoginOptions loginOptions = new LoginOptions
        {
            Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken, Token = null },
            UserLoginInfo = new UserLoginInfo { DisplayName = "Moonscraper Duet" },
        };
        m_platform.GetConnectInterface().Login(ref loginOptions, null, (ref LoginCallbackInfo info) =>
        {
            Result loginResult = info.ResultCode;
            ProductUserId user = info.LocalUserId;
            OnMainThread(() => OnLoggedIn(loginResult, user));
        });
    }

    void OnLoggedIn(Result result, ProductUserId user)
    {
        if (m_platform == null)
            return;

        if (result != Result.Success || user == null)
        {
            Fail("couldn't sign in (" + result + ")");
            return;
        }

        m_localUser = user;
        m_socket = new SocketId { SocketName = SocketName };

        P2PInterface p2p = m_platform.GetP2PInterface();
        SetPacketQueueSizeOptions queueOptions = new SetPacketQueueSizeOptions
        {
            IncomingPacketQueueMaxSizeBytes = P2PInterface.MAX_QUEUE_SIZE_UNLIMITED,
            OutgoingPacketQueueMaxSizeBytes = P2PInterface.MAX_QUEUE_SIZE_UNLIMITED,
        };
        p2p.SetPacketQueueSize(ref queueOptions);

        AddNotifyPeerConnectionRequestOptions requestOptions = new AddNotifyPeerConnectionRequestOptions { LocalUserId = m_localUser, SocketId = m_socket };
        m_requestNotification = p2p.AddNotifyPeerConnectionRequest(ref requestOptions, null, OnConnectionRequest);

        AddNotifyPeerConnectionClosedOptions closedOptions = new AddNotifyPeerConnectionClosedOptions { LocalUserId = m_localUser, SocketId = m_socket };
        m_closedNotification = p2p.AddNotifyPeerConnectionClosed(ref closedOptions, null, OnConnectionClosed);

        AddNotifyLobbyMemberStatusReceivedOptions memberOptions = new AddNotifyLobbyMemberStatusReceivedOptions();
        m_memberNotification = m_platform.GetLobbyInterface().AddNotifyLobbyMemberStatusReceived(ref memberOptions, null, OnLobbyMemberStatus);

        AddNotifyAuthExpirationOptions expirationOptions = new AddNotifyAuthExpirationOptions();
        m_authNotification = m_platform.GetConnectInterface().AddNotifyAuthExpiration(ref expirationOptions, null, (ref AuthExpirationCallbackInfo info) =>
        {
            OnMainThread(() =>
            {
                Debug.Log("Duet EOS: sign-in is about to expire, signing in again");
                RequestRelogin();
            });
        });

        m_phase = Phase.Ready;
        Debug.Log("Duet EOS: signed in as " + m_localUser);
    }

    void RequestRelogin()
    {
        if (m_reloginAt < 0 && !m_reloginRunning)
            m_reloginAt = 0;
    }

    void UpdateRelogin()
    {
        if (m_reloginRunning || m_reloginAt < 0 || Time.realtimeSinceStartup < m_reloginAt)
            return;

        m_reloginRunning = true;
        m_reloginAt = -1;
        LoginOptions loginOptions = new LoginOptions
        {
            Credentials = new Credentials { Type = ExternalCredentialType.DeviceidAccessToken, Token = null },
            UserLoginInfo = new UserLoginInfo { DisplayName = "Moonscraper Duet" },
        };
        m_platform.GetConnectInterface().Login(ref loginOptions, null, (ref LoginCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            OnMainThread(() =>
            {
                m_reloginRunning = false;
                if (result == Result.Success)
                    Debug.Log("Duet EOS: signed in again");
                else
                {
                    Debug.LogWarning("Duet EOS: couldn't sign in again (" + result + "), trying again soon");
                    m_reloginAt = Time.realtimeSinceStartup + ReloginRetrySeconds;
                }
            });
        });
    }

    void Fail(string reason)
    {
        Debug.LogWarning("Duet EOS: " + reason);
        m_failure = reason;
        m_phase = Phase.Failed;
    }

    void Shutdown()
    {
        if (m_room != null)
        {
            m_room.Stop();
            m_room = null;
        }

        // Give EOS a moment to deliver what's queued, such as a goodbye, before the connections close, so everyone
        // sees the session end straight away instead of timing out
        if (m_platform != null && m_phase == Phase.Ready)
        {
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ShutdownGraceMs)
            {
                foreach (Link link in new List<Link>(m_links.Values))
                    link.Flush();
                TickPlatform();
                RunDeferred();
                System.Threading.Thread.Sleep(10);
            }
        }

        foreach (Link link in new List<Link>(m_links.Values))
            link.Close("Online rooms shut down");
        m_links.Clear();
        m_waitingPeers.Clear();

        if (m_platform != null && m_phase == Phase.Ready)
        {
            for (int i = 0; i < 5; ++i)
            {
                TickPlatform();
                System.Threading.Thread.Sleep(10);
            }
        }

        DuetKeepAlive.tick = null;

        if (m_platform != null)
        {
            if (m_requestNotification != 0)
                m_platform.GetP2PInterface().RemoveNotifyPeerConnectionRequest(m_requestNotification);
            if (m_closedNotification != 0)
                m_platform.GetP2PInterface().RemoveNotifyPeerConnectionClosed(m_closedNotification);
            if (m_memberNotification != 0)
                m_platform.GetLobbyInterface().RemoveNotifyLobbyMemberStatusReceived(m_memberNotification);
            if (m_authNotification != 0)
                m_platform.GetConnectInterface().RemoveNotifyAuthExpiration(m_authNotification);
            m_requestNotification = m_closedNotification = m_memberNotification = m_authNotification = 0;
            m_reloginRunning = false;
            m_reloginAt = -1;

            m_platform.Release();
            m_platform = null;
        }

        if (m_sdkInitialized)
        {
            PlatformInterface.Shutdown();
            m_sdkInitialized = false;
        }

        if (m_library != IntPtr.Zero)
        {
            Bindings.Unhook();
            FreeLibrary(m_library);
            m_library = IntPtr.Zero;
        }

        m_localUser = null;
        lock (m_deferred)
            m_deferred.Clear();
    }

    // Not OnApplicationQuit: Unity sends that as soon as the window's X is clicked, before Moonscraper has asked whether
    // to quit, so cancelling would still close the room. Application.quitting only comes once it's really quitting.
    void Awake()
    {
        m_mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        Application.quitting += OnQuitting;
    }

    void OnDestroy()
    {
        Application.quitting -= OnQuitting;
    }

    // Sessions end first and say goodbye, which goes out during Shutdown's grace period
    void OnQuitting()
    {
        DuetRooms.NotifyQuitting();
        if (m_platform != null && m_phase == Phase.Ready)
        {
            foreach (Link link in new List<Link>(m_links.Values))
                link.Flush();
        }
        Shutdown();
    }

    #endregion

    #region Every frame

    void Update()
    {
        if (m_platform == null)
            return;

        TickPlatform();
        RunDeferred();

        if (m_platform == null || m_phase != Phase.Ready)
            return;

        UpdateRelogin();
        ReceivePackets();

        foreach (Link link in new List<Link>(m_links.Values))
            link.Flush();
    }

    void OnMainThread(Action action)
    {
        if (System.Threading.Thread.CurrentThread.ManagedThreadId == m_mainThreadId)
        {
            RunSafely(action);
            return;
        }

        lock (m_deferred)
            m_deferred.Enqueue(action);
    }

    void RunDeferred()
    {
        while (true)
        {
            Action action;
            lock (m_deferred)
            {
                if (m_deferred.Count <= 0)
                    return;
                action = m_deferred.Dequeue();
            }
            RunSafely(action);
        }
    }

    // An error thrown back into EOS's native code would close Moonscraper on the spot, so log it instead
    static void RunSafely(Action action)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            Debug.LogError("Duet EOS: " + e);
        }
    }

    void TickPlatform()
    {
        lock (DuetKeepAlive.tickLock)
        {
            if (m_platform != null)
                m_platform.Tick();
        }
    }

    void ReceivePackets()
    {
        P2PInterface p2p = m_platform.GetP2PInterface();
        ReceivePacketOptions options = new ReceivePacketOptions { LocalUserId = m_localUser, MaxDataSizeBytes = P2PInterface.MAX_PACKET_SIZE };

        for (int i = 0; i < MaxPacketsPerFrame; ++i)
        {
            ProductUserId peer = null;
            SocketId socket = new SocketId();
            byte channel;
            uint bytesWritten;
            Result result = p2p.ReceivePacket(ref options, ref peer, ref socket, out channel, new ArraySegment<byte>(m_receiveBuffer), out bytesWritten);
            if (result != Result.Success)
                return;

            if (peer == null || socket.SocketName != SocketName)
                continue;

            Link link = FindOrAcceptLink(peer);
            if (link != null)
                link.OnPacket(m_receiveBuffer, (int)bytesWritten);
        }
    }

    #endregion

    #region Connections

    void OnConnectionRequest(ref OnIncomingConnectionRequestInfo info)
    {
        if (info.SocketId == null || info.SocketId.Value.SocketName != SocketName || info.RemoteUserId == null)
            return;

        ProductUserId peer = info.RemoteUserId;
        OnMainThread(() =>
        {
            if (m_platform != null && m_localUser != null)
                FindOrAcceptLink(peer);
        });
    }

    // A known peer's link, or a new one for someone joining the room being hosted. Null for anyone else.
    Link FindOrAcceptLink(ProductUserId peer)
    {
        Link link;
        if (m_links.TryGetValue(peer.ToString(), out link))
            return link;

        if (m_room == null || m_room.isStopped)
            return null;

        if (!IsRoomMember(peer))
        {
            if (m_waitingPeers.Count < MaxWaitingPeers)
                m_waitingPeers.Add(peer.ToString());
            return null;
        }

        m_waitingPeers.Remove(peer.ToString());
        Accept(peer);
        link = new Link(this, peer, null);
        m_links[peer.ToString()] = link;
        m_room.Enqueue(link);
        return link;
    }

    void Accept(ProductUserId peer)
    {
        AcceptConnectionOptions options = new AcceptConnectionOptions { LocalUserId = m_localUser, RemoteUserId = peer, SocketId = m_socket };
        m_platform.GetP2PInterface().AcceptConnection(ref options);
    }

    void OnConnectionClosed(ref OnRemoteConnectionClosedInfo info)
    {
        if (info.RemoteUserId == null)
            return;

        string peer = info.RemoteUserId.ToString();
        ConnectionClosedReason reason = info.Reason;
        OnMainThread(() =>
        {
            Link link;
            if (m_links.TryGetValue(peer, out link))
                link.Close(DescribeClose(reason));
        });
    }

    bool IsRoomMember(ProductUserId peer)
    {
        if (m_room == null || m_room.lobbyId == null)
            return false;

        CopyLobbyDetailsHandleOptions options = new CopyLobbyDetailsHandleOptions { LobbyId = m_room.lobbyId, LocalUserId = m_localUser };
        LobbyDetails details;
        if (m_platform.GetLobbyInterface().CopyLobbyDetailsHandle(ref options, out details) != Result.Success || details == null)
            return false;

        bool member = false;
        LobbyDetailsGetMemberCountOptions countOptions = new LobbyDetailsGetMemberCountOptions();
        uint count = details.GetMemberCount(ref countOptions);
        for (uint i = 0; i < count && !member; ++i)
        {
            LobbyDetailsGetMemberByIndexOptions memberOptions = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = i };
            ProductUserId user = details.GetMemberByIndex(ref memberOptions);
            member = user != null && user.ToString() == peer.ToString();
        }
        details.Release();
        return member;
    }

    void OnLobbyMemberStatus(ref LobbyMemberStatusReceivedCallbackInfo info)
    {
        ProductUserId target = info.TargetUserId;
        string lobbyId = info.LobbyId;
        LobbyMemberStatus status = info.CurrentStatus;
        OnMainThread(() => OnLobbyMemberStatus(target, lobbyId, status));
    }

    void OnLobbyMemberStatus(ProductUserId target, string lobbyId, LobbyMemberStatus status)
    {
        if (m_platform == null || m_localUser == null)
            return;

        string targetKey = target != null ? target.ToString() : null;
        if (status == LobbyMemberStatus.Joined && targetKey != null && m_waitingPeers.Contains(targetKey)
            && m_room != null && lobbyId == m_room.lobbyId)
        {
            FindOrAcceptLink(target);
            return;
        }

        bool gone = status == LobbyMemberStatus.Left || status == LobbyMemberStatus.Disconnected
            || status == LobbyMemberStatus.Kicked || status == LobbyMemberStatus.Closed;
        if (!gone)
            return;

        foreach (Link link in new List<Link>(m_links.Values))
        {
            bool roomClosed = status == LobbyMemberStatus.Closed && link.lobbyId == lobbyId;
            if (roomClosed || (targetKey != null && link.peer.ToString() == targetKey))
                link.Close(roomClosed ? "The host closed the room" : "Left the room");
        }
    }

    static string DescribeClose(ConnectionClosedReason reason)
    {
        switch (reason)
        {
            case ConnectionClosedReason.ClosedByPeer: return "The other side closed the connection";
            case ConnectionClosedReason.TimedOut: return "Connection timed out";
            case ConnectionClosedReason.ConnectionFailed: return "Couldn't connect to the other PC";
            default: return "Connection closed (" + reason + ")";
        }
    }

    void RemoveLink(Link link)
    {
        m_links.Remove(link.peer.ToString());

        if (m_platform != null && m_localUser != null)
        {
            CloseConnectionOptions options = new CloseConnectionOptions { LocalUserId = m_localUser, RemoteUserId = link.peer, SocketId = m_socket };
            m_platform.GetP2PInterface().CloseConnection(ref options);

            if (link.lobbyId != null)
            {
                LeaveLobbyOptions leaveOptions = new LeaveLobbyOptions { LocalUserId = m_localUser, LobbyId = link.lobbyId };
                m_platform.GetLobbyInterface().LeaveLobby(ref leaveOptions, null, (ref LeaveLobbyCallbackInfo info) => { });
            }
        }
    }

    long OutgoingQueueBytes()
    {
        if (m_platform == null)
            return 0;

        GetPacketQueueInfoOptions options = new GetPacketQueueInfoOptions();
        PacketQueueInfo info;
        if (m_platform.GetP2PInterface().GetPacketQueueInfo(ref options, out info) != Result.Success)
            return 0;
        return (long)info.OutgoingPacketQueueCurrentSizeBytes;
    }

    // Returns false when the outgoing queue is full and the packet should be tried again next frame
    bool TrySendPacket(Link link, byte[] packet, out string error)
    {
        error = null;
        SendPacketOptions options = new SendPacketOptions
        {
            LocalUserId = m_localUser,
            RemoteUserId = link.peer,
            SocketId = m_socket,
            Channel = 0,
            Data = new ArraySegment<byte>(packet),
            AllowDelayedDelivery = true,
            Reliability = PacketReliability.ReliableOrdered,
        };

        Result result = m_platform.GetP2PInterface().SendPacket(ref options);
        if (result == Result.Success)
            return true;
        if (result == Result.InvalidAuth)
            RequestRelogin();       // Kept and sent once signed in again, rather than dropping the connection
        else if (result != Result.LimitExceeded)
            error = "Couldn't send (" + result + ")";
        return false;
    }

    #endregion

    #region Lobbies

    // From a secure random source, so nobody can predict the next room's code
    static string NewRoomCode()
    {
        char[] code = new char[RoomCodeLength];
        byte[] random = new byte[1];
        using (System.Security.Cryptography.RandomNumberGenerator generator = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            for (int i = 0; i < code.Length;)
            {
                generator.GetBytes(random);
                if (random[0] < 256 - 256 % RoomCodeLetters.Length)     // Every letter equally likely
                    code[i++] = RoomCodeLetters[random[0] % RoomCodeLetters.Length];
            }
        }
        return new string(code);
    }

    // The lobby's ID is worked out from the code rather than being the code. Lobby IDs can be seen by anyone using
    // Epic's services, so this keeps the code itself secret: guessing it back from the ID is deliberately slow.
    // Whoever joins still has to give the host the code itself. See DuetSession's hello.
    const int LobbyIdHashRounds = 20000;
    static readonly byte[] s_lobbyIdSalt = System.Text.Encoding.UTF8.GetBytes("Moonscraper Duet room");

    static string LobbyIdFor(string code)
    {
        using (System.Security.Cryptography.Rfc2898DeriveBytes hash = new System.Security.Cryptography.Rfc2898DeriveBytes(System.Text.Encoding.UTF8.GetBytes(code), s_lobbyIdSalt, LobbyIdHashRounds))
            return LobbyIdPrefix + BitConverter.ToString(hash.GetBytes(20)).Replace("-", string.Empty).ToLowerInvariant();
    }

    void CreateLobby(Room room, string code)
    {
        CreateLobbyOptions options = new CreateLobbyOptions
        {
            LocalUserId = m_localUser,
            MaxLobbyMembers = DuetRooms.MaxMembers,
            PermissionLevel = LobbyPermissionLevel.Publicadvertised,
            PresenceEnabled = false,
            AllowInvites = false,
            BucketId = BucketId,
            DisableHostMigration = true,
            LobbyId = LobbyIdFor(code),
            EnableJoinById = true,
        };
        m_platform.GetLobbyInterface().CreateLobby(ref options, null, (ref CreateLobbyCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            string lobbyId = info.LobbyId;
            OnMainThread(() => room.OnCreated(result, code, lobbyId));
        });
    }

    void DestroyLobby(string lobbyId)
    {
        if (m_platform == null || m_localUser == null)
            return;

        DestroyLobbyOptions options = new DestroyLobbyOptions { LocalUserId = m_localUser, LobbyId = lobbyId };
        m_platform.GetLobbyInterface().DestroyLobby(ref options, null, (ref DestroyLobbyCallbackInfo info) => { });
    }

    void FindLobby(Join join)
    {
        LobbyInterface lobbies = m_platform.GetLobbyInterface();
        CreateLobbySearchOptions searchOptions = new CreateLobbySearchOptions { MaxResults = 1 };
        LobbySearch search;
        Result result = lobbies.CreateLobbySearch(ref searchOptions, out search);
        if (result != Result.Success)
        {
            join.OnFailed("Couldn't search for the room (" + result + ")");
            return;
        }

        LobbySearchSetLobbyIdOptions idOptions = new LobbySearchSetLobbyIdOptions { LobbyId = LobbyIdFor(join.code) };
        search.SetLobbyId(ref idOptions);

        LobbySearchFindOptions findOptions = new LobbySearchFindOptions { LocalUserId = m_localUser };
        search.Find(ref findOptions, null, (ref LobbySearchFindCallbackInfo info) =>
        {
            Result found = info.ResultCode;
            OnMainThread(() => OnLobbyFound(join, search, found));
        });
    }

    void OnLobbyFound(Join join, LobbySearch search, Result found)
    {
        if (m_platform == null || m_localUser == null)
        {
            search.Release();
            join.OnFailed("Online rooms shut down");
            return;
        }

        LobbyDetails details = null;
        if (found == Result.Success)
        {
            LobbySearchGetSearchResultCountOptions countOptions = new LobbySearchGetSearchResultCountOptions();
            if (search.GetSearchResultCount(ref countOptions) > 0)
            {
                LobbySearchCopySearchResultByIndexOptions copyOptions = new LobbySearchCopySearchResultByIndexOptions { LobbyIndex = 0 };
                search.CopySearchResultByIndex(ref copyOptions, out details);
            }
        }
        search.Release();

        if (details == null)
        {
            join.OnFailed(found == Result.Success || found == Result.NotFound
                ? "No room with the code " + join.code + ". Check the code, or ask the host to make a new room."
                : "Couldn't search for the room (" + found + ")");
            return;
        }

        JoinLobby(join, details);
    }

    void JoinLobby(Join join, LobbyDetails details)
    {
        LobbyDetailsGetLobbyOwnerOptions ownerOptions = new LobbyDetailsGetLobbyOwnerOptions();
        ProductUserId owner = details.GetLobbyOwner(ref ownerOptions);

        JoinLobbyOptions options = new JoinLobbyOptions { LobbyDetailsHandle = details, LocalUserId = m_localUser, PresenceEnabled = false };
        m_platform.GetLobbyInterface().JoinLobby(ref options, null, (ref JoinLobbyCallbackInfo info) =>
        {
            Result result = info.ResultCode;
            string lobbyId = info.LobbyId;
            OnMainThread(() => OnLobbyJoined(join, details, owner, result, lobbyId));
        });
    }

    void OnLobbyJoined(Join join, LobbyDetails details, ProductUserId owner, Result result, string lobbyId)
    {
        details.Release();

        if (m_platform == null || m_localUser == null)
        {
            join.OnFailed("Online rooms shut down");
            return;
        }

        if (join.isCancelled)
        {
            if (result == Result.Success)
            {
                LeaveLobbyOptions leaveOptions = new LeaveLobbyOptions { LocalUserId = m_localUser, LobbyId = lobbyId };
                m_platform.GetLobbyInterface().LeaveLobby(ref leaveOptions, null, (ref LeaveLobbyCallbackInfo leaveInfo) => { });
            }
            return;
        }

        if (result != Result.Success)
        {
            join.OnFailed(result == Result.LobbyTooManyPlayers ? "That room is full." : "Couldn't join the room (" + result + ")");
            return;
        }

        if (owner == null || !owner.IsValid() || owner.ToString() == m_localUser.ToString())
        {
            join.OnFailed("That room's host can't be reached. Ask them to make a new room.");
            return;
        }

        Link existing;
        if (m_links.TryGetValue(owner.ToString(), out existing))
            existing.Close("Replaced");

        Accept(owner);
        Link link = new Link(this, owner, lobbyId);
        m_links[owner.ToString()] = link;
        join.OnJoined(link);
    }

    #endregion

    #region Credentials and native loading

    static readonly string[] s_credentialNames = { "ProductId", "SandboxId", "DeploymentId", "ClientId", "ClientSecret" };

    // GitHub builds compile the keys in (DuetEosKeys.cs, made by CI and never committed) so no credentials.txt ships.
    // Each value is XORed with a mask that changes every build, so the keys aren't plain text in the DLL.
    static bool TryReadEmbeddedCredentials(Dictionary<string, string> credentials)
    {
#if DUET_EOS_EMBEDDED
        byte[] mask = DuetEosKeys.Mask;
        for (int n = 0; n < s_credentialNames.Length; ++n)
        {
            byte[] value = DuetEosKeys.Values[n];
            char[] chars = new char[value.Length];
            for (int i = 0; i < value.Length; ++i)
                chars[i] = (char)(value[i] ^ mask[(i + n * 7) % mask.Length]);
            credentials[s_credentialNames[n]] = new string(chars);
        }
        return true;
#else
        return false;
#endif
    }

    // Otherwise EOS/credentials.txt holds lines of Name=Value for the five values from the Epic Developer Portal
    static bool TryReadCredentials(out Dictionary<string, string> credentials, out string error)
    {
        credentials = new Dictionary<string, string>();
        if (TryReadEmbeddedCredentials(credentials))
        {
            error = null;
            return true;
        }

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

        foreach (string name in s_credentialNames)
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

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr LoadLibrary(string path);

    [DllImport("kernel32", SetLastError = true)]
    static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32", CharSet = CharSet.Ansi)]
    static extern IntPtr GetProcAddress(IntPtr module, string name);

    #endregion

    /// <summary>
    /// A connection to one other person. Messages are framed as a byte stream and split into P2P packets,
    /// which arrive reliably and in order, so the receiver just joins them back up.
    /// </summary>
    class Link : IDuetLink
    {
        readonly DuetEos m_owner;
        readonly Queue<byte[]> m_outgoing = new Queue<byte[]>();
        readonly Queue<Dictionary<string, object>> m_incoming = new Queue<Dictionary<string, object>>();
        readonly DuetFraming.Assembler m_assembler;
        bool m_closeWhenSent = false;
        System.Diagnostics.Stopwatch m_closeClock;
        const int CloseGraceMs = 1000;

        public readonly ProductUserId peer;
        public readonly string lobbyId;     // Joiners only: the room to leave when this link closes

        public int id { get; set; }
        public string remoteAddress { get { return "an online room"; } }
        public string peerKey { get { return peer.ToString(); } }
        public bool isClosed { get; private set; }
        public string closeReason { get; private set; }

        // What's waiting here plus everything in EOS's own outgoing queue, which all links share
        public long queuedBytes
        {
            get
            {
                long waiting = 0;
                foreach (byte[] packet in m_outgoing)
                    waiting += packet.Length;
                return waiting + m_owner.OutgoingQueueBytes();
            }
        }

        public Link(DuetEos owner, ProductUserId peer, string lobbyId)
        {
            m_owner = owner;
            this.peer = peer;
            this.lobbyId = lobbyId;
            // Only the host sends big messages. A link with no room to leave is someone joining this computer's room.
            m_assembler = new DuetFraming.Assembler(lobbyId == null ? DuetFraming.MaxJoinerMessageBytes : DuetFraming.MaxMessageBytes);
        }

        public void Send(Dictionary<string, object> message)
        {
            if (isClosed || m_closeWhenSent)
                return;

            byte[] frame = DuetFraming.Encode(message);
            for (int offset = 0; offset < frame.Length; offset += P2PInterface.MAX_PACKET_SIZE)
            {
                int size = Math.Min(P2PInterface.MAX_PACKET_SIZE, frame.Length - offset);
                byte[] packet = new byte[size];
                Buffer.BlockCopy(frame, offset, packet, 0, size);
                m_outgoing.Enqueue(packet);
            }

            Flush();
        }

        public bool TryReceive(out Dictionary<string, object> message)
        {
            if (m_incoming.Count > 0)
            {
                message = m_incoming.Dequeue();
                return true;
            }

            message = null;
            return false;
        }

        public void Flush()
        {
            while (!isClosed && m_outgoing.Count > 0)
            {
                string error;
                if (!m_owner.TrySendPacket(this, m_outgoing.Peek(), out error))
                {
                    if (error != null)
                        Close(error);
                    return;
                }
                m_outgoing.Dequeue();
            }

            // Closing the connection can drop what EOS hasn't delivered yet, such as a goodbye, so wait a moment
            if (m_closeWhenSent && m_outgoing.Count <= 0 && m_closeClock.ElapsedMilliseconds >= CloseGraceMs)
                Close(closeReason);
        }

        public void OnPacket(byte[] data, int count)
        {
            if (isClosed)
                return;

            try
            {
                m_owner.m_receivedMessages.Clear();
                m_assembler.Append(data, 0, count, m_owner.m_receivedMessages);
                foreach (Dictionary<string, object> message in m_owner.m_receivedMessages)
                    m_incoming.Enqueue(message);
            }
            catch (Exception e)
            {
                Close("Got data this version doesn't understand: " + e.Message);
            }
        }

        public void Close(string reason = "Disconnected")
        {
            if (isClosed)
                return;

            isClosed = true;
            closeReason = reason;
            m_outgoing.Clear();
            m_owner.RemoveLink(this);
        }

        public void CloseAfterSending(string reason)
        {
            closeReason = reason;
            if (!m_closeWhenSent)
                m_closeClock = System.Diagnostics.Stopwatch.StartNew();
            m_closeWhenSent = true;
            Flush();
        }
    }

    /// <summary>
    /// The room being hosted: a lobby named after its room code, accepting connections from whoever joins it.
    /// </summary>
    class Room : IDuetListener
    {
        const int MaxCreateAttempts = 3;

        readonly DuetEos m_owner;
        readonly Queue<IDuetLink> m_accepted = new Queue<IDuetLink>();
        bool m_createStarted = false;
        int m_attempts = 0;
        string m_lobbyId;
        public string lobbyId { get { return m_lobbyId; } }

        public string roomCode { get; private set; }
        public string error { get; private set; }
        public bool isStopped { get; private set; }

        // Opening a room again after a crash: its code, and when to try next. Epic only closes the old room once it
        // notices the host has gone, which can take a while, so this keeps trying until then.
        readonly string m_fixedCode;
        float m_retryAt = 0;
        const float RetrySeconds = 5;
        bool m_triedDestroy = false;

        public Room(DuetEos owner, string fixedCode)
        {
            m_owner = owner;
            m_fixedCode = fixedCode;
        }

        public bool TryAccept(out IDuetLink link)
        {
            link = null;
            if (isStopped)
                return false;

            if (m_owner.m_phase == Phase.Failed)
            {
                error = "Couldn't open an online room: " + m_owner.m_failure;
                return false;
            }

            if (m_owner.m_phase == Phase.Ready && !m_createStarted && Time.realtimeSinceStartup >= m_retryAt)
            {
                m_createStarted = true;
                ++m_attempts;
                m_owner.CreateLobby(this, m_fixedCode ?? NewRoomCode());
            }

            if (m_accepted.Count <= 0)
                return false;

            link = m_accepted.Dequeue();
            return true;
        }

        public void Enqueue(IDuetLink link)
        {
            m_accepted.Enqueue(link);
        }

        public void OnCreated(Result result, string code, string lobbyId)
        {
            if (isStopped)
            {
                if (result == Result.Success)
                    m_owner.DestroyLobby(lobbyId);
                return;
            }

            if (result == Result.Success)
            {
                roomCode = code;
                m_lobbyId = lobbyId;
                return;
            }

            // The room from before the crash is still open. It's ours, so try closing it, and keep trying to open it.
            if (result == Result.LobbyLobbyAlreadyExists && m_fixedCode != null)
            {
                if (!m_triedDestroy)
                {
                    m_triedDestroy = true;
                    Debug.Log("Duet EOS: the room from before is still open, waiting for Epic to close it");
                    m_owner.DestroyLobby(LobbyIdFor(code));
                }
                m_retryAt = Time.realtimeSinceStartup + RetrySeconds;
                m_createStarted = false;
                return;
            }

            // Another room may already have this code, so try a new one
            if (result == Result.LobbyLobbyAlreadyExists && m_attempts < MaxCreateAttempts)
            {
                m_createStarted = false;
                return;
            }

            if (result == Result.InvalidAuth)
            {
                // Signed out by time: sign in again and try once that's done
                m_owner.RequestRelogin();
                m_retryAt = Time.realtimeSinceStartup + RetrySeconds;
                m_createStarted = false;
                return;
            }

            error = "Couldn't open an online room (" + result + ")";
        }

        public void Stop()
        {
            if (isStopped)
                return;

            isStopped = true;
            if (m_lobbyId != null)
                m_owner.DestroyLobby(m_lobbyId);

            if (m_owner.m_room == this)
                m_owner.m_room = null;
        }
    }

    /// <summary>
    /// A joiner finding a room by its code, joining it, and opening a connection to its host.
    /// </summary>
    class Join : IDuetConnector
    {
        readonly DuetEos m_owner;
        bool m_started = false;
        IDuetLink m_link;
        string m_error;

        public readonly string code;
        public bool isCancelled { get; private set; }

        public Join(DuetEos owner, string code)
        {
            m_owner = owner;
            this.code = code;
        }

        public bool TryFinish(out IDuetLink link, out string error)
        {
            if (m_link == null && m_error == null)
            {
                if (code.Length != RoomCodeLength)
                    m_error = "A room code has " + RoomCodeLength + " letters and numbers.";
                else if (m_owner.m_phase == Phase.Failed)
                    m_error = m_owner.m_failure;
                else if (m_owner.m_phase == Phase.Ready && !m_started)
                {
                    m_started = true;
                    m_owner.FindLobby(this);
                }
            }

            link = m_link;
            error = m_error;
            return link != null || error != null;
        }

        public void OnJoined(IDuetLink link)
        {
            m_link = link;
        }

        public void OnFailed(string error)
        {
            if (error.Contains("InvalidAuth"))
            {
                // Signed out by time: sign in again, so the next try (or the automatic rejoin) works
                m_owner.RequestRelogin();
                error = "Epic sign-in expired and is being renewed. Try again in a few seconds.";
            }
            m_error = error;
        }

        public void Cancel()
        {
            isCancelled = true;
            if (m_link != null)
                m_link.Close("Cancelled");
        }
    }
}

#endif
