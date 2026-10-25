// See LICENSE in project root for license information.

// Checks that exporting a copy of the song at another speed retimes the copy and leaves the open song alone.

using System;
using MoonscraperChartEditor.Song;

public static class SpeedTests
{
    static int s_failures = 0;

    public static int Run()
    {
        Song song = new Song();
        song.offset = 1;
        song.Add(new BPM(song.resolution > 0 ? (uint)(song.resolution * 4) : 768, 150000, 3f));
        song.metaData.previewStart = 30;
        double originalEnd = song.TickToTime((uint)(song.resolution * 8), song.resolution);

        Song slowed = new Song(song);
        slowed.ScaleSpeed(0.5f);

        Expect("the copy's BPMs are halved", slowed.bpms[0].value == 60000 && slowed.bpms[1].value == 75000);
        Expect("the copy's anchors, offset and preview are twice as late", slowed.bpms[1].anchor == 6f && slowed.offset == 2 && slowed.metaData.previewStart == 60);
        Expect("a beat in the copy takes twice as long", Math.Abs(slowed.TickToTime((uint)(song.resolution * 8), song.resolution) - originalEnd * 2) < 0.001);
        Expect("the open song is untouched", song.bpms[0].value == 120000 && song.bpms[1].value == 150000 && song.bpms[1].anchor == 3f && song.offset == 1
            && song.metaData.previewStart == 30 && song.bpms[0].song == song);

        return s_failures;
    }

    static void Expect(string what, bool ok)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
        if (!ok)
            ++s_failures;
    }
}
