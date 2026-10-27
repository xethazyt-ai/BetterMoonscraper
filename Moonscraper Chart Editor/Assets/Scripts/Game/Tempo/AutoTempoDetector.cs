// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;

/// <summary>
/// Works out a song's tempo and where its first downbeat is, from the audio alone, for Auto Tempo Map.
///
/// How: the audio is turned into an "onset" curve (how much new sound starts at each moment), once for the whole mix
/// and once for the low end where kick drums are. The tempo is the beat length that, when the whole song is folded
/// over it, piles the onsets up into the sharpest peak. A wrong tempo smears them out, so over a whole song this
/// pins the tempo down to a thousandth of a BPM. The downbeat is whichever of the beats in a measure has the most
/// kick, and the first one after the music starts is the first measure.
///
/// It never pretends to be sure: every measure is checked against the audio afterwards, and measures where the
/// beats don't line up with what's heard (a tempo change, a free-time intro) are listed so they can be checked.
/// No Unity in here, so it can be tested on its own.
/// </summary>
public class AutoTempoDetector
{
    public class Candidate
    {
        public double bpm;
        public double score;        // How sharply the song lines up at this tempo; higher is better
        public double firstBeat;    // Seconds into the audio of a beat at this tempo
        public bool isWhole;        // Snapped to a whole (or half) BPM, as songs made in a DAW usually are
    }

    public class Result
    {
        public List<Candidate> candidates = new List<Candidate>();     // Best first; the others are half, double and so on
        public double musicStart;   // Seconds into the audio where the music starts
    }

    public class Check
    {
        public double firstDownbeat;                        // Seconds into the audio
        public List<double> unsureMeasures = new List<double>();   // Start times of measures that don't line up
        public int measuresChecked;
    }

    const int TargetRate = 11025;
    const int Window = 512;
    const int Hop = 64;             // About 5.8 ms
    const double MinBpm = 50, MaxBpm = 320;
    const double KickMaxHz = 160;
    const double FluxLatencySeconds = 0.0105;

    float[] m_onsets;               // Whole mix
    float[] m_kicks;                // Low end only
    double m_hopSeconds;
    double m_frameOffset;           // Seconds from a frame's index time to the moment its onset belongs to

    public double duration { get; private set; }

    /// <summary>Mono samples of the song (any rate).</summary>
    public AutoTempoDetector(float[] samples, int sampleRate)
    {
        int factor = Math.Max(1, (int)Math.Round(sampleRate / (double)TargetRate));
        int rate = sampleRate / factor;
        float[] audio = Downsample(samples, factor);
        duration = samples.Length / (double)sampleRate;
        m_hopSeconds = Hop / (double)rate;
        // A hit shows up most strongly a little before the window's middle reaches it (measured on test drums)
        m_frameOffset = Window / 2.0 / rate + FluxLatencySeconds;
        BuildOnsets(audio, rate);
    }

    // Averages each group of samples: a rough low-pass that keeps drums and loses only the highest cymbal sizzle
    static float[] Downsample(float[] samples, int factor)
    {
        if (factor == 1)
            return samples;
        float[] result = new float[samples.Length / factor];
        for (int i = 0; i < result.Length; ++i)
        {
            float sum = 0;
            int start = i * factor;
            for (int j = 0; j < factor; ++j)
                sum += samples[start + j];
            result[i] = sum / factor;
        }
        return result;
    }

