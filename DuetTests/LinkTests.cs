// See LICENSE in project root for license information.

// Checks the parts online rooms rely on without needing Epic's SDK: rebuilding messages from packets of any size,
// and running a whole session over links that split every message into 1170-byte packets the way EOS P2P does.

using System;
using System.Collections.Generic;
using MoonscraperChartEditor.Song;

public static class LinkTests
{
    const int PacketSize = 1170;    // P2PInterface.MAX_PACKET_SIZE

    static int s_failures = 0;

    public static int Run()
    {
        CheckAssembler();
        CheckSessionOverPackets();
        CheckHostCrash();
        CheckRoomCodes();
        CheckKeepAlive();
        return s_failures;
    }

    // While a Windows pop-up freezes the main thread, online rooms keep ticking; afterwards they stop
    static void CheckKeepAlive()
    {
        int ticks = 0;
        DuetKeepAlive.tick = () => System.Threading.Interlocked.Increment(ref ticks);
        using (DuetKeepAlive.WhileBlocked())
            System.Threading.Thread.Sleep(300);
        int afterwards = ticks;
        System.Threading.Thread.Sleep(150);
        DuetKeepAlive.tick = null;

        Expect("rooms keep ticking while a pop-up is open", afterwards >= 3);
        Expect("and stop once it closes", ticks == afterwards);
    }

    static void CheckAssembler()
    {
        List<Dictionary<string, object>> sent = new List<Dictionary<string, object>>
        {
            new Dictionary<string, object> { { "type", "small" } },
            new Dictionary<string, object> { { "type", "big" }, { "chart", new string('x', 50000) + "é€" } },
            new Dictionary<string, object> { { "type", "empty" } },
        };

        List<byte> stream = new List<byte>();
        foreach (Dictionary<string, object> message in sent)
            stream.AddRange(DuetFraming.Encode(message));
        byte[] bytes = stream.ToArray();

        foreach (int pieceSize in new[] { 1, 3, 7, PacketSize, bytes.Length })
        {
            DuetFraming.Assembler assembler = new DuetFraming.Assembler();
            List<Dictionary<string, object>> received = new List<Dictionary<string, object>>();
            for (int offset = 0; offset < bytes.Length; offset += pieceSize)
                assembler.Append(bytes, offset, Math.Min(pieceSize, bytes.Length - offset), received);

            bool same = received.Count == sent.Count;
            for (int i = 0; same && i < sent.Count; ++i)
                same = DuetJson.Write(received[i]) == DuetJson.Write(sent[i]);
            Expect("messages rebuild from " + pieceSize + "-byte pieces", same);
        }

        bool rejected = false;
        try
        {
            new DuetFraming.Assembler().Append(new byte[] { 0x7f, 0xff, 0xff, 0xff }, 0, 4, new List<Dictionary<string, object>>());
        }
        catch (System.IO.InvalidDataException)
        {
            rejected = true;
        }
        Expect("an impossible message length is rejected", rejected);
    }

    static void CheckSessionOverPackets()
    {
        SessionTests.TestSong hostSong = new SessionTests.TestSong("packets-host");
        SessionTests.TestSong joinerSong = new SessionTests.TestSong("packets-joiner");

        // A chart big enough that joining takes hundreds of packets
        List<SongEditChange> bigChart = new List<SongEditChange>();
        for (uint tick = 0; tick < 192 * 6000; tick += 96)
            bigChart.Add(SessionTests.Add(new Note(tick, (int)(tick / 96 % 5))));
        hostSong.Edit(bigChart.ToArray());

        PacketRoom room = new PacketRoom("K7Q2MX");
        DuetSession host = DuetSession.Host(hostSong, "Robert", room, "Opening an online room");
        DuetSession joiner = DuetSession.Join(joinerSong, "Alex", room.Join(), "Looking for room K7Q2MX", "K7Q2MX");
        List<DuetSession> all = new List<DuetSession> { host, joiner };

        // Someone who reached the room without its code, as anyone could through Epic's lobby search, is turned away
        DuetSession stranger = DuetSession.Join(new SessionTests.TestSong("packets-stranger"), "Stranger", room.Join(), "Looking for room", "AAAAAA");
        SessionTests.Pump(new List<DuetSession> { host, stranger }, () => !stranger.isActive);
        Expect("the room code is checked by the host", !stranger.isActive && stranger.endReason.Contains("Wrong room code") && host.users.Count == 1);

        SessionTests.Pump(all, () => joiner.state == DuetSession.State.Connected);
        Expect("joiner gets a large chart split into packets", SessionTests.Same(hostSong, joinerSong) && room.packetsSent > 100);
        Expect("host shows the room code", host.roomCode == "K7Q2MX");

        joiner.OnLocalEdit(joinerSong.Edit(SessionTests.Add(new Section("Chorus", 768))));
        host.OnLocalEdit(hostSong.Edit(SessionTests.Add(new BPM(1536, 180000))));
        SessionTests.Pump(all, () => SessionTests.Same(hostSong, joinerSong) && joiner.unacknowledgedEditCount == 0);
        Expect("edits go both ways over packets", SessionTests.Same(hostSong, joinerSong) && joiner.unacknowledgedEditCount == 0);

        host.Leave();
        SessionTests.Pump(all, () => !joiner.isActive);
        Expect("joiner is told when the host closes the room", !joiner.isActive && room.isStopped);

        DuetSession failed = DuetSession.Join(new SessionTests.TestSong("packets-nobody"), "Alex", new FailingConnector("No room with the code ABCDEF."), "Looking for room ABCDEF");
        SessionTests.Pump(new List<DuetSession> { failed }, () => !failed.isActive);
        Expect("a wrong room code ends with the reason", !failed.isActive && failed.endReason.Contains("No room with the code"));
    }

