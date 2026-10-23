// See LICENSE in project root for license information.

// Checks that cursors, ghost notes and the song's details (name, artist and so on) reach everyone in a session.

using System;
using System.Collections.Generic;

public static class PresenceTests
{
    static int s_failures = 0;

    public static int Run()
    {
        SessionTests.TestSong hostSong = new SessionTests.TestSong("presence-host");
        SessionTests.TestSong aliceSong = new SessionTests.TestSong("presence-alice");
        SessionTests.TestSong bobSong = new SessionTests.TestSong("presence-bob");
        hostSong.song.name = "Through the Fire and Flames";
        hostSong.song.metaData.artist = "DragonForce";
        hostSong.song.iniProperties = SongIniFunctions.SongIniFromString("delay = 0\nloading_phrase = Hold on tight");

        DuetSession host = DuetSession.Host(hostSong, "Robert", 0);
        DuetSession alice = DuetSession.Join(aliceSong, "Alex", "127.0.0.1", host.port);
        List<DuetSession> all = new List<DuetSession> { host, alice };

        // The host is already pointing somewhere before Alex joins
        DuetPresence robert = new DuetPresence { color = 4, viewTick = 768, instrument = "Guitar", difficulty = "Expert", hasPointer = true, pointerX = -1.5f, pointerTick = 960 };
        robert.ghosts.Add(new DuetPresence.Ghost { x = -1f, tick = 960, isOpen = false });
        host.SetLocalPresence(robert);

        SessionTests.Pump(all, () => alice.state == DuetSession.State.Connected && alice.presences.ContainsKey(0));
        DuetPresence seen;
        alice.presences.TryGetValue(0, out seen);
        Expect("a joiner sees the host's cursor straight away", seen != null && seen.hasPointer && seen.pointerTick == 960 && seen.pointerX == -1.5f && seen.color == 4);
        Expect("a joiner sees the host's ghost note", seen != null && seen.ghosts.Count == 1 && seen.ghosts[0].tick == 960 && !seen.ghosts[0].isOpen);
        Expect("a joiner gets the song's details, custom ini settings included",
            aliceSong.song.name == "Through the Fire and Flames" && aliceSong.song.metaData.artist == "DragonForce"
            && SongIniFunctions.IniTextFromSongProperties(aliceSong.song.iniProperties).Contains("Hold on tight"));
        Expect("a joiner knows their own user id", alice.localUserId == 1);

        // Alex moves; the host and a second joiner see it
        DuetSession bob = DuetSession.Join(bobSong, "Sam", "127.0.0.1", host.port);
        all.Add(bob);
        SessionTests.Pump(all, () => bob.state == DuetSession.State.Connected && bob.users.Count == 3 && alice.users.Count == 3);

        DuetPresence alex = new DuetPresence { color = 0, viewTick = 1536, instrument = "Drums", difficulty = "Hard" };
        alice.SetLocalPresence(alex);
        SessionTests.Pump(all, () => host.presences.ContainsKey(1) && bob.presences.ContainsKey(1));
        Expect("the host sees a joiner's presence", host.presences.ContainsKey(1) && host.presences[1].viewTick == 1536 && !host.presences[1].hasPointer);
        Expect("other joiners see it too, through the host", bob.presences.ContainsKey(1) && bob.presences[1].instrument == "Drums");
        Expect("nobody gets their own presence back", !alice.presences.ContainsKey(1));

        // Song details change on a joiner, and on the host at about the same time, in different fields
        aliceSong.song.metaData.charter = "Alex";
        hostSong.song.metaData.album = "Inhuman Rampage";
        SessionTests.Pump(all, () => hostSong.song.metaData.charter == "Alex" && bobSong.song.metaData.charter == "Alex"
            && aliceSong.song.metaData.album == "Inhuman Rampage" && bobSong.song.metaData.album == "Inhuman Rampage");
        Expect("a joiner's change to the song's details reaches everyone", hostSong.song.metaData.charter == "Alex" && bobSong.song.metaData.charter == "Alex");
        Expect("a change on the host at the same time isn't lost", aliceSong.song.metaData.album == "Inhuman Rampage" && bobSong.song.metaData.album == "Inhuman Rampage"
            && hostSong.song.metaData.charter == "Alex");

        bobSong.song.offset = 0.25f;
        SessionTests.Pump(all, () => hostSong.song.offset == 0.25f && aliceSong.song.offset == 0.25f);
        Expect("the offset syncs exactly", hostSong.song.offset == 0.25f && aliceSong.song.offset == 0.25f);

        // When Alex leaves, their cursor goes away for everyone
        alice.Leave();
        SessionTests.Pump(all, () => !host.presences.ContainsKey(1) && !bob.presences.ContainsKey(1));
        Expect("a leaver's cursor disappears", !host.presences.ContainsKey(1) && !bob.presences.ContainsKey(1));
        Expect("a joiner that got the chart says so", alice.hasReceivedSong && bob.hasReceivedSong);

        host.Leave();
        SessionTests.Pump(all, () => !bob.isActive);

        DuetPresence round = DuetPresence.FromJson((Dictionary<string, object>)DuetJson.Read(DuetJson.Write(robert.ToJson())));
        Expect("presence survives being written and read", DuetJson.Write(round.ToJson()) == DuetJson.Write(robert.ToJson()));

        return s_failures;
    }

    static void Expect(string label, bool condition)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
        if (!condition)
            ++s_failures;
    }
}