    void BuildOnsets(float[] audio, int rate)
    {
        int frames = Math.Max(0, (audio.Length - Window) / Hop);
        m_onsets = new float[frames];
        m_kicks = new float[frames];

        float[] hann = new float[Window];
        for (int i = 0; i < Window; ++i)
            hann[i] = (float)(0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Window - 1)));

        int bins = Window / 2;
        int kickBins = Math.Max(2, (int)(KickMaxHz * Window / rate));
        float[] previous = new float[bins];
        float[] re = new float[Window], im = new float[Window];

        for (int f = 0; f < frames; ++f)
        {
            int start = f * Hop;
            for (int i = 0; i < Window; ++i)
            {
                re[i] = audio[start + i] * hann[i];
                im[i] = 0;
            }
            Fft(re, im);

            float flux = 0, kick = 0;
            for (int b = 1; b < bins; ++b)
            {
                float magnitude = (float)Math.Log(1 + 100 * Math.Sqrt(re[b] * re[b] + im[b] * im[b]));
                float rise = magnitude - previous[b];
                if (rise > 0)
                {
                    flux += rise;
                    if (b <= kickBins)
                        kick += rise;
                }
                previous[b] = magnitude;
            }
            m_onsets[f] = flux;
            m_kicks[f] = kick;
        }

        // Only what stands out from the moment around it counts, so loud sections don't outweigh quiet ones
        Whiten(m_onsets, (int)(0.4 / m_hopSeconds));
        Whiten(m_kicks, (int)(0.4 / m_hopSeconds));
    }

    static void Whiten(float[] curve, int radius)
    {
        if (curve.Length == 0)
            return;

        double[] sums = new double[curve.Length + 1];
        for (int i = 0; i < curve.Length; ++i)
            sums[i + 1] = sums[i] + curve[i];

        float[] result = new float[curve.Length];
        for (int i = 0; i < curve.Length; ++i)
        {
            int a = Math.Max(0, i - radius), b = Math.Min(curve.Length, i + radius + 1);
            double mean = (sums[b] - sums[a]) / (b - a);
            result[i] = (float)Math.Max(0, curve[i] - mean);
        }

        // Scaled so a typical onset is about 1, whatever the song's loudness
        double total = 0;
        int counted = 0;
        foreach (float value in result)
        {
            if (value > 0)
            {
                total += value;
                ++counted;
            }
        }
        float scale = counted > 0 && total > 0 ? (float)(counted / total) : 1;
        for (int i = 0; i < result.Length; ++i)
            curve[i] = result[i] * scale;
    }

    // In-place radix-2 FFT
    static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; ++i)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                float t = re[i]; re[i] = re[j]; re[j] = t;
                t = im[i]; im[i] = im[j]; im[j] = t;
            }
        }
        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            float wRe = (float)Math.Cos(angle), wIm = (float)Math.Sin(angle);
            for (int i = 0; i < n; i += length)
            {
                float curRe = 1, curIm = 0;
                for (int j = 0; j < length / 2; ++j)
                {
                    int a = i + j, b = i + j + length / 2;
                    float tRe = re[b] * curRe - im[b] * curIm;
                    float tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe; im[b] = im[a] - tIm;
                    re[a] += tRe; im[a] += tIm;
                    float next = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = next;
                }
            }
        }
    }

    double FrameTime(int frame) { return frame * m_hopSeconds + m_frameOffset; }

    /// <summary>
    /// The song's tempo, best guess first, with the half, double and other related tempos it could be mistaken for.
    /// Only the part from 'from' to 'to' (seconds) is used; pass 0 and the duration for the whole song.
    /// </summary>
    public Result Detect(double from, double to)
    {
        Result result = new Result();
        result.musicStart = FindMusicStart(from);
        int first = Math.Max(0, (int)((Math.Max(from, result.musicStart) - m_frameOffset) / m_hopSeconds));
        int last = Math.Min(m_onsets.Length, (int)((to - m_frameOffset) / m_hopSeconds));
        if (last - first < (int)(8 / m_hopSeconds))
            return result;      // Too short to be sure of anything

        // Rough tempos from how the onset curve repeats
        List<double> rough = AutocorrelationPeaks(first, last);

        // Each one, its half, double and 3:2 relations, pinned down exactly
        List<double> tried = new List<double>();
        foreach (double bpm in rough)
        {
            foreach (double related in new[] { bpm, bpm / 2, bpm * 2, bpm * 2 / 3, bpm * 3 / 2 })
            {
                if (related < MinBpm || related > MaxBpm || tried.Exists(t => Math.Abs(t - related) / related < 0.02))
                    continue;
                tried.Add(related);
                result.candidates.Add(Refine(related, first, last));
            }
        }

        result.candidates.Sort((a, b) => b.score.CompareTo(a.score));
        PreferBeatOverHalf(result.candidates, first, last);
        // Duplicates after refining
        for (int i = result.candidates.Count - 1; i > 0; --i)
        {
            for (int j = 0; j < i; ++j)
            {
                if (Math.Abs(result.candidates[i].bpm - result.candidates[j].bpm) < 0.01)
                {
                    result.candidates.RemoveAt(i);
                    break;
                }
            }
        }
        if (result.candidates.Count > 5)
            result.candidates.RemoveRange(5, result.candidates.Count - 5);
        return result;
    }

    // The first moment with real sound: the first onset as strong as a typical one
    double FindMusicStart(double from)
    {
        int start = Math.Max(0, (int)((from - m_frameOffset) / m_hopSeconds));
        for (int i = start; i < m_onsets.Length; ++i)
        {
            if (m_onsets[i] > 1.5f)
                return Math.Max(0, FrameTime(i) - 0.02);
        }
        return from;
    }

    List<double> AutocorrelationPeaks(int first, int last)
    {
        int minLag = (int)(60 / MaxBpm / m_hopSeconds), maxLag = (int)(60 / MinBpm / m_hopSeconds) + 1;
        double[] correlation = new double[maxLag + 1];
        for (int lag = minLag; lag <= maxLag; ++lag)
        {
            double sum = 0;
            for (int i = first; i + lag < last; ++i)
                sum += m_onsets[i] * m_onsets[i + lag];
            correlation[lag] = sum / (last - first - lag);
        }

        List<KeyValuePair<double, double>> peaks = new List<KeyValuePair<double, double>>();
        for (int lag = minLag + 1; lag < maxLag; ++lag)
        {
            if (correlation[lag] > correlation[lag - 1] && correlation[lag] >= correlation[lag + 1])
            {
                // Between frames, by fitting a curve through the three points
                double a = correlation[lag - 1], b = correlation[lag], c = correlation[lag + 1];
                double denominator = a - 2 * b + c;
                double exact = lag + (denominator != 0 ? 0.5 * (a - c) / denominator : 0);
                peaks.Add(new KeyValuePair<double, double>(60 / (exact * m_hopSeconds), b));
            }
        }
        peaks.Sort((x, y) => y.Value.CompareTo(x.Value));

        List<double> bpms = new List<double>();
        for (int i = 0; i < peaks.Count && bpms.Count < 4; ++i)
            bpms.Add(peaks[i].Key);
        return bpms;
    }

    /// <summary>
    /// How sharply the onsets pile up when the song is folded over one beat at this tempo, and where the pile is.
    /// The peak's height over the average of the fold: a wrong tempo smears the pile out across the whole beat.
    /// </summary>
    double Sharpness(double bpm, int first, int last, out double phase)
    {
        double period = 60 / bpm;
        const double BinSeconds = 0.002;
        int bins = Math.Max(8, (int)(period / BinSeconds));
        double[] fold = new double[bins];
        double binScale = bins / period;

        for (int i = first; i < last; ++i)
        {
            float value = m_onsets[i];
            if (value <= 0)
                continue;
            double position = FrameTime(i) % period * binScale;
            int bin = (int)position;
            double part = position - bin;
            fold[bin % bins] += value * (1 - part);
            fold[(bin + 1) % bins] += value * part;
        }

        // A little smoothing, so one lucky bin doesn't win
        double best = 0, total = 0;
        int bestBin = 0;
        for (int b = 0; b < bins; ++b)
        {
            double smoothed = fold[(b + bins - 1) % bins] + fold[b] * 2 + fold[(b + 1) % bins];
            total += fold[b];
            if (smoothed > best)
            {
                best = smoothed;
                bestBin = b;
            }
        }
        phase = (bestBin + 0.5) / binScale;
        return total > 0 ? best / 4 / (total / bins) : 0;
    }

    Candidate Refine(double bpm, int first, int last)
    {
        double phase;
        double best = bpm, bestScore = -1;

        // Coarse, then fine, then finer
        foreach (double[] pass in new[] { new[] { 0.02, 0.02 }, new[] { 0.002, 0.0025 }, new[] { 0.0002, 0.00025 } })
        {
            double span = best * pass[0], step = best * pass[1] / 10;
            double centre = best;
            for (double candidate = centre - span; candidate <= centre + span; candidate += step)
            {
                double score = Sharpness(candidate, first, last, out phase);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
        }

        // Songs made in a DAW sit on a whole or half BPM; take that when it lines up as well as the exact figure
        Candidate result = new Candidate { bpm = best, score = bestScore };
        foreach (double whole in new[] { Math.Round(best), Math.Round(best * 2) / 2 })
        {
            if (Math.Abs(whole - best) > 0.6)
                continue;
            double wholeScore = Sharpness(whole, first, last, out phase);
            if (wholeScore >= bestScore * 0.97)
            {
                result.bpm = whole;
                result.score = wholeScore;
                result.isWhole = true;
                break;
            }
        }

        Sharpness(result.bpm, first, last, out phase);
        result.firstBeat = phase;
        return result;
    }

    // Folding at half the real tempo also piles everything up (every other beat lands on the off-beat bin). If the
    // off-beat of a slower candidate is about as strong as its beat, the faster one is the real beat.
    void PreferBeatOverHalf(List<Candidate> candidates, int first, int last)
    {
        // Repeats, so a fold at a quarter of the tempo climbs all the way up
        for (bool moved = true; moved && candidates.Count > 1; )
        {
            moved = false;
            Candidate top = candidates[0];
            double beat = Strength(top.bpm, top.firstBeat, 0, first, last);
            double offBeat = Strength(top.bpm, top.firstBeat, 0.5, first, last);
            // A strong off-beat means the faster tempo is the beat. Slow tempos need less to tip them, since songs
            // under 100 BPM are usually charted at double.
            if (offBeat <= beat * (top.bpm < 100 ? 0.3 : 0.5))
                return;

            foreach (Candidate other in candidates)
            {
                if (Math.Abs(other.bpm / top.bpm - 2) < 0.01 && other.bpm <= 250)
                {
                    candidates.Remove(other);
                    candidates.Insert(0, other);
                    moved = true;
                    break;
                }
            }
        }
    }

    // Average onset at a point in every beat (0 = on the beat, 0.5 = halfway to the next)
    double Strength(double bpm, double phase, double fraction, int first, int last)
    {
        double period = 60 / bpm;
        double total = 0;
        int count = 0;
        for (double t = phase + fraction * period; t < FrameTime(last); t += period)
        {
            if (t < FrameTime(first))
                continue;
            total += PeakNear(m_onsets, t, 0.012);
            ++count;
        }
        return count > 0 ? total / count : 0;
    }

    float PeakNear(float[] curve, double seconds, double radius)
    {
        int a = Math.Max(0, (int)((seconds - radius - m_frameOffset) / m_hopSeconds));
        int b = Math.Min(curve.Length - 1, (int)((seconds + radius - m_frameOffset) / m_hopSeconds) + 1);
        float best = 0;
        for (int i = a; i <= b; ++i)
            best = Math.Max(best, curve[i]);
        return best;
    }

    public class Segment
    {
        public double downbeat;     // Seconds into the audio of the measure this tempo starts on
        public double bpm;
    }

    public class Map
    {
        public List<Candidate> choices = new List<Candidate>();     // What the main tempo could be, best first
        public List<Segment> segments = new List<Segment>();         // The main tempo, then any changes
        public List<double> unsureMeasures = new List<double>();    // Start times (audio) of measures to check by ear
        public int measuresChecked;
        public double musicStart;
    }

    const int ChangeRunMeasures = 4;     // This many unsure measures within ChangeWindowMeasures means the tempo changed
    const int ChangeWindowMeasures = 6;
    const int MaxSegments = 12;

    /// <summary>
    /// The whole song's tempo map: the main tempo (or 'chosenBpm' when one of the choices was picked), where the first
    /// measure is, and any later tempo changes, found where several measures in a row stop lining up.
    /// </summary>
    public Map MapSong(int beatsPerMeasure, double chosenBpm)
    {
        Map map = new Map();
        Result detected = Detect(0, duration);
        map.choices = detected.candidates;
        map.musicStart = detected.musicStart;
        if (detected.candidates.Count == 0)
            return map;

        Candidate tempo = detected.candidates[0];
        if (chosenBpm > 0)
        {
            foreach (Candidate candidate in detected.candidates)
            {
                if (Math.Abs(candidate.bpm - chosenBpm) < Math.Abs(tempo.bpm - chosenBpm))
                    tempo = candidate;
            }
        }

        double from = detected.musicStart;
        for (int segment = 0; segment < MaxSegments; ++segment)
        {
            Check check = CheckMeasures(tempo, from, beatsPerMeasure, duration);
            if (map.segments.Count > 0)
            {
                Segment previous = map.segments[map.segments.Count - 1];
                check.firstDownbeat = MoveChangeBack(previous, tempo.bpm, check.firstDownbeat, beatsPerMeasure);
                if (check.firstDownbeat <= previous.downbeat + 0.05)
                    break;
                // Measures from the new start that the old tempo flagged but the new one covers
                map.unsureMeasures.RemoveAll(t => t >= check.firstDownbeat);
            }
            map.segments.Add(new Segment { downbeat = check.firstDownbeat, bpm = tempo.bpm });
            map.measuresChecked += check.measuresChecked;

            // Where unsure measures bunch up (most of a few measures in a row) the tempo has changed
            double measure = 60 / tempo.bpm * beatsPerMeasure;
            int runStart = -1;
            for (int i = 0; i + ChangeRunMeasures <= check.unsureMeasures.Count && runStart < 0; ++i)
            {
                double span = check.unsureMeasures[i + ChangeRunMeasures - 1] - check.unsureMeasures[i];
                if (span < measure * (ChangeWindowMeasures - 0.5))
                    runStart = i;
            }

            if (runStart < 0)
            {
                map.unsureMeasures.AddRange(check.unsureMeasures);
                break;
            }
            for (int i = 0; i < runStart; ++i)
                map.unsureMeasures.Add(check.unsureMeasures[i]);

            // The new tempo, from the change on, close to the old one (a change, not a half or double)
            double changeAt = check.unsureMeasures[runStart];
            Result next = Detect(changeAt, duration);
            Candidate changed = null;
            foreach (Candidate candidate in next.candidates)
            {
                double ratio = candidate.bpm / tempo.bpm;
                if (ratio > 0.67 && ratio < 1.5 && (changed == null || candidate.score > changed.score))
                    changed = candidate;
            }
            if (changed == null)
            {
                // Can't tell what it changes to, so leave the rest to be checked by ear
                for (int i = runStart; i < check.unsureMeasures.Count; ++i)
                    map.unsureMeasures.Add(check.unsureMeasures[i]);
                break;
            }

            tempo = changed;
            from = changeAt - 60 / tempo.bpm * 0.5;
        }
        return map;
    }

    // A change is found a few measures late (it takes some measures to drift off), so walk the new tempo's measures
    // back for as long as they fit the audio better than the old tempo's beats there
    double MoveChangeBack(Segment previous, double bpm, double downbeat, int beatsPerMeasure)
    {
        double oldPeriod = 60 / previous.bpm, newPeriod = 60 / bpm;
        double newMeasure = newPeriod * beatsPerMeasure;
        while (downbeat - newMeasure > previous.downbeat + oldPeriod * beatsPerMeasure)
        {
            double start = downbeat - newMeasure;
            double fitNew = 0, fitOld = 0;
            for (int i = 0; i < beatsPerMeasure; ++i)
                fitNew += PeakNear(m_onsets, start + i * newPeriod, 0.012);
            // The old tempo's beats in the same stretch of time
            double oldBeat = previous.downbeat + Math.Ceiling((start - previous.downbeat) / oldPeriod - 1e-6) * oldPeriod;
            int oldCount = 0;
            for (double t = oldBeat; t < start + newMeasure - 0.001; t += oldPeriod, ++oldCount)
                fitOld += PeakNear(m_onsets, t, 0.012);
            fitNew /= beatsPerMeasure;
            fitOld /= Math.Max(1, oldCount);
            if (fitNew <= fitOld * 1.15)
                break;
            downbeat = start;
        }
        return downbeat;
    }

    public class Marker
    {
        public int measure;     // Counting from the chart's start
        public double bpm;
    }

    /// <summary>
    /// The BPM markers for a map, the way charters place them: one at the very start sized so the first measure
    /// line lands on the first downbeat, the song's tempo from there, and at each change a measure sized to land the
    /// next downbeat on a measure line. 'chartStart' is where the chart's 0 is in the audio (the song's offset).
    /// </summary>
    public static List<Marker> ToMarkers(List<Segment> segments, double chartStart, int beatsPerMeasure)
    {
        List<Marker> markers = new List<Marker>();
        if (segments.Count == 0)
            return markers;

        double firstMeasure = 60 / segments[0].bpm * beatsPerMeasure;
        double lead = segments[0].downbeat - chartStart;
        int measure = 0;
        if (lead > 0.01)
        {
            int count = Math.Max(1, (int)Math.Round(lead / firstMeasure));
            markers.Add(new Marker { measure = 0, bpm = 60 * beatsPerMeasure * count / lead });
            measure = count;
        }
        markers.Add(new Marker { measure = measure, bpm = segments[0].bpm });

        for (int i = 1; i < segments.Count; ++i)
        {
            double length = 60 / segments[i - 1].bpm * beatsPerMeasure;
            double gap = segments[i].downbeat - segments[i - 1].downbeat;
            int count = Math.Max(1, (int)Math.Round(gap / length));
            double last = gap - (count - 1) * length;     // The measure that brings the next downbeat onto a line
            if (Math.Abs(last - length) > 0.001)
                markers.Add(new Marker { measure = measure + count - 1, bpm = 60 * beatsPerMeasure / last });
            measure += count;
            markers.Add(new Marker { measure = measure, bpm = segments[i].bpm });
        }

        // Two markers on one measure: the later one wins
        for (int i = markers.Count - 2; i >= 0; --i)
        {
            if (markers[i].measure == markers[i + 1].measure)
                markers.RemoveAt(i);
        }
        return markers;
    }

    /// <summary>
    /// Where the first measure starts at this tempo, and which measures after it don't line up with the audio.
    /// </summary>
    public Check CheckMeasures(Candidate tempo, double musicStart, int beatsPerMeasure, double to)
    {
        Check check = new Check();
        double period = 60 / tempo.bpm;
        double end = Math.Min(to, duration);

        // The music starts on the first beat from which the beats keep sounding: a pad swelling in or a single
        // noise before it doesn't count
        double beat = tempo.firstBeat % period;
        while (beat < musicStart - period * 0.25)
            beat += period;
        double typical = TypicalBeatStrength(tempo, beat, end);
        while (beat + period * 4 < end)
        {
            double next = 0;
            for (int i = 0; i < 4; ++i)
                next += PeakNear(m_onsets, beat + i * period, 0.02);
            if (PeakNear(m_onsets, beat, 0.02) > typical * 0.4 && next / 4 > typical * 0.4)
                break;
            beat += period;
        }

        // The downbeat: the beats of the measure with the most kick over the song (often both 1 and 3), and of
        // those, the first one where the music starts
        double[] kicks = new double[beatsPerMeasure];
        double bestKick = 0;
        for (int offset = 0; offset < beatsPerMeasure; ++offset)
        {
            for (double t = beat + offset * period; t < end; t += period * beatsPerMeasure)
                kicks[offset] += PeakNear(m_kicks, t, 0.015) + 0.25 * PeakNear(m_onsets, t, 0.015);
            bestKick = Math.Max(bestKick, kicks[offset]);
        }
        int bestOffset = 0;
        while (bestOffset < beatsPerMeasure - 1 && kicks[bestOffset] < bestKick * 0.8)
            ++bestOffset;
        check.firstDownbeat = beat + bestOffset * period;

        // Each measure: are the onsets near its beats right on them? Measures whose hits sit consistently early or
        // late (a tempo change) or that have hits but none on the beats are unsure. Silent ones are fine.
        double measure = period * beatsPerMeasure;
        for (double start = check.firstDownbeat; start + measure <= end; start += measure)
        {
            ++check.measuresChecked;
            List<double> offsets = new List<double>();
            double onBeat = 0, anywhere = 0;
            for (int i = 0; i < beatsPerMeasure; ++i)
            {
                double t = start + i * period;
                double offset;
                float strength = StrongestNear(t, period * 0.3, out offset);
                anywhere += strength;
                if (strength > 1)
                    offsets.Add(offset);
                onBeat += PeakNear(m_onsets, t, 0.02);
            }

            bool hasSound = anywhere > beatsPerMeasure * 0.8;
            offsets.Sort();
            double median = offsets.Count > 0 ? offsets[offsets.Count / 2] : 0;
            bool drifting = offsets.Count >= beatsPerMeasure / 2 + 1 && Math.Abs(median) > 0.03;
            bool offGrid = hasSound && onBeat < anywhere * 0.35;
            if (drifting || offGrid)
                check.unsureMeasures.Add(start);
        }
        return check;
    }

    // The middle onset strength on the beats with sound, to judge where the music really starts
    double TypicalBeatStrength(Candidate tempo, double from, double end)
    {
        double period = 60 / tempo.bpm;
        List<float> values = new List<float>();
        for (double t = from; t < end; t += period)
        {
            float value = PeakNear(m_onsets, t, 0.02);
            if (value > 0.2f)
                values.Add(value);
        }
        if (values.Count == 0)
            return 1;
        values.Sort();
        return values[values.Count / 2];
    }

    // The strongest onset within radius of a moment, and how far from it (seconds, + = late)
    float StrongestNear(double seconds, double radius, out double offset)
    {
        int a = Math.Max(0, (int)((seconds - radius - m_frameOffset) / m_hopSeconds));
        int b = Math.Min(m_onsets.Length - 1, (int)((seconds + radius - m_frameOffset) / m_hopSeconds) + 1);
        float best = 0;
        offset = 0;
        for (int i = a; i <= b; ++i)
        {
            if (m_onsets[i] > best)
            {
                best = m_onsets[i];
                offset = FrameTime(i) - seconds;
            }
        }
        return best;
    }
}