    // The host crashes: joiners keep the chart and wait, and when the host is back, the chart comes back from them
    static void CheckHostCrash()
    {
        SessionTests.TestSong hostSong = new SessionTests.TestSong("crash-host");
        SessionTests.TestSong aliceSong = new SessionTests.TestSong("crash-alice");
        SessionTests.TestSong bobSong = new SessionTests.TestSong("crash-bob");
        hostSong.Edit(SessionTests.Add(new Note(192, 1)));

        PacketRoom room = new PacketRoom("CR4SH2");
        Func<IDuetConnector> reconnect = () => room.isStopped ? (IDuetConnector)new FailingConnector("No room with the code CR4SH2.") : room.Join();
        DuetSession host = DuetSession.Host(hostSong, "Robert", room, "Opening an online room");
        DuetSession alice = DuetSession.Join(aliceSong, "Alex", room.Join(), "Looking for room CR4SH2", "CR4SH2");
        DuetSession bob = DuetSession.Join(bobSong, "Bo", room.Join(), "Looking for room CR4SH2", "CR4SH2");
        alice.reconnect = reconnect;
        bob.reconnect = reconnect;
        SessionTests.Pump(new List<DuetSession> { host, alice, bob }, () => alice.state == DuetSession.State.Connected && bob.state == DuetSession.State.Connected);

        alice.OnLocalEdit(aliceSong.Edit(SessionTests.Add(new Section("Verse", 384))));
        SessionTests.Pump(new List<DuetSession> { host, alice, bob }, () => SessionTests.Same(hostSong, bobSong) && alice.unacknowledgedEditCount == 0);

        // Gone without a word, the way a crash looks to everyone else
        room.Crash();
        SessionTests.Pump(new List<DuetSession> { alice, bob }, () => alice.state == DuetSession.State.HostLost && bob.state == DuetSession.State.HostLost);
        Expect("joiners wait for a host that crashed, instead of leaving", alice.state == DuetSession.State.HostLost && bob.state == DuetSession.State.HostLost
            && alice.isActive && aliceSong.song.sections.Count == 1);

        // The host starts again with only what was saved, and the same room
        SessionTests.TestSong restarted = new SessionTests.TestSong("crash-host-restarted");
        room = new PacketRoom("CR4SH2");
        DuetSession host2 = DuetSession.Host(restarted, "Robert", room, "Opening an online room");
        host2.ExpectRecovery();
        List<DuetSession> all = new List<DuetSession> { host2, alice, bob };
        SessionTests.Pump(all, () => alice.state == DuetSession.State.Connected && bob.state == DuetSession.State.Connected
            && SessionTests.Same(restarted, aliceSong) && SessionTests.Same(restarted, bobSong));
        Expect("the host gets the latest chart back from a joiner", restarted.recoveredCharts == 1 && SessionTests.Same(restarted, aliceSong)
            && restarted.song.sections.Count == 1 && !host2.isAwaitingRecoveryChart);
        Expect("and everyone is back in the session", alice.state == DuetSession.State.Connected && bob.state == DuetSession.State.Connected && host2.users.Count == 3);

        bob.OnLocalEdit(bobSong.Edit(SessionTests.Add(new Section("Chorus", 768))));
        SessionTests.Pump(all, () => SessionTests.Same(restarted, aliceSong) && restarted.song.sections.Count == 2);
        Expect("editing carries on after the host is back", SessionTests.Same(restarted, aliceSong) && restarted.song.sections.Count == 2);

        // Ending on purpose isn't a crash: nobody waits
        host2.Leave();
        SessionTests.Pump(all, () => !alice.isActive && !bob.isActive);
        Expect("a host ending the session on purpose ends it for everyone", !alice.isActive && !bob.isActive && alice.endReason.Contains("ended"));
    }

