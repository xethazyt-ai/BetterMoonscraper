// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;

/// <summary>
/// Finds notes a few ticks off the step they were meant to be on (from a bad conversion, a resolution change or a
/// drag) and where they belong. Each beat (beatline to beatline) is checked on its own: the plainest step that every
/// note in it is close to is taken as the step it was charted at, and notes not exactly on it are off. A beat that
/// mixes two steps is checked a half beat at a time instead. Notes that fit no step closely are left alone.
/// </summary>
public static class NoteGridCheck
{
    // Ways a beat is split, plainest first: 1 = quarter notes in x/4 ... 24 = 1/96 notes
    static readonly int[] s_splits = { 1, 2, 3, 4, 6, 8, 12, 16, 24 };
    const double CloseShare = 0.2;      // A note within a fifth of a step's gap is meant for that step
    const double OnTicks = 1;           // How far a step between two ticks can be from either

    public struct Line
    {
        public uint tick;           // Where this time signature starts
        public double beatTicks;    // Ticks from one beatline to the next under it
    }

    public class Fix
    {
        public uint from;
        public uint to;
        public int step;            // The step it belongs on, as Moonscraper counts them (16 = 1/16)
    }

    /// <summary>Where each note tick belongs, for every tick that's off. 'ticks' must be sorted and distinct.</summary>
    public static List<Fix> Find(IList<uint> ticks, IList<Line> lines, float resolution)
    {
        List<Fix> fixes = new List<Fix>();
        int i = 0;
        while (i < ticks.Count)
        {
            double start, beat;
            BeatAt(ticks[i], lines, out start, out beat);
            double end = start + beat;

            int j = i;
            while (j < ticks.Count && ticks[j] < end - 0.5)
                ++j;

            List<uint> inBeat = new List<uint>();
            for (int k = i; k < j; ++k)
                inBeat.Add(ticks[k]);

            if (!CheckSpan(inBeat, start, beat, resolution, fixes))
            {
                // Two steps in one beat (a triplet then 16ths, say): each half on its own
                double half = beat / 2;
                List<uint> first = new List<uint>(), second = new List<uint>();
                foreach (uint tick in inBeat)
                    (tick < start + half - 0.5 ? first : second).Add(tick);
                CheckSpan(first, start, half, resolution, fixes);
                CheckSpan(second, start + half, half, resolution, fixes);
            }
            i = Math.Max(j, i + 1);
        }
        return fixes;
    }

    // False when no step fits every note in the span
    static bool CheckSpan(List<uint> ticks, double start, double length, float resolution, List<Fix> fixes)
    {
        if (ticks.Count == 0)
            return true;

        foreach (int split in s_splits)
        {
            double gap = length / split;
            bool fits = true;
            foreach (uint tick in ticks)
            {
                double offset = tick - start;
                double off = Math.Abs(offset - Math.Round(offset / gap) * gap);
                if (off > Math.Max(OnTicks, gap * CloseShare))
                {
                    fits = false;
                    break;
                }
            }
            if (!fits)
                continue;

            int step = (int)Math.Round(resolution * 4 / gap);
            foreach (uint tick in ticks)
            {
                double offset = tick - start;
                double onStep = start + Math.Round(offset / gap) * gap;
                if (!IsOn(tick, onStep))
                    fixes.Add(new Fix { from = tick, to = (uint)Math.Max(0, Math.Round(onStep)), step = step });
            }
            return true;
        }
        return false;
    }

    // Exactly on the step's tick; a step that falls between ticks (1/64 at 480 resolution, say) takes either neighbour
    static bool IsOn(uint tick, double onStep)
    {
        if (Math.Abs(onStep - Math.Round(onStep)) < 0.01)
            return tick == (uint)Math.Round(onStep);
        return Math.Abs(tick - onStep) < OnTicks;
    }

    // The beat (from its line, and how long) that a tick is in
    static void BeatAt(uint tick, IList<Line> lines, out double start, out double beat)
    {
        Line line = lines.Count > 0 ? lines[0] : new Line { tick = 0, beatTicks = 192 };
        double limit = double.MaxValue;
        for (int i = 0; i < lines.Count; ++i)
        {
            if (lines[i].tick <= tick)
            {
                line = lines[i];
                limit = i + 1 < lines.Count ? lines[i + 1].tick : double.MaxValue;
            }
        }
        beat = Math.Max(1, line.beatTicks);
        start = line.tick + Math.Floor((tick - line.tick) / beat) * beat;
        if (start + beat > limit)
            beat = limit - start;    // The last beat before a time signature change ends at it
    }
}
