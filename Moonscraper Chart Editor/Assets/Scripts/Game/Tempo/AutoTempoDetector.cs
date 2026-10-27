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
        public List<double> downbeatChoices = new List<double>();   // Other beats about as strong as the downbeat
    }

    const int TargetRate = 11025;
    const int Window = 512;
    const int Hop = 64;             // About 5.8 ms
    const double MinBpm = 50, MaxBpm = 320;
    const double KickMaxHz = 160;
    const double PadSeconds = 0.5;
    const double KickLatencySeconds = 0.015;  // The low end's onset peaks a little after the hit (longer waves)
    const double FluxLatencySeconds = 0.0105;

    float[] m_onsets;               // Whole mix
    float[] m_kicks;                // Low end only
    double[] m_energySums;          // Running total of each frame's loudness (log), for averages over a stretch
    double[] m_chromaSums;          // Running totals of each frame's share per pitch class (12 a frame)
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

        // Silence in front, so a song that hits on its very first sample still shows a jump from quiet to loud
        int pad = (int)(PadSeconds * rate);
        float[] padded = new float[audio.Length + pad];
        Array.Copy(audio, 0, padded, pad, audio.Length);
        audio = padded;

        // A hit shows up most strongly a little before the window's middle reaches it (measured on test drums)
        m_frameOffset = Window / 2.0 / rate + FluxLatencySeconds - pad / (double)rate;
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

        // Pitch class of each bin in the range where chords and bass notes sit
        int[] pitchClass = new int[bins];
        for (int b = 0; b < bins; ++b)
        {
            double hz = b * (double)rate / Window;
            pitchClass[b] = hz < 120 || hz > 2000 ? -1 : (((int)Math.Round(69 + 12 * Math.Log(hz / 440, 2)) % 12) + 12) % 12;
        }
        m_energySums = new double[frames + 1];
        m_chromaSums = new double[(frames + 1) * 12];
        double[] chroma = new double[12];

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
            double energy = 0, chromaTotal = 0;
            Array.Clear(chroma, 0, 12);
            for (int b = 1; b < bins; ++b)
            {
                double linear = Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
                energy += linear;
                if (pitchClass[b] >= 0)
                {
                    chroma[pitchClass[b]] += linear;
                    chromaTotal += linear;
                }
                float magnitude = (float)Math.Log(1 + 100 * linear);
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
            m_energySums[f + 1] = m_energySums[f] + Math.Log(1 + energy);
            for (int k = 0; k < 12; ++k)
                m_chromaSums[(f + 1) * 12 + k] = m_chromaSums[f * 12 + k] + (chromaTotal > 0 ? chroma[k] / chromaTotal : 0);
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

    // The tempo from a first stretch is only as exact as that stretch is long: a 0.05% miss is a beat line 100 ms
    // off after three minutes. So pin it down over longer and longer stretches, for as long as the song keeps it.
    Candidate Extend(Candidate tempo, double from)
    {
        double window = SectionSeconds;
        Candidate good = tempo;
        double goodScore = double.NaN;
        while (from + window < duration)
        {
            window = Math.Min(duration - from, window * 2);
            int first = Math.Max(0, (int)((from - m_frameOffset) / m_hopSeconds));
            int last = Math.Min(m_onsets.Length, (int)((from + window - m_frameOffset) / m_hopSeconds));
            if (double.IsNaN(goodScore))
            {
                double unused;
                goodScore = Sharpness(good.bpm, first, Math.Min(last, first + (int)(SectionSeconds / m_hopSeconds)), out unused);
            }
            Candidate longer = FineRefine(good.bpm, first, last);
            if (longer.score < goodScore * 0.85)
                break;      // The tempo changes in there: keep what fitted before
            good = longer;
            goodScore = longer.score;
        }
        return good;
    }

    Candidate FineRefine(double bpm, int first, int last)
    {
        double phase, best = bpm, bestScore = -1;
        foreach (double[] pass in new[] { new[] { 0.002, 0.0025 }, new[] { 0.0002, 0.00025 }, new[] { 0.00003, 0.00003 } })
        {
            double span = best * pass[0], step = best * pass[1] / 10, centre = best;
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
        Candidate result = new Candidate { bpm = best, score = bestScore };
        // A whole or half BPM only when it fits practically as well over the whole stretch
        foreach (double whole in new[] { Math.Round(best), Math.Round(best * 2) / 2 })
        {
            if (Math.Abs(whole - best) > 0.6)
                continue;
            double wholeScore = Sharpness(whole, first, last, out phase);
            if (wholeScore >= bestScore * 0.995)
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

    // How much happens on the beats of a tempo between two moments
    double BeatStrength(double bpm, double phase, double from, double to)
    {
        double period = 60 / bpm;
        double total = 0;
        for (double t = phase + Math.Ceiling((from - phase) / period) * period; t < Math.Min(to, duration); t += period)
            total += PeakNear(m_onsets, t, 0.02);
        return total;
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
        public bool ramp;           // One measure of a slowdown or speed-up
        public double[] beats;      // ...and where each of its beats falls, the first being the downbeat, for a marker on each
        public double[] halves;     // ...and a note halfway through a long beat, where there is one (NaN where not)
    }

    public class Map
    {
        public List<Candidate> choices = new List<Candidate>();     // What the main tempo could be, best first
        public List<Segment> segments = new List<Segment>();         // The main tempo, then any changes
        public List<double> unsureMeasures = new List<double>();    // Start times (audio) of measures to check by ear
        public int measuresChecked;
        public double musicStart;
        public List<double> followDownbeats;    // Following: every measure's start as followed
        public bool followed;
        public double followScore;
        public double startBpm;     // The tempo picked from the choices, that the map starts from
    }

    const int ChangeRunMeasures = 4;     // This many unsure measures within ChangeWindowMeasures means the tempo changed
    const int ChangeWindowMeasures = 6;
    const int MaxSegments = 12;
    const double SectionSeconds = 40;     // How much of the song from a start or a change is used to find its tempo
    List<double> m_pendingUnsure = new List<double>();
    double m_forcedDownbeat = double.NaN;
    const double RampProbeSeconds = 2;      // Looking for where a slowdown or speed-up settles, in steps of this
    const double RampSettleSeconds = 24;    // ...and how much steady music after it is used to find the new tempo
    const double RampMaxSeconds = 32;       // ...settling within this long after the measures stop lining up

    // The first of ChangeRunMeasures unsure measures close together, from 'from' on
    static int FindRun(List<double> unsure, int from, double measure)
    {
        for (int i = from; i + ChangeRunMeasures <= unsure.Count; ++i)
        {
            if (unsure[i + ChangeRunMeasures - 1] - unsure[i] < measure * (ChangeWindowMeasures - 0.5))
                return i;
        }
        return -1;
    }

    // A different tempo from a moment on, close to the old one (a change, not its half or double)
    Candidate NewTempoAt(double changeAt, Candidate old)
    {
        Result next = Detect(changeAt, Math.Min(duration, changeAt + SectionSeconds));
        foreach (Candidate found in next.candidates)
        {
            Candidate candidate = NearestOctave(found, old.bpm, changeAt, changeAt + SectionSeconds);
            double ratio = candidate.bpm / old.bpm;
            // The first that fits: the list already prefers the beat over its half or double
            if (ratio > 0.67 && ratio < 1.5)
            {
                candidate = ThreeTwoCheck(candidate, old.bpm, changeAt, changeAt + SectionSeconds, 4);
                return Math.Abs(candidate.bpm / old.bpm - 1) > 0.003 ? candidate : null;
            }
        }
        return null;
    }

    // Music that moves in threes fits both a tempo and the one 3:2 from it (133 and 200 BPM share every sixteenth).
    // The one whose measures line up better, with more on its beats, is the one a charter would use.
    Candidate ThreeTwoCheck(Candidate found, double oldBpm, double from, double to, int beatsPerMeasure)
    {
        to = Math.Min(duration, to);
        Candidate best = found;
        double bestFit = MeasureShare(found, from, to, beatsPerMeasure);
        double bestStrength = BeatStrength(found.bpm, found.firstBeat, from, to) / ((to - from) * found.bpm / 60);
        foreach (double factor in new[] { 1.5, 2.0 / 3 })
        {
            double bpm = found.bpm * factor;
            double ratio = bpm / oldBpm;
            if (bpm < MinBpm || bpm > MaxBpm || ratio <= 0.67 || ratio >= 1.5)
                continue;
            double period = 60 / bpm;
            double phase = 0, phaseStrength = double.MinValue;
            for (int k = 0; k < 12; ++k)
            {
                double tryPhase = (found.firstBeat + period * k / 12) % period;
                double strength = BeatStrength(bpm, tryPhase, from, to);
                if (strength > phaseStrength)
                {
                    phaseStrength = strength;
                    phase = tryPhase;
                }
            }
            Candidate other = new Candidate { bpm = bpm, score = found.score, firstBeat = phase, isWhole = found.isWhole };
            double fit = MeasureShare(other, from, to, beatsPerMeasure);
            double strengthPerBeat = phaseStrength / ((to - from) * bpm / 60);
            if (fit >= bestFit && strengthPerBeat > bestStrength * 1.1)
            {
                best = other;
                bestFit = fit;
                bestStrength = strengthPerBeat;
            }
        }
        return best;
    }

    // The share of measures that line up
    double MeasureShare(Candidate tempo, double from, double to, int beatsPerMeasure)
    {
        Check check = CheckMeasures(tempo, from, beatsPerMeasure, to);
        return check.measuresChecked == 0 ? 0 : 1 - (double)check.unsureMeasures.Count / check.measuresChecked;
    }

    // A tempo found after a change is often its half or double; the one nearest the old tempo is the one meant
    Candidate NearestOctave(Candidate found, double oldBpm, double from, double to)
    {
        Candidate best = found;
        foreach (double factor in new[] { 0.5, 2.0 })
        {
            double bpm = found.bpm * factor;
            if (bpm < MinBpm || bpm > MaxBpm)
                continue;
            if (Math.Abs(Math.Log(bpm / oldBpm)) < Math.Abs(Math.Log(best.bpm / oldBpm)))
            {
                // Halving leaves two ways to place the beats: on the found beats or between them
                double period = 60 / bpm;
                double phase = found.firstBeat % period;
                if (factor < 1 && BeatStrength(bpm, phase + period / 2, from, to) > BeatStrength(bpm, phase, from, to))
                    phase = (phase + period / 2) % period;
                best = new Candidate { bpm = bpm, score = found.score, firstBeat = phase, isWhole = found.isWhole };
            }
        }
        return best;
    }

    // Whether a tempo fits the music straight after a moment: most of the next measures line up
    bool FitsFrom(Candidate tempo, double at, int beatsPerMeasure)
    {
        double measure = 60 / tempo.bpm * beatsPerMeasure;
        Check check = CheckMeasures(tempo, at - 60 / tempo.bpm * 0.5, beatsPerMeasure, Math.Min(duration, at + measure * 12));
        // A sudden change fits straight away; one that only fits a while later is a slowdown or speed-up
        int unsureAtOnce = check.unsureMeasures.FindAll(t => t < at + measure * 4).Count;
        return check.measuresChecked >= 4 && check.unsureMeasures.Count <= check.measuresChecked / 4 && unsureAtOnce <= 1
            && check.firstDownbeat < at + measure * 2;
    }

    const double TailSeconds = 45;          // How close to the end a part that never settles is followed beat by beat
    const double TailHitShare = 0.75;       // ...if at least this share of its beats land on a note

    // The rest of the song from a measure line, beat by beat, trying each tempo found there for its start and keeping
    // the one whose beats land on notes best. The change often starts a measure before the first one flagged, so it
    // starts from whichever of those two measure lines has a note on it.
    List<Segment> TrackTail(Segment old, double changeAt, int beatsPerMeasure)
    {
        double oldMeasure = 60 / old.bpm * beatsPerMeasure;
        double start = changeAt;
        if (changeAt - oldMeasure > old.downbeat && PeakNear(m_onsets, changeAt - oldMeasure, 0.03) > PeakNear(m_onsets, changeAt, 0.03))
            start = changeAt - oldMeasure;
        start = OnsetNear(start, 0.03);

        Result found = Detect(start, Math.Min(duration, start + 10));
        List<Segment> best = null;
        double bestScore = 0;
        foreach (Candidate candidate in found.candidates)
        {
            foreach (double factor in new[] { 1.0, 0.5, 2.0 })
            {
                double bpm = candidate.bpm * factor;
                if (bpm < MinBpm || bpm > MaxBpm || bpm / old.bpm < 0.5 || bpm / old.bpm > 2)
                    continue;
                int hits;
                List<double> beats = TrackBeats(start, bpm, out hits);
                if (beats.Count < beatsPerMeasure * 2 || hits < (beats.Count - 1) * TailHitShare)
                    continue;
                double score = 0;
                foreach (double beat in beats)
                    score += PeakNear(m_onsets, beat, 0.01);
                score /= beats.Count;
                if (score <= bestScore)
                    continue;
                bestScore = score;
                best = new List<Segment>();
                for (int i = 0; i + 1 < beats.Count; i += beatsPerMeasure)
                {
                    // A last measure cut short by the end keeps the pace of its own beats
                    int last = Math.Min(i + beatsPerMeasure, beats.Count - 1);
                    double length = (beats[last] - beats[i]) * beatsPerMeasure / (last - i);
                    Segment segment = new Segment { downbeat = beats[i], bpm = 60 * beatsPerMeasure / length, ramp = true };
                    segment.beats = beats.GetRange(i, last - i + (last == beats.Count - 1 && last - i < beatsPerMeasure ? 1 : 0)).ToArray();
                    segment.halves = new double[segment.beats.Length];
                    for (int b = 0; b < segment.beats.Length; ++b)
                        segment.halves[b] = HalfBeatNote(beats, i + b);
                    best.Add(segment);
                }
            }
        }
        if (best == null)
            return null;

        // Notes that still fit the old tempo going on unchanged mean the music didn't really slow down
        double[] lastBeats = best[best.Count - 1].beats;
        double end = lastBeats[lastBeats.Length - 1];
        foreach (double factor in new[] { 1.0, 0.5 })
        {
            int gridBeats = 0, gridHits = 0;
            for (double t = start; t <= end; t += 60 / (old.bpm * factor), ++gridBeats)
            {
                if (PeakNear(m_onsets, t, 0.03) >= 0.5)
                    ++gridHits;
            }
            if (gridHits >= gridBeats * TailHitShare)
                return null;
        }

        // Beats that keep to the old tempo (or its half or double) mean nothing changed: just a part with unclear measures.
        // The very last measure doesn't count, as the last note can ring out however long it likes.
        for (int i = 0; i + 1 < best.Count; ++i)
        {
            double ratio = best[i].bpm / old.bpm;
            if (Math.Abs(ratio - 1) > 0.04 && Math.Abs(ratio - 0.5) > 0.02 && Math.Abs(ratio - 2) > 0.08)
                return best;
        }
        return null;
    }

    // A beat held well past the one before it, as an ending rings out, often has a note in its middle that the
    // beat's even split wouldn't put on the grid. That note's time, or NaN.
    double HalfBeatNote(List<double> beats, int i)
    {
        if (i < 1 || i + 1 >= beats.Count)
            return double.NaN;
        double gap = beats[i + 1] - beats[i];
        if (gap < (beats[i] - beats[i - 1]) * 1.2)
            return double.NaN;
        double best = double.NaN, bestValue = 2;
        for (double t = beats[i] + gap * 0.3; t <= beats[i] + gap * 0.7; t += 0.002)
        {
            double value = PeakNear(m_onsets, t, 0.004);
            if (value > bestValue)
            {
                bestValue = value;
                best = t;
            }
        }
        return best;
    }

    // The strongest note start within a window, or the moment itself if there's none
    double OnsetNear(double t, double radius)
    {
        double best = t, bestValue = 0;
        for (double x = t - radius; x <= t + radius; x += 0.002)
        {
            double value = PeakNear(m_onsets, x, 0.002);
            if (value > bestValue)
            {
                bestValue = value;
                best = x;
            }
        }
        return best;
    }

    // Beats one at a time from a measure line, each looked for near where the last gap says the next one falls, so
    // the tempo can keep moving. Stops where the notes stop.
    List<double> TrackBeats(double start, double bpm, out int hits)
    {
        List<double> beats = new List<double> { start };
        double period = 60 / bpm, startPeriod = period;
        int misses = 0;
        hits = 0;
        while (true)
        {
            double last = beats[beats.Count - 1];
            double expected = last + period;
            if (expected > duration)
                break;
            double best = expected, bestValue = 0;
            // More room on the late side: an ending can slow down a lot from one beat to the next
            for (double t = expected - period * 0.2; t <= expected + period * 0.35; t += 0.002)
            {
                double off = (t - expected) / (period * (t < expected ? 0.1 : 0.25));
                double value = PeakNear(m_onsets, t, 0.004) * Math.Exp(-0.5 * off * off);
                if (value > bestValue)
                {
                    bestValue = value;
                    best = t;
                }
            }
            // Music that has already slowed a lot can hold the next beat even longer, as the last notes ring out
            if (bestValue < 0.5 && period > startPeriod * 1.2)
            {
                for (double t = expected + period * 0.35; t <= expected + period * 0.8; t += 0.002)
                {
                    double value = PeakNear(m_onsets, t, 0.004);
                    if (value > bestValue && value >= 2)
                    {
                        bestValue = value;
                        best = t;
                    }
                }
            }
            if (bestValue < 0.5)
            {
                // This miss isn't in the list yet
                if (misses == 4)
                    break;
                ++misses;
                best = expected;
            }
            else
            {
                misses = 0;
                ++hits;
            }
            period = period * 0.3 + (best - last) * 0.7;
            beats.Add(best);
        }
        // Not past the last note
        beats.RemoveRange(beats.Count - misses, misses);
        return beats;
    }

    /// <summary>
    /// A slowdown or speed-up: from the old tempo into a new steady one found a little later. The measures between
    /// get a tempo each, changing evenly, placed where the beats land best.
    /// </summary>
    List<Segment> FindRamp(Segment old, double changeAt, int beatsPerMeasure, out Candidate after, out double afterDownbeat)
    {
        after = null;
        afterDownbeat = 0;
        double oldMeasure = 60 / old.bpm * beatsPerMeasure;
        for (double probe = changeAt + RampProbeSeconds; probe + RampSettleSeconds * 0.66 < duration && probe < changeAt + RampMaxSeconds; probe += RampProbeSeconds)
        {
            Result settled = Detect(probe, Math.Min(duration, probe + RampSettleSeconds));
            Candidate next = null;
            foreach (Candidate found in settled.candidates)
            {
                Candidate candidate = NearestOctave(found, old.bpm, probe, probe + RampSettleSeconds);
                double ratio = candidate.bpm / old.bpm;
                if (ratio > 0.67 && ratio < 1.5 && Math.Abs(ratio - 1) >= 0.01)
                {
                    next = candidate;
                    break;
                }
            }
            if (next == null)
                continue;
            next = ThreeTwoCheck(next, old.bpm, probe, probe + RampSettleSeconds, beatsPerMeasure);
            if (Math.Abs(next.bpm / old.bpm - 1) < 0.01)
                continue;
            double nextMeasure = 60 / next.bpm * beatsPerMeasure;
            Check check = CheckMeasures(next, probe, beatsPerMeasure, Math.Min(duration, probe + nextMeasure * 16));
            if (check.measuresChecked < 4 || check.unsureMeasures.Count > check.measuresChecked / 4)
                continue;
            // The old tempo still fitting there means nothing changed after all
            Candidate oldTempo = new Candidate { bpm = old.bpm, firstBeat = old.downbeat };
            Check stillOld = CheckMeasures(oldTempo, probe, beatsPerMeasure, Math.Min(duration, probe + oldMeasure * 16), OnGrid(old, probe, beatsPerMeasure));
            if (stillOld.unsureMeasures.Count <= check.unsureMeasures.Count || stillOld.unsureMeasures.Count * 2 <= stillOld.measuresChecked)
                return null;

            // A steady stretch well after the change pins the new tempo and where its measures fall. The probe can
            // land inside the slowdown or speed-up, where they'd be a little off.
            double anchorFrom = check.firstDownbeat + nextMeasure * 4;
            if (anchorFrom + RampSettleSeconds * 0.66 < duration)
            {
                Result later = Detect(anchorFrom, Math.Min(duration, anchorFrom + RampSettleSeconds));
                foreach (Candidate found in later.candidates)
                {
                    Candidate candidate = NearestOctave(found, next.bpm, anchorFrom, anchorFrom + RampSettleSeconds);
                    if (Math.Abs(candidate.bpm / next.bpm - 1) < 0.02)
                    {
                        next = candidate;
                        nextMeasure = 60 / next.bpm * beatsPerMeasure;
                        break;
                    }
                }
            }
            else
                anchorFrom = check.firstDownbeat;
            Check anchorCheck = CheckMeasures(next, anchorFrom - 60 / next.bpm * 0.5, beatsPerMeasure, Math.Min(duration, anchorFrom + nextMeasure * 8));
            if (anchorCheck.measuresChecked < 2)
                continue;
            double anchor = anchorCheck.firstDownbeat;
            double horizon = anchor + nextMeasure * 2;

            // Where it starts, where it ends and how many measures it takes: the old tempo's measures up to the
            // start, tempos changing evenly to the end, then the new tempo's measures. Whichever puts the most beats
            // on hits over the same stretch of song.
            double rangeStart = Math.Max(old.downbeat + oldMeasure, changeAt - oldMeasure * 4);
            int firstMeasure = (int)Math.Ceiling((rangeStart - old.downbeat) / oldMeasure - 1e-6);
            List<Segment> best = null;
            double end = anchor;
            double bestScore = double.MinValue;
            for (double ending = anchor; ending > rangeStart + oldMeasure; ending -= 60 / next.bpm)
            {
                double newScore = 0;
                int newBeats = 0;
                for (double t = ending; t < horizon - 0.01; t += 60 / next.bpm, ++newBeats)
                    newScore += PeakNear(m_onsets, t, 0.02);
                for (int k = firstMeasure; old.downbeat + k * oldMeasure < ending - Math.Min(oldMeasure, nextMeasure) * 0.9; ++k)
                {
                    double start = old.downbeat + k * oldMeasure;
                    double gap = ending - start;
                    int guess = (int)Math.Round(gap * (old.bpm + next.bpm) / 2 / 60 / beatsPerMeasure);
                    for (int count = Math.Max(1, guess - 1); count <= guess + 1; ++count)
                    {
                        List<Segment> ramp = EvenRamp(start, gap, count, old.bpm, next.bpm, beatsPerMeasure);
                        if (ramp == null)
                            continue;
                        double score = newScore;
                        int beats = newBeats;
                        for (int m = firstMeasure; m < k; ++m)
                        {
                            for (int b = 0; b < beatsPerMeasure; ++b, ++beats)
                                score += PeakNear(m_onsets, old.downbeat + (m * beatsPerMeasure + b) * 60 / old.bpm, 0.02);
                        }
                        foreach (Segment segment in ramp)
                        {
                            for (int b = 0; b < beatsPerMeasure; ++b, ++beats)
                                score += PeakNear(m_onsets, segment.downbeat + b * 60 / segment.bpm, 0.02);
                        }
                        score /= Math.Max(1, beats);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = ramp;
                            end = ending;
                        }
                    }
                }
            }
            if (best == null)
                continue;
            FitRamp(best, end, beatsPerMeasure);
            for (int i = 0; i < best.Count; ++i)
                best[i].beats = BeatsOf(best[i].downbeat, i + 1 < best.Count ? best[i + 1].downbeat : end, beatsPerMeasure);
            after = Extend(next, end);
            afterDownbeat = end;
            return best;
        }
        return null;
    }

    // Nudges each measure line inside a slowdown or speed-up to where its beats land best, keeping both ends where
    // they are, then gives each measure the tempo that fills it
    void FitRamp(List<Segment> ramp, double end, int beatsPerMeasure)
    {
        int count = ramp.Count;
        double[] lines = new double[count + 1];
        for (int i = 0; i < count; ++i)
            lines[i] = ramp[i].downbeat;
        lines[count] = end;

        for (int pass = 0; pass < 3; ++pass)
        {
            for (int j = 1; j < count; ++j)
            {
                double bestShift = 0, bestScore = double.MinValue;
                double room = Math.Min(lines[j] - lines[j - 1], lines[j + 1] - lines[j]) * 0.04;
                for (double shift = -room; shift <= room; shift += 0.002)
                {
                    double line = lines[j] + shift;
                    double score = MeasureFit(lines[j - 1], line, beatsPerMeasure) + MeasureFit(line, lines[j + 1], beatsPerMeasure);
                    if (score > bestScore + 1e-6)
                    {
                        bestScore = score;
                        bestShift = shift;
                    }
                }
                lines[j] += bestShift;
            }
        }

        for (int i = 0; i < count; ++i)
        {
            ramp[i].downbeat = lines[i];
            ramp[i].bpm = 60.0 * beatsPerMeasure / (lines[i + 1] - lines[i]);
        }
    }

    // Where each beat of a measure falls: on the note nearest its even place, if there's a clear one
    double[] BeatsOf(double from, double to, int beatsPerMeasure)
    {
        double period = (to - from) / beatsPerMeasure;
        double[] beats = new double[beatsPerMeasure];
        beats[0] = from;
        for (int b = 1; b < beatsPerMeasure; ++b)
        {
            double even = from + b * period;
            double hit = OnsetNear(even, period * 0.12);
            beats[b] = PeakNear(m_onsets, hit, 0.002) >= 0.5 && hit > beats[b - 1] + period * 0.5 ? hit : even;
        }
        return beats;
    }

    // How well a measure's evenly spaced beats land on hits
    double MeasureFit(double from, double to, int beatsPerMeasure)
    {
        double period = (to - from) / beatsPerMeasure, score = 0;
        for (int b = 0; b < beatsPerMeasure; ++b)
            score += PeakNear(m_onsets, from + b * period, 0.012);
        return score;
    }

    // The first of a segment's measure lines at or after a moment
    static double OnGrid(Segment segment, double t, int beatsPerMeasure)
    {
        double measure = 60 / segment.bpm * beatsPerMeasure;
        return segment.downbeat + Math.Ceiling((t - segment.downbeat) / measure) * measure;
    }

    // 'count' measures from 'start' filling 'gap' seconds, their tempos going evenly from one to the other
    static List<Segment> EvenRamp(double start, double gap, int count, double fromBpm, double toBpm, int beatsPerMeasure)
    {
        double[] bpms = new double[count];
        double total = 0;
        for (int i = 0; i < count; ++i)
        {
            bpms[i] = fromBpm + (toBpm - fromBpm) * (i + 0.5) / count;
            total += 60.0 * beatsPerMeasure / bpms[i];
        }
        double scale = total / gap;     // Above 1: the measures have to be quicker to fit
        if (scale < 0.9 || scale > 1.1)
            return null;
        List<Segment> ramp = new List<Segment>();
        double t = start;
        for (int i = 0; i < count; ++i)
        {
            double bpm = bpms[i] * scale;
            ramp.Add(new Segment { downbeat = t, bpm = bpm, ramp = true });
            t += 60.0 * beatsPerMeasure / bpm;
        }
        return ramp;
    }

    /// <summary>
    /// The whole song's tempo map: the main tempo (or 'chosenBpm' when one of the choices was picked), where the first
    /// measure is, and any later tempo changes, found where several measures in a row stop lining up.
    /// </summary>
    public Map MapSong(int beatsPerMeasure, double chosenBpm)
    {
        Map map = new Map();
        // The tempo the song starts in, from its first stretch: over the whole song a later, longer section in another
        // tempo could win and the start would be mapped in the wrong one
        Result whole = Detect(0, duration);
        Result detected = Detect(whole.musicStart, whole.musicStart + SectionSeconds);
        if (detected.candidates.Count == 0)
            detected = whole;
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

        map.startBpm = tempo.bpm;
        double from = detected.musicStart;
        tempo = Extend(tempo, from);
        for (int segment = 0; segment < MaxSegments; ++segment)
        {
            Check check = CheckMeasures(tempo, from, beatsPerMeasure, duration);
            if (map.segments.Count > 0)
            {
                Segment previous = map.segments[map.segments.Count - 1];
                if (!double.IsNaN(m_forcedDownbeat))
                {
                    // After a slowdown or speed-up, which already ends on this downbeat
                    check = CheckMeasures(tempo, from, beatsPerMeasure, duration, m_forcedDownbeat);
                    m_forcedDownbeat = double.NaN;
                }
                else
                {
                    check.firstDownbeat = InStepWith(previous, check, beatsPerMeasure);
                    check.firstDownbeat = MoveChangeBack(previous, tempo.bpm, check.firstDownbeat, beatsPerMeasure);
                }
                if (check.firstDownbeat <= previous.downbeat + 0.05)
                {
                    // It found the same tempo again, so it can't tell what changed: leave those to check by ear
                    map.unsureMeasures.AddRange(m_pendingUnsure);
                    break;
                }
                // Measures from the new start that the old tempo flagged but the new one covers
                map.unsureMeasures.RemoveAll(t => t >= check.firstDownbeat);
            }
            map.segments.Add(new Segment { downbeat = check.firstDownbeat, bpm = tempo.bpm });
            map.measuresChecked += check.measuresChecked;

            // Where unsure measures bunch up (most of a few measures in a row) the tempo may have changed: suddenly
            // (a new steady tempo from there), gradually (a slowdown or speed-up into a new steady tempo), or not at
            // all (a part with no clear beat, left to check by ear)
            double measure = 60 / tempo.bpm * beatsPerMeasure;
            int searchFrom = 0;
            bool carryOn = false;
            while (true)
            {
                int runStart = FindRun(check.unsureMeasures, searchFrom, measure);
                if (runStart < 0)
                {
                    for (int i = searchFrom; i < check.unsureMeasures.Count; ++i)
                        map.unsureMeasures.Add(check.unsureMeasures[i]);
                    break;
                }
                for (int i = searchFrom; i < runStart; ++i)
                    map.unsureMeasures.Add(check.unsureMeasures[i]);

                double changeAt = check.unsureMeasures[runStart];
                m_pendingUnsure = check.unsureMeasures.GetRange(runStart, check.unsureMeasures.Count - runStart);

                Candidate changed = NewTempoAt(changeAt, tempo);
                if (changed != null && FitsFrom(changed, changeAt, beatsPerMeasure))
                {
                    tempo = Extend(changed, changeAt - 60 / changed.bpm * 0.5);
                    from = changeAt - 60 / tempo.bpm * 0.5;
                    carryOn = true;
                    break;
                }

                Segment current = map.segments[map.segments.Count - 1];
                Candidate after;
                double afterDownbeat;
                List<Segment> ramp = FindRamp(current, changeAt, beatsPerMeasure, out after, out afterDownbeat);
                if (ramp != null)
                {
                    // The measures of the slowdown or speed-up stay on the list to check by ear
                    foreach (double unsure in m_pendingUnsure)
                    {
                        if (unsure < afterDownbeat)
                            map.unsureMeasures.Add(unsure);
                    }
                    map.segments.AddRange(ramp);
                    tempo = after;
                    from = afterDownbeat - 60 / tempo.bpm * 0.5;
                    m_forcedDownbeat = afterDownbeat;
                    carryOn = true;
                    break;
                }

                // Near the end, music that keeps slowing down or speeding up (an outro winding down) never settles
                // into a new tempo: follow it beat by beat to the end
                List<Segment> tail = changeAt > duration - TailSeconds ? TrackTail(current, changeAt, beatsPerMeasure) : null;
                if (tail != null)
                {
                    foreach (double unsure in m_pendingUnsure)
                        map.unsureMeasures.Add(unsure);
                    map.segments.AddRange(tail);
                    break;
                }

                // Neither: those measures are just unclear, so look for a change further on
                int runEnd = Math.Min(check.unsureMeasures.Count, runStart + ChangeRunMeasures);
                for (int i = runStart; i < runEnd; ++i)
                    map.unsureMeasures.Add(check.unsureMeasures[i]);
                searchFrom = runEnd;
            }
            if (!carryOn)
                break;
        }
        return map;
    }

    const double FollowSearch = 0.12;       // Following: how far (share of a beat) a beat may land from where it's expected
    const double FollowPhaseGain = 0.6;     // ...how much of that miss moves the beat
    const double FollowTempoGain = 0.12;    // ...and how much it changes the tempo
    const double FollowGridSeconds = 0.012; // Measures that stay this close to an even grid share one BPM marker

    // The steady map's beat nearest a moment, when the measure there is one it's sure of
    static bool SteadyBeatNear(Map steady, double t, int beatsPerMeasure, out double beat, out double period)
    {
        beat = period = 0;
        int index = -1;
        for (int i = 0; i < steady.segments.Count && steady.segments[i].downbeat <= t + 0.05; ++i)
            index = i;
        if (index < 0)
            return false;
        Segment segment = steady.segments[index];
        period = 60 / segment.bpm;
        double measure = period * beatsPerMeasure;
        double measureStart = segment.downbeat + Math.Floor((t + period * 0.5 - segment.downbeat) / measure) * measure;
        foreach (double unsure in steady.unsureMeasures)
        {
            if (Math.Abs(unsure - measureStart) < period * 0.5)
                return false;
        }
        beat = segment.downbeat + Math.Round((t - segment.downbeat) / period) * period;
        return true;
    }

    static bool FitsGrid(List<double> downbeats, int from, int to)
    {
        double length = (downbeats[to] - downbeats[from]) / (to - from);
        for (int k = from + 1; k < to; ++k)
        {
            if (Math.Abs(downbeats[k] - (downbeats[from] + (k - from) * length)) > FollowGridSeconds)
                return false;
        }
        return true;
    }

    /// <summary>
    /// For songs whose tempo wanders (live drummers, slowing down or speeding up): starts like MapSong, then follows
    /// the beat one beat at a time and gives a tempo for each measure, merging measures that keep the same tempo.
    /// </summary>
    public Map FollowSong(int beatsPerMeasure, double chosenBpm)
    {
        return BestFollow(MapSong(beatsPerMeasure, chosenBpm), beatsPerMeasure);
    }

    // Following through the steady map's sudden changes suits a jump; following on without them suits a ramp, where
    // the steady map puts a change somewhere in the middle. Whichever lands on more beats wins.
    Map BestFollow(Map steady, int beatsPerMeasure)
    {
        Map through = Follow(steady, beatsPerMeasure, true);
        if (steady.segments.Count < 2)
            return through;
        Map on = Follow(steady, beatsPerMeasure, false);
        return on.followScore > through.followScore ? on : through;
    }

    public enum Mode { Auto, Steady, Follow }

    /// <summary>
    /// The song's map: steady tempos with any sudden changes, slowdowns and speed-ups (Auto), or followed beat by
    /// beat for a tempo that wanders all through (a live drummer).
    /// </summary>
    public Map MapAuto(int beatsPerMeasure, double chosenBpm, Mode mode)
    {
        Map steady = MapSong(beatsPerMeasure, chosenBpm);
        if (mode == Mode.Steady || steady.segments.Count == 0)
            return steady;
        Map followed = BestFollow(steady, beatsPerMeasure);
        if (mode == Mode.Follow)
        {
            followed.followed = true;
            return followed;
        }
        return steady;
    }

    Map Follow(Map steady, int beatsPerMeasure, bool throughChanges)
    {
        Map map = new Map { choices = steady.choices, musicStart = steady.musicStart, startBpm = steady.startBpm };
        if (steady.segments.Count == 0)
            return map;

        double period = 60 / steady.segments[0].bpm;
        double beat = steady.segments[0].downbeat;
        double typical = 0;
        {
            List<float> values = new List<float>();
            for (double t = beat; t < duration; t += period)
            {
                float value = PeakNear(m_onsets, t, 0.02);
                if (value > 0.2f)
                    values.Add(value);
            }
            values.Sort();
            typical = values.Count > 0 ? values[values.Count / 2] : 1;
        }

        // Every downbeat, following the beats as they drift
        List<double> downbeats = new List<double> { beat };
        List<int> heard = new List<int>();
        int heardThisMeasure = 0;
        double minPeriod = 60 / MaxBpm, maxPeriod = 60 / MinBpm;
        int nextChange = throughChanges ? 1 : steady.segments.Count;
        double score = 0;
        for (int i = 1; beat + period < duration - 0.05; ++i)
        {
            // A sudden change the steady map found: too big a jump to follow, so start again from it
            while (nextChange < steady.segments.Count && beat + period > steady.segments[nextChange].downbeat - period * 0.5
                && Math.Abs(steady.segments[nextChange].bpm * period / 60 - 1) < FollowSearch * 0.6)
                ++nextChange;   // Close to the tempo it's following already: follow it there instead
            if (nextChange < steady.segments.Count && beat + period > steady.segments[nextChange].downbeat - period * 0.5)
            {
                Segment change = steady.segments[nextChange++];
                beat = change.downbeat;
                period = 60 / change.bpm;
                downbeats.Add(beat);
                heard.Add(heardThisMeasure + beatsPerMeasure / 2);
                heardThisMeasure = 0;
                i = 0;
                continue;
            }

            double expected = beat + period;
            double offset = 0;
            float strength = 0;

            // Where the steady map is sure of its measures, keep to its grid: following hits beat by beat wanders
            // in parts without drums. Only where it isn't sure (a slowdown, a speed-up) follow the beat freely.
            double steadyBeat, steadyPeriod;
            if (SteadyBeatNear(steady, expected, beatsPerMeasure, out steadyBeat, out steadyPeriod)
                && Math.Abs(steadyBeat - expected) < period * 0.25)
            {
                beat = steadyBeat;
                period = steadyPeriod;
                ++heardThisMeasure;
                score += typical;
            }
            else if ((strength = StrongestNear(expected, period * FollowSearch, out offset)) > typical * 0.5)
            {
                score += strength;
                beat = expected + offset * FollowPhaseGain;
                period = Math.Max(minPeriod, Math.Min(maxPeriod, period + offset * FollowTempoGain));
                ++heardThisMeasure;
            }
            else
                beat = expected;     // Nothing to go by (a break): carry on at the same tempo

            if (i % beatsPerMeasure == 0)
            {
                downbeats.Add(beat);
                heard.Add(heardThisMeasure);
                heardThisMeasure = 0;
            }
        }

        // Sections of one steady tempo: as many measures as stay within a few ms of an even grid
        int sectionStart = 0;
        while (sectionStart < downbeats.Count - 1)
        {
            int end = sectionStart + 1;
            while (end + 1 < downbeats.Count && FitsGrid(downbeats, sectionStart, end + 1))
                ++end;
            map.segments.Add(new Segment { downbeat = downbeats[sectionStart], bpm = 60.0 * beatsPerMeasure * (end - sectionStart) / (downbeats[end] - downbeats[sectionStart]) });
            sectionStart = end;
        }
        if (map.segments.Count == 0)
            map.segments.Add(steady.segments[0]);
        map.followDownbeats = downbeats;
        map.followScore = score;

        // Measures where hardly any beat was heard were guessed: worth checking
        for (int m = 0; m < heard.Count; ++m)
        {
            ++map.measuresChecked;
            if (heard[m] < beatsPerMeasure / 2 && PeakNear(m_onsets, downbeats[m], period) > typical * 0.3)
                map.unsureMeasures.Add(downbeats[m]);
        }
        return map;
    }

    // Songs change tempo on a bar line, so of the beats that could start the new section's measures (kicks often
    // land on 1 and 3 alike), take the one a whole number of the old measures after the old downbeat
    static double InStepWith(Segment previous, Check check, int beatsPerMeasure)
    {
        double oldMeasure = 60 / previous.bpm * beatsPerMeasure;
        double best = check.firstDownbeat, bestError = double.MaxValue;
        foreach (double choice in check.downbeatChoices)
        {
            double measures = (choice - previous.downbeat) / oldMeasure;
            double error = Math.Abs(measures - Math.Round(measures));
            if (error < bestError - 0.05)
            {
                bestError = error;
                best = choice;
            }
        }
        return best;
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
        public double beat;     // ...and the beat in it (0.5 = halfway to the next)
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
        int firstLine = 0;      // The measure whose line is the first downbeat
        if (lead > 0.01)
        {
            int count = (int)Math.Round(lead / firstMeasure);
            if (count == 0)
            {
                // Music that starts almost at once: a measure that short would need a huge BPM, so the lead-in takes
                // in the first real measure too and lines up with the second downbeat
                lead += firstMeasure;
                count = 1;
                firstLine = -1;
            }
            markers.Add(new Marker { measure = 0, bpm = 60 * beatsPerMeasure * count / lead });
            measure = count;
        }
        markers.Add(new Marker { measure = measure, bpm = segments[0].bpm });
        measure += firstLine;

        for (int i = 1; i < segments.Count; ++i)
        {
            double length = 60 / segments[i - 1].bpm * beatsPerMeasure;
            double gap = segments[i].downbeat - segments[i - 1].downbeat;
            int count = Math.Max(1, (int)Math.Round(gap / length));
            double last = gap - (count - 1) * length;     // The measure that brings the next downbeat onto a line
            // A nudge of a percent or so is just the new part's start found a few milliseconds off: no marker for it
            if (Math.Abs(last - length) > 0.001 && (segments[i - 1].ramp || Math.Abs(last / length - 1) > 0.015))
                markers.Add(new Marker { measure = measure + count - 1, bpm = 60 * beatsPerMeasure / last });
            measure += count;

            Segment segment = segments[i];
            if (segment.beats == null)
            {
                markers.Add(new Marker { measure = measure, bpm = segment.bpm });
                continue;
            }
            // A slowdown or speed-up gets a marker on every beat, so each lands where it's heard
            double measureEnd = i + 1 < segments.Count ? segments[i + 1].downbeat : segment.downbeat + 60 / segment.bpm * beatsPerMeasure;
            for (int b = 0; b < segment.beats.Length; ++b)
            {
                double next = b + 1 < segment.beats.Length ? segment.beats[b + 1] : measureEnd;
                if (b + 1 == segment.beats.Length && segment.beats.Length < beatsPerMeasure)
                {
                    // The song's last beat, in a measure the ending cuts short: it keeps the pace that led into it
                    if (b == 0)
                        break;
                    next = segment.beats[b] + (segment.beats[b] - segment.beats[b - 1]);
                }
                double half = segment.halves != null ? segment.halves[b] : double.NaN;
                if (double.IsNaN(half))
                {
                    markers.Add(new Marker { measure = measure, beat = b, bpm = 60 / (next - segment.beats[b]) });
                    continue;
                }
                // A note in the middle of a long beat gets the half beat's line put on it
                markers.Add(new Marker { measure = measure, beat = b, bpm = 30 / (half - segment.beats[b]) });
                markers.Add(new Marker { measure = measure, beat = b + 0.5, bpm = 30 / (next - half) });
            }
        }

        // Two markers on one beat: the later one wins
        for (int i = markers.Count - 2; i >= 0; --i)
        {
            if (markers[i].measure == markers[i + 1].measure && markers[i].beat == markers[i + 1].beat)
                markers.RemoveAt(i);
        }
        return markers;
    }

    /// <summary>
    /// Where the first measure starts at this tempo, and which measures after it don't line up with the audio.
    /// </summary>
    public Check CheckMeasures(Candidate tempo, double musicStart, int beatsPerMeasure, double to)
    {
        return CheckMeasures(tempo, musicStart, beatsPerMeasure, to, double.NaN);
    }

    // 'downbeat' (when given) is where the measures start, rather than working it out
    Check CheckMeasures(Candidate tempo, double musicStart, int beatsPerMeasure, double to, double downbeat)
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

        // The downbeat: the beat of the measure with the most kick over the song. Where two are about level (kick on
        // 1 and 3 alike), the first one where the music starts
        // Three signs of a measure's first beat, each counted over the whole song: the most kick, the chords
        // changing, and the loudness changing (sections and phrases start on a 1)
        double[] kickSum = new double[beatsPerMeasure], chordSum = new double[beatsPerMeasure], loudSum = new double[beatsPerMeasure];
        for (int offset = 0; offset < beatsPerMeasure; ++offset)
        {
            for (double t = beat + offset * period; t < end; t += period * beatsPerMeasure)
            {
                kickSum[offset] += PeakNear(m_kicks, t + KickLatencySeconds, 0.03) + 0.05 * PeakNear(m_onsets, t, 0.015);
                chordSum[offset] += ChordChange(t, period);
                loudSum[offset] += Math.Abs(Loudness(t, t + period) - Loudness(t - period, t));
            }
        }
        double[] kicks = new double[beatsPerMeasure];
        double bestKick = 0;
        for (int offset = 0; offset < beatsPerMeasure; ++offset)
        {
            kicks[offset] = Share(kickSum, offset) + Share(chordSum, offset) + Share(loudSum, offset);
            bestKick = Math.Max(bestKick, kicks[offset]);
        }
        int bestOffset = 0;
        while (bestOffset < beatsPerMeasure - 1 && kicks[bestOffset] < bestKick * 0.97)
            ++bestOffset;
        check.firstDownbeat = double.IsNaN(downbeat) ? beat + bestOffset * period : downbeat;
        for (int offset = 0; offset < beatsPerMeasure; ++offset)
        {
            if (kicks[offset] >= bestKick * 0.6)
                check.downbeatChoices.Add(beat + offset * period);
        }

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

    // A value against the average of all the measure's beats, so the three signs weigh the same
    static double Share(double[] values, int index)
    {
        double total = 0;
        foreach (double value in values)
            total += value;
        return total > 0 ? values[index] * values.Length / total : 1;
    }

    int FrameAt(double seconds)
    {
        return Math.Max(0, Math.Min(m_onsets.Length, (int)((seconds - m_frameOffset) / m_hopSeconds)));
    }

    // Average loudness (log) from one moment to another
    double Loudness(double from, double to)
    {
        int a = FrameAt(from), b = FrameAt(to);
        return b > a ? (m_energySums[b] - m_energySums[a]) / (b - a) : 0;
    }

    // How much the notes playing change at a moment: pitch classes over the stretch before against after
    double ChordChange(double t, double span)
    {
        int a = FrameAt(t - span), m = FrameAt(t), b = FrameAt(t + span);
        if (m <= a || b <= m)
            return 0;
        double change = 0;
        for (int k = 0; k < 12; ++k)
        {
            double before = (m_chromaSums[m * 12 + k] - m_chromaSums[a * 12 + k]) / (m - a);
            double after = (m_chromaSums[b * 12 + k] - m_chromaSums[m * 12 + k]) / (b - m);
            change += Math.Abs(after - before);
        }
        return change;
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