    static void CheckRoomCodes()
    {
        bool refused = false;
        try { DuetJson.Read(new string('[', 100000)); }
        catch (FormatException) { refused = true; }
        Expect("a message nested thousands deep is refused, not a crash", refused);

        Expect("room codes ignore case, spaces and dashes", DuetRooms.NormalizeCode(" k7q-2mx ") == "K7Q2MX");
    }

    static void Expect(string label, bool condition)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
        if (!condition)
            ++s_failures;
    }

    // Two ends of a connection that deliver each message as a stream of packets, like EOS P2P
    class PacketLink : IDuetLink
    {
        readonly PacketRoom m_room;
        readonly DuetFraming.Assembler m_assembler = new DuetFraming.Assembler();
        readonly Queue<Dictionary<string, object>> m_incoming = new Queue<Dictionary<string, object>>();
        public PacketLink other;

        public PacketLink(PacketRoom room) { m_room = room; }

        public int id { get; set; }
        public string remoteAddress { get { return "an online room"; } }
        public string peerKey { get { return "packet-" + GetHashCode(); } }
        public bool isClosed { get; private set; }
        public string closeReason { get; private set; }
        public long queuedBytes { get { return 0; } }

        public void Send(Dictionary<string, object> message)
        {
            if (isClosed)
                return;

            byte[] frame = DuetFraming.Encode(message);
            List<Dictionary<string, object>> rebuilt = new List<Dictionary<string, object>>();
            for (int offset = 0; offset < frame.Length; offset += PacketSize)
            {
                ++m_room.packetsSent;
                other.m_assembler.Append(frame, offset, Math.Min(PacketSize, frame.Length - offset), rebuilt);
            }
            foreach (Dictionary<string, object> m in rebuilt)
                other.m_incoming.Enqueue(m);
        }

        public bool TryReceive(out Dictionary<string, object> message)
        {
            message = m_incoming.Count > 0 ? m_incoming.Dequeue() : null;
            return message != null;
        }

        public void Close(string reason = "Disconnected")
        {
            if (isClosed)
                return;
            isClosed = true;
            closeReason = reason;
            other.Close("The other side closed the connection");
        }

        public void CloseAfterSending(string reason) { Close(reason); }
    }

    class PacketRoom : IDuetListener
    {
        readonly Queue<IDuetLink> m_waiting = new Queue<IDuetLink>();
        readonly List<PacketLink> m_hostEnds = new List<PacketLink>();
        readonly string m_code;
        public int packetsSent = 0;
        public bool isStopped { get; private set; }

        public PacketRoom(string code) { m_code = code; }

        public string roomCode { get { return m_code; } }
        public string error { get { return null; } }

        public IDuetConnector Join()
        {
            PacketLink hostEnd = new PacketLink(this);
            PacketLink joinerEnd = new PacketLink(this);
            m_hostEnds.Add(hostEnd);
            hostEnd.other = joinerEnd;
            joinerEnd.other = hostEnd;
            m_waiting.Enqueue(hostEnd);
            return new ReadyConnector(joinerEnd);
        }

        public bool TryAccept(out IDuetLink link)
        {
            link = m_waiting.Count > 0 ? m_waiting.Dequeue() : null;
            return link != null;
        }

        public void Stop() { isStopped = true; }

        // The host's end of every connection drops at once, without saying goodbye
        public void Crash()
        {
            isStopped = true;
            foreach (PacketLink link in m_hostEnds)
                link.Close("Connection timed out");
        }
    }

    class ReadyConnector : IDuetConnector
    {
        readonly IDuetLink m_link;
        public ReadyConnector(IDuetLink link) { m_link = link; }
        public bool TryFinish(out IDuetLink link, out string error) { link = m_link; error = null; return true; }
        public void Cancel() { }
    }

    class FailingConnector : IDuetConnector
    {
        readonly string m_error;
        public FailingConnector(string error) { m_error = error; }
        public bool TryFinish(out IDuetLink link, out string error) { link = null; error = m_error; return true; }
        public void Cancel() { }
    }
}
