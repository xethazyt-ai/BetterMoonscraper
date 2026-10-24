// See LICENSE in project root for license information.

// Checks that cursors, ghost notes, selections and the song's details (name, artist and so on) reach everyone in a session.

using System;
using System.Collections.Generic;
using MoonscraperChartEditor.Song;

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

        DuetSession host = DuetSession.Host(hostSong, "Robert", 0, "pw");
        DuetSession alice = DuetSession.Join(aliceSong, "Alex", "127.0.0.1", host.port, "pw");
        List<DuetSession> all = new List<DuetSession> { host, alice };

        // The host is already pointing somewhere before Alex joins
        DuetPresence robert = new DuetPresence { color = 4, viewTick = 768, instrument = "Guitar", difficulty = "Expert", hasPointer = true, pointerX = -1.5f, pointerTick = 960 };
        robert.ghosts.Add(new DuetPresence.Ghost { tick = 960, rawNote = 2, flags = 4 });
        host.SetLocalPresence(robert);

        SessionTests.Pump(all, () => alice.state == DuetSession.State.Connected && alice.presences.ContainsKey(0));
        DuetPresence seen;
        alice.presences.TryGetValue(0, out seen);
        Expect("a joiner sees the host's cursor straight away", seen != null && seen.hasPointer && seen.pointerTick == 960 && seen.pointerX == -1.5f && seen.color == 4);
        Expect("a joiner sees the host's ghost note", seen != null && seen.ghosts.Count == 1 && seen.ghosts[0].tick == 960 && seen.ghosts[0].rawNote == 2 && seen.ghosts[0].flags == 4);
        Expect("a joiner gets the song's details, custom ini settings included",
            aliceSong.song.name == "Through the Fire and Flames" && aliceSong.song.metaData.artist == "DragonForce"
            && SongIniFunctions.IniTextFromSongProperties(aliceSong.song.iniProperties).Contains("Hold on tight"));
        Expect("a joiner knows their own user id", alice.localUserId == 1);

        // Alex moves; the host and a second joiner see it
        DuetSession bob = DuetSession.Join(bobSong, "Sam", "127.0.0.1", host.port, "pw");
        all.Add(bob);
        SessionTests.Pump(all, () => bob.state == DuetSession.State.Connected && bob.users.Count == 3 && alice.users.Count == 3);

        DuetPresence alex = new DuetPresence { color = 0, viewTick = 1536, instrument = "Drums", difficulty = "Hard" };
        alice.SetLocalPresence(alex);
        SessionTests.Pump(all, () => host.presences.ContainsKey(1) && bob.presences.ContainsKey(1));
        Expect("the host sees a joiner's presence", host.presences.ContainsKey(1) && host.presences[1].viewTick == 1536 && !host.presences[1].hasPointer);
        Expect("other joiners see it too, through the host", bob.presences.ContainsKey(1) && bob.presences[1].instrument == "Drums");
        Expect("nobody gets their own presence back", !alice.presences.ContainsKey(1));

        // Chat: a joiner says something, then the host answers
        alice.SendChat("  can you take the solo?\n");
        SessionTests.Pump(all, () => host.chat.Count == 1 && bob.chat.Count == 1);
        Expect("a chat message reaches everyone, with who sent it", host.chat.Count == 1 && bob.chat.Count == 1 && alice.chat.Count == 1
            && bob.chat[0].name == "Alex" && bob.chat[0].userId == 1 && bob.chat[0].text == "can you take the solo?");
        host.SendChat("sure");
        alice.SendChat("   ");
        SessionTests.Pump(all, () => alice.chat.Count == 2 && bob.chat.Count == 2);
        Expect("the host's messages reach everyone, and blank ones are dropped", alice.chat.Count == 2 && bob.chat.Count == 2 && host.chat.Count == 2
            && alice.chat[1].name == "Robert" && alice.chat[1].text == "sure");

        DuetPresence typing = new DuetPresence { color = 0, viewTick = 1536, instrument = "Drums", difficulty = "Hard", isTyping = true };
        alice.SetLocalPresence(typing);
        SessionTests.Pump(all, () => bob.presences.ContainsKey(1) && bob.presences[1].isTyping);
        Expect("everyone sees who's typing", bob.presences.ContainsKey(1) && bob.presences[1].isTyping && host.presences[1].isTyping);

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

        // Selections: Alex selects two notes and a BPM marker, the host selects one note
        DuetSelection aliceSelection = new DuetSelection { instrument = "Drums", difficulty = "Hard" };
        aliceSelection.items.Add(new DuetSelection.Item { classID = (int)SongObject.ID.Note, tick = 192, extra = 2 });
        aliceSelection.items.Add(new DuetSelection.Item { classID = (int)SongObject.ID.Note, tick = 384, extra = 0 });
        aliceSelection.items.Add(new DuetSelection.Item { classID = (int)SongObject.ID.BPM, tick = 0, extra = 0 });
        alice.SetLocalSelection(aliceSelection);
        DuetSelection hostSelection = new DuetSelection { instrument = "Guitar", difficulty = "Expert" };
        hostSelection.items.Add(new DuetSelection.Item { classID = (int)SongObject.ID.Note, tick = 768, extra = 4 });
        host.SetLocalSelection(hostSelection);
        SessionTests.Pump(all, () => host.selections.ContainsKey(1) && bob.selections.ContainsKey(1) && alice.selections.ContainsKey(0));
        Expect("everyone sees what a joiner selected", host.selections.ContainsKey(1) && bob.selections.ContainsKey(1)
            && bob.selections[1].items.Count == 3 && bob.selections[1].items[0].tick == 192 && bob.selections[1].items[0].extra == 2
            && bob.selections[1].instrument == "Drums");
        Expect("everyone sees what the host selected", alice.selections.ContainsKey(0) && bob.selections[0].items.Count == 1 && bob.selections[0].items[0].extra == 4);
        Expect("nobody gets their own selection back", !alice.selections.ContainsKey(1) && !host.selections.ContainsKey(0));

        alice.SetLocalSelection(new DuetSelection { instrument = "Drums", difficulty = "Hard" });
        SessionTests.Pump(all, () => bob.selections[1].items.Count == 0);
        Expect("deselecting everything clears it for everyone", bob.selections[1].items.Count == 0 && host.selections[1].items.Count == 0);
        alice.SetLocalSelection(aliceSelection);
        SessionTests.Pump(all, () => bob.selections[1].items.Count == 3);

        DuetPresence dragging = new DuetPresence { viewTick = 1536, instrument = "Drums", difficulty = "Hard",
            hasDrag = true, dragX1 = -1.5f, dragTick1 = 192, dragX2 = 2.25f, dragTick2 = 960, dragRemoving = true };
        alice.SetLocalPresence(dragging);
        SessionTests.Pump(all, () => bob.presences.ContainsKey(1) && bob.presences[1].hasDrag);
        Expect("everyone sees a deselect drag box", bob.presences[1].hasDrag && bob.presences[1].dragRemoving
            && bob.presences[1].dragX1 == -1.5f && bob.presences[1].dragTick2 == 960);

        // When Alex leaves, their cursor goes away for everyone
        alice.Leave();
        SessionTests.Pump(all, () => !host.presences.ContainsKey(1) && !bob.presences.ContainsKey(1));
        Expect("a leaver's cursor disappears", !host.presences.ContainsKey(1) && !bob.presences.ContainsKey(1));
        SessionTests.Pump(all, () => !bob.selections.ContainsKey(1));
        Expect("a leaver's selection disappears", !host.selections.ContainsKey(1) && !bob.selections.ContainsKey(1));
        Expect("a joiner that got the chart says so", alice.hasReceivedSong && bob.hasReceivedSong);

        SessionTests.TestSong carolSong = new SessionTests.TestSong("presence-carol");
        DuetSession carol = DuetSession.Join(carolSong, "Casey", "127.0.0.1", host.port, "pw");
        all.Add(carol);
        SessionTests.Pump(all, () => carol.state == DuetSession.State.Connected && carol.chat.Count == 2);
        SessionTests.Pump(all, () => carol.selections.ContainsKey(0));
        Expect("someone joining later sees the host's selection", carol.selections.ContainsKey(0) && carol.selections[0].items.Count == 1);
        Expect("someone joining later sees what was said", carol.chat.Count == 2 && carol.chat[0].name == "Alex" && carol.chat[1].text == "sure");

        // A locked session turns new people away but keeps everyone already in
        host.isLocked = true;
        DuetSession dave = DuetSession.Join(new SessionTests.TestSong("presence-dave"), "Dev", "127.0.0.1", host.port, "pw");
        all.Add(dave);
        SessionTests.Pump(all, () => !dave.isActive);
        Expect("nobody new can join a locked session", !dave.isActive && dave.endReason.Contains("locked") && carol.isActive && host.users.Count == 3);
        host.isLocked = false;

        // A full session turns new people away too
        host.maxUsers = 3;
        DuetSession erin = DuetSession.Join(new SessionTests.TestSong("presence-erin"), "Erin", "127.0.0.1", host.port, "pw");
        all.Add(erin);
        SessionTests.Pump(all, () => !erin.isActive);
        Expect("nobody new can join a full session", !erin.isActive && erin.endReason.Contains("full") && host.users.Count == 3);
        host.maxUsers = 1;
        Expect("the room size stays between 2 and 64", host.maxUsers == 2);
        host.maxUsers = 100;
        Expect("the room size can't go past 64", host.maxUsers == 64);

        // A direct IP session needs its password
        DuetSession frank = DuetSession.Join(new SessionTests.TestSong("presence-frank"), "Frank", "127.0.0.1", host.port, "nope");
        all.Add(frank);
        SessionTests.Pump(all, () => !frank.isActive);
        Expect("the wrong password is turned away", !frank.isActive && frank.endReason.Contains("Wrong password") && host.users.Count == 3);

        // The host can remove someone, who then can't come back. Last, since everyone here shares one address.
        int casey = host.users.Find(u => u.name == "Casey").id;
        host.Kick(casey);
        SessionTests.Pump(all, () => !carol.isActive);
        Expect("the host can remove someone", !carol.isActive && carol.endReason.Contains("removed") && host.users.Count == 2 && !host.users.Exists(u => u.id == casey));
        DuetSession again = DuetSession.Join(new SessionTests.TestSong("presence-again"), "Casey", "127.0.0.1", host.port, "pw");
        all.Add(again);
        SessionTests.Pump(all, () => !again.isActive);
        Expect("someone removed can't rejoin", !again.isActive && again.endReason.Contains("can't join"));

        host.Leave();
        SessionTests.Pump(all, () => !bob.isActive && !carol.isActive);

        int pink = DuetPresence.MakeCustomColor(new UnityEngine.Color(1f, 0.5f, 0.25f));
        UnityEngine.Color back = DuetPresence.GetColor(pink);
        Expect("a custom color comes back the same", DuetPresence.IsCustomColor(pink) && back.r == 1f && Math.Abs(back.g - 128 / 255f) < 0.001f && Math.Abs(back.b - 64 / 255f) < 0.001f);
        Expect("a custom color is named by its hex code", DuetPresence.GetColorName(pink) == "#FF8040" && DuetPresence.GetColorName(2) == "Yellow");
        Expect("presets and custom colors are valid, nonsense isn't", DuetPresence.IsValidColor(pink) && DuetPresence.IsValidColor(7) && !DuetPresence.IsValidColor(8) && !DuetPresence.IsValidColor(-1));
        DuetPresence customRound = DuetPresence.FromJson((Dictionary<string, object>)DuetJson.Read(DuetJson.Write(new DuetPresence { color = pink }.ToJson())));
        Expect("a custom color survives being sent", customRound.color == pink);

        DuetPresence round = DuetPresence.FromJson((Dictionary<string, object>)DuetJson.Read(DuetJson.Write(robert.ToJson())));
        Expect("presence survives being written and read", DuetJson.Write(round.ToJson()) == DuetJson.Write(robert.ToJson()));

        int rainbow = DuetPresence.MakeRainbow(7);
        Expect("a rainbow keeps its speed", DuetPresence.IsRainbow(rainbow) && DuetPresence.RainbowSpeed(rainbow) == 7 && DuetPresence.IsValidColor(rainbow)
            && !DuetPresence.IsCustomColor(rainbow) && DuetPresence.GetColorName(rainbow) == "Rainbow");
        Expect("a nonsense rainbow isn't valid", !DuetPresence.IsValidColor(DuetPresence.RainbowFlag) && !DuetPresence.IsValidColor(DuetPresence.RainbowFlag | 99)
            && !DuetPresence.IsValidColor(DuetPresence.RainbowFlag | DuetPresence.CustomColorFlag | 3));
        UnityEngine.Color first = DuetPresence.GetColor(DuetPresence.MakeRainbow(DuetPresence.MaxRainbowSpeed));
        System.Threading.Thread.Sleep(250);
        UnityEngine.Color later = DuetPresence.GetColor(DuetPresence.MakeRainbow(DuetPresence.MaxRainbowSpeed));
        Expect("a rainbow changes color over time", first.r != later.r || first.g != later.g || first.b != later.b);
        DuetPresence rainbowRound = DuetPresence.FromJson((Dictionary<string, object>)DuetJson.Read(DuetJson.Write(new DuetPresence { color = rainbow }.ToJson())));
        Expect("a rainbow survives being sent", rainbowRound.color == rainbow);

        // A selection from someone else is checked: too many objects, or nonsense, is cut out
        List<object> flood = new List<object>();
        for (int i = 0; i < DuetSelection.MaxItems + 100; ++i)
        {
            flood.Add((double)(int)SongObject.ID.Note);
            flood.Add((double)i);
            flood.Add((double)1);
        }
        flood.Add(99.0); flood.Add(5.0); flood.Add(0.0);    // No such kind of object
        DuetSelection flooded = DuetSelection.FromJson(new Dictionary<string, object> { { "type", "selection" }, { "items", flood } });
        Expect("a selection is capped", flooded.items.Count == DuetSelection.MaxItems);
        DuetSelection odd = DuetSelection.FromJson(new Dictionary<string, object> { { "type", "selection" }, { "items", new List<object> { 99.0, 5.0, 0.0, 5.0, "x", 1.0, 5.0, 10.0, -3.0, 5.0, 20.0, 1.0 } } });
        Expect("nonsense in a selection is skipped", odd.items.Count == 1 && odd.items[0].tick == 20);
        DuetPresence badDrag = DuetPresence.FromJson(new Dictionary<string, object> { { "type", "presence" }, { "drag", new List<object> { double.NaN, 1.0, 2.0, 3.0, false } } });
        Expect("a nonsense drag box is ignored", !badDrag.hasDrag);

        return s_failures;
    }

    static void Expect(string label, bool condition)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
        if (!condition)
            ++s_failures;
    }
}
