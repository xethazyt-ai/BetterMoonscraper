// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;

/// <summary>
/// Finds notes a few ticks off the step they were meant to be on (from a bad conversion, a resolution change or a
/// drag) and where they belong. Each beat (beatline to beatline) is checked on its own: the plainest step that every
/// note in it is close to is taken as the step it was charted at, and notes not exactly on it are off. A beat that
/// mixes two steps is checked a half beat at a time instead. When no step fits every note, the plainest step that
/// at least three in four notes sit on is used, and the one or two others are moved to the nearest free spot on it,
/// or removed as extra notes when both spots beside them are taken. In a stream of single notes, a two-note chord next
/// to a hole in the stream is split first, one note going into the hole.
/// </summary>
public static class NoteGridCheck
{
    // Ways a beat is split, plainest first: 1 = quarter notes in x/4 ... 24 = 1/96 notes
    static readonly int[] s_splits = { 1, 2, 3, 4, 6, 8, 12, 16, 24 };
    const double CloseShare = 0.2;      // A note within a fifth of a step's gap is meant for that step
    const double OnTicks = 1;           // How far a step between two ticks can be from either
    const double MostShare = 0.75;      // Share of notes on a step for it to win when not every note fits
    const int MostOdd = 2;              // More odd ones out than this is a pattern (a 1/96 burst), not strays
    const int MostMinNotes = 4;         // Too few notes to tell which are the odd ones out
    const double StreamShare = 0.25;    // At most this share of a span's notes are chords for it to be a stream

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
        public int fret = -1;       // Only this note of the chord (-1 = every note at 'from')
        public bool chordSplit;     // A chord in a stream of single notes, pulled apart
        public bool remove;         // An extra note with no free spot on the step: deleted
    }

    /// <summary>
    /// Where each note tick belongs, for every tick that's off. 'ticks' must be sorted and distinct. 'frets' (optional)
    /// gives the 5-fret notes at each tick (0 green to 4 orange) for splitting stray chords in streams.
    /// </summary>
    public static List<Fix> Find(IList<uint> ticks, IList<Line> lines, float resolution, IDictionary<uint, List<int>> frets = null)
    {
        List<Fix> fixes = new List<Fix>();
        HashSet<uint> taken = new HashSet<uint>(ticks);
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

            Span whole = new Span(inBeat, start, beat);
            double half = beat / 2;
            List<uint> first = new List<uint>(), second = new List<uint>();
            foreach (uint tick in inBeat)
                (tick < start + half - 0.5 ? first : second).Add(tick);
            Span firstHalf = new Span(first, start, half), secondHalf = new Span(second, start + half, half);

            // Every note on one step, else each half on its own (a triplet then 16ths, say), else most notes on one
            // step, else most notes in each half. Halves that only fit at a finer step than most of the beat sits on
            // lose to the beat's step: 1/192 fits nearly anything in a half beat.
            if (whole.Fit(true))
                whole.Plan(resolution, taken, frets, fixes);
            else
            {
                bool halves = firstHalf.Fit(true) && secondHalf.Fit(true);
                double halvesGap = Math.Min(firstHalf.gap > 0 ? firstHalf.gap : double.MaxValue, secondHalf.gap > 0 ? secondHalf.gap : double.MaxValue);
                bool most = whole.Fit(false);
                if (halves && (!most || halvesGap >= whole.gap - 0.01))
                {
                    firstHalf.Plan(resolution, taken, frets, fixes);
                    secondHalf.Plan(resolution, taken, frets, fixes);
                }
                else if (most)
                    whole.Plan(resolution, taken, frets, fixes);
                else
                {
                    if (firstHalf.Fit(true) || firstHalf.Fit(false))
                        firstHalf.Plan(resolution, taken, frets, fixes);
                    if (secondHalf.Fit(true) || secondHalf.Fit(false))
                        secondHalf.Plan(resolution, taken, frets, fixes);
                }
            }
            i = Math.Max(j, i + 1);
        }
        return fixes;
    }

    class Span
    {
        readonly List<uint> m_ticks;
        readonly double m_start, m_length;
        public double gap;          // The step's gap once one fits, else 0

        public Span(List<uint> ticks, double start, double length)
        {
            m_ticks = ticks;
            m_start = start;
            m_length = length;
        }

        // Picks the plainest step every note is close to (all), or that most notes are close to
        public bool Fit(bool all)
        {
            gap = 0;
            if (m_ticks.Count == 0)
                return all;
            if (!all && m_ticks.Count < MostMinNotes)
                return false;

            foreach (int split in s_splits)
            {
                double g = m_length / split;
                int close = 0, on = 0;
                foreach (uint tick in m_ticks)
                {
                    if (Off(tick, g) <= Math.Max(OnTicks, g * CloseShare))
                        ++close;
                    if (IsOn(tick, Slot(tick - m_start, g)))
                        ++on;
                }
                // Most notes must be exactly on the step, so a fine step can't claim notes spread evenly between
                // its lines (20 to a beat, say)
                if (all ? close == m_ticks.Count : on >= m_ticks.Count * MostShare && m_ticks.Count - close <= MostOdd)
                {
                    gap = g;
                    return true;
                }
            }
            return false;
        }

        double Off(uint tick, double g)
        {
            double offset = tick - m_start;
            return Math.Abs(offset - Math.Round(offset / g) * g);
        }

        double Slot(double offset, double g) { return m_start + Math.Round(offset / g) * g; }

        // Adds the moves that put this span's notes on its step. 'taken' holds every tick a note is at (kept up to date)
        public void Plan(float resolution, HashSet<uint> taken, IDictionary<uint, List<int>> frets, List<Fix> fixes)
        {
            if (gap <= 0 || m_ticks.Count == 0)
                return;

            int step = (int)Math.Round(resolution * 4 / gap);
            List<uint> final = new List<uint>();
            List<uint> strays = new List<uint>();
            foreach (uint tick in m_ticks)
            {
                if (Off(tick, gap) > Math.Max(OnTicks, gap * CloseShare))
                {
                    strays.Add(tick);
                    continue;
                }
                uint to = tick;
                double onStep = Slot(tick - m_start, gap);
                if (!IsOn(tick, onStep))
                    to = (uint)Math.Max(0, Math.Round(onStep));
                final.Add(Move(tick, to, step, taken, frets, fixes) ? to : tick);
            }

            // Chords first, so a note split off a chord gets its spot ahead of a stray
            if (frets != null)
                SplitChords(final, step, taken, frets, fixes);

            // An odd one out goes to the nearest spot on the step with no note on it; with both taken it's an
            // extra note and is removed
            foreach (uint tick in strays)
            {
                double offset = tick - m_start;
                double below = m_start + Math.Floor(offset / gap) * gap, above = below + gap;
                double near = tick - below <= above - tick ? below : above, far = near == below ? above : below;
                bool moved = false;
                foreach (double spot in new[] { near, far })
                {
                    uint t = (uint)Math.Max(0, Math.Round(spot));
                    if (!taken.Contains(t) && Move(tick, t, step, taken, frets, fixes))
                    {
                        moved = true;
                        break;
                    }
                }
                if (!moved)
                {
                    fixes.Add(new Fix { from = tick, to = tick, step = step, remove = true });
                    taken.Remove(tick);
                    if (frets != null)
                        frets.Remove(tick);
                }
            }
        }

        static bool Move(uint tick, uint to, int step, HashSet<uint> taken, IDictionary<uint, List<int>> frets, List<Fix> fixes)
        {
            if (to == tick || taken.Contains(to))
                return false;
            fixes.Add(new Fix { from = tick, to = to, step = step });
            taken.Remove(tick);
            taken.Add(to);
            if (frets != null && frets.ContainsKey(tick))
            {
                frets[to] = frets[tick];
                frets.Remove(tick);
            }
            return true;
        }

        // In a stream of single notes, a two-note chord beside a hole in the stream was meant as two notes in a row
        void SplitChords(List<uint> final, int step, HashSet<uint> taken, IDictionary<uint, List<int>> frets, List<Fix> fixes)
        {
            int chords = 0;
            foreach (uint tick in final)
            {
                List<int> at;
                if (frets.TryGetValue(tick, out at) && at.Count > 1)
                    ++chords;
            }
            if (final.Count < MostMinNotes || chords == 0 || chords > final.Count * StreamShare)
                return;

            double end = m_start + m_length;
            foreach (uint tick in final)
            {
                List<int> at;
                if (!frets.TryGetValue(tick, out at) || at.Count != 2 || at[0] > 4 || at[1] > 4)
                    continue;

                uint? after = SpotAt(tick + gap, end - 1), afterNext = SpotAt(tick + 2 * gap, end + gap);
                uint? before = tick >= gap ? SpotAt(tick - gap, end) : null;
                uint? beforePrev = tick >= 2 * gap ? SpotAt(tick - 2 * gap, end) : null;
                bool holeAfter = after.HasValue && !taken.Contains(after.Value) && afterNext.HasValue && taken.Contains(afterNext.Value)
                    && before.HasValue && taken.Contains(before.Value);
                bool holeBefore = before.HasValue && before.Value >= m_start - 0.5 && !taken.Contains(before.Value)
                    && beforePrev.HasValue && taken.Contains(beforePrev.Value) && after.HasValue && taken.Contains(after.Value);
                if (holeAfter == holeBefore)
                    continue;   // No hole, or one each side and no telling which

                // The lower fret goes later (G+Y in a stream is Y then G), unless that repeats the note past the hole
                uint hole = holeAfter ? after.Value : before.Value;
                List<int> beyond;
                frets.TryGetValue(holeAfter ? afterNext.Value : beforePrev.Value, out beyond);
                int low = Math.Min(at[0], at[1]), high = Math.Max(at[0], at[1]);
                int move = holeAfter ? low : high;
                if (beyond != null && beyond.Count == 1)
                {
                    if (beyond[0] == move)
                        move = move == high ? low : high;
                }

                fixes.Add(new Fix { from = tick, to = hole, step = step, fret = move, chordSplit = true });
                taken.Add(hole);
                at.Remove(move);
                frets[hole] = new List<int> { move };
            }
        }

        // A spot on the step, or null past 'limit'
        uint? SpotAt(double tick, double limit)
        {
            if (tick > limit + 0.5)
                return null;
            return (uint)Math.Max(0, Math.Round(Slot(tick - m_start, gap)));
        }
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
