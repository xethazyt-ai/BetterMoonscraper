// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;

/// <summary>
/// Auto Tempo Map's tap-along helper: beats tapped while the song plays become a tempo map for that stretch. Steady
/// measures share one BPM marker (fitted over every tapped beat, so a few uneven taps even out), a measure whose beats
/// are uneven gets a marker on each beat, and the rest of the song keeps what Auto Tempo Map found.
/// </summary>
public static class TapTempo
{
    const double SteadyChange = 0.015;      // A measure within 1.5% of the stretch before it carries on that tempo
    const double EvenBeats = 0.03;          // Seconds a beat can be off an even split before it's a beat-by-beat measure

    /// <summary>
    /// Tapped times cleaned up: a double tap (much closer than the usual gap) is dropped, and a missed beat (a gap two
    /// or three times the usual) is filled in.
    /// </summary>
    public static List<double> CleanBeats(List<double> taps)
    {
        List<double> sorted = new List<double>(taps);
        sorted.Sort();
        if (sorted.Count < 3)
            return sorted;

        List<double> gaps = new List<double>();
        for (int i = 1; i < sorted.Count; ++i)
            gaps.Add(sorted[i] - sorted[i - 1]);
        gaps.Sort();
        double usual = gaps[gaps.Count / 2];
        if (usual <= 0)
            return sorted;

        List<double> beats = new List<double> { sorted[0] };
        for (int i = 1; i < sorted.Count; ++i)
        {
            double gap = sorted[i] - beats[beats.Count - 1];
            if (gap < usual * 0.5)
                continue;
            int missed = (int)Math.Round(gap / usual) - 1;
            if (missed >= 1 && missed <= 2)
            {
                // Spread the missed beats evenly between the two taps
                double start = beats[beats.Count - 1];
                for (int m = 1; m <= missed; ++m)
                    beats.Add(start + gap * m / (missed + 1));
            }
            beats.Add(sorted[i]);
        }
        return beats;
    }

    /// <summary>
    /// The tapped stretch as map segments, the first tap being beat 1 of a measure. Only whole measures count.
    /// </summary>
    public static List<AutoTempoDetector.Segment> ToSegments(List<double> beats, int beatsPerMeasure)
    {
        List<AutoTempoDetector.Segment> segments = new List<AutoTempoDetector.Segment>();
        int measures = (beats.Count - 1) / beatsPerMeasure;
        int m = 0;
        while (m < measures)
        {
            if (!IsEven(beats, m, beatsPerMeasure))
            {
                // Speeding up or slowing down inside the measure: a marker on each beat
                double[] measureBeats = new double[beatsPerMeasure];
                for (int b = 0; b < beatsPerMeasure; ++b)
                    measureBeats[b] = beats[m * beatsPerMeasure + b];
                segments.Add(new AutoTempoDetector.Segment
                {
                    downbeat = measureBeats[0],
                    bpm = MeasureBpm(beats, m, beatsPerMeasure),
                    ramp = true,
                    beats = measureBeats,
                });
                ++m;
                continue;
            }

            // As many steady measures in a row as stay close to the same tempo
            int end = m + 1;
            double bpm = FitBpm(beats, m * beatsPerMeasure, end * beatsPerMeasure);
            while (end < measures && IsEven(beats, end, beatsPerMeasure)
                && Math.Abs(MeasureBpm(beats, end, beatsPerMeasure) / bpm - 1) <= SteadyChange)
            {
                ++end;
                bpm = FitBpm(beats, m * beatsPerMeasure, end * beatsPerMeasure);
            }

            double downbeat = FitStart(beats, m * beatsPerMeasure, end * beatsPerMeasure);
            segments.Add(new AutoTempoDetector.Segment { downbeat = downbeat, bpm = TidyBpm(bpm) });
            m = end;
        }
        return segments;
    }

    /// <summary>
    /// The tapped stretch put into the detected map: what was found before it stays, the taps replace the stretch,
    /// and after it the map picks up again on its own measure lines. With no map, it's the taps alone.
    /// </summary>
    public static List<AutoTempoDetector.Segment> Merge(List<AutoTempoDetector.Segment> found, List<AutoTempoDetector.Segment> tapped, double tapEnd, int beatsPerMeasure)
    {
        List<AutoTempoDetector.Segment> merged = new List<AutoTempoDetector.Segment>();
        if (tapped.Count == 0)
        {
            if (found != null)
                merged.AddRange(found);
            return merged;
        }

        double tapStart = tapped[0].downbeat;
        if (found != null)
        {
            foreach (AutoTempoDetector.Segment segment in found)
            {
                if (segment.downbeat < tapStart - 0.05)
                    merged.Add(segment);
            }
        }
        merged.AddRange(tapped);
        if (found == null)
            return merged;

        // The part of the map playing when the taps stopped carries on from its next measure line
        AutoTempoDetector.Segment playing = null;
        int next = 0;
        for (int i = 0; i < found.Count; ++i)
        {
            if (found[i].downbeat <= tapEnd)
            {
                playing = found[i];
                next = i + 1;
            }
        }
        double resume = tapEnd;
        if (playing != null && playing.beats == null)
        {
            double measure = 60 / playing.bpm * beatsPerMeasure;
            double line = playing.downbeat + Math.Ceiling((tapEnd - playing.downbeat) / measure - 0.25) * measure;
            if (next >= found.Count || line < found[next].downbeat - 0.05)
            {
                merged.Add(new AutoTempoDetector.Segment { downbeat = line, bpm = playing.bpm });
                resume = line;
            }
        }
        for (int i = next; i < found.Count; ++i)
        {
            if (found[i].downbeat > resume + 0.05)
                merged.Add(found[i]);
        }
        return merged;
    }

    static double MeasureBpm(List<double> beats, int measure, int beatsPerMeasure)
    {
        double start = beats[measure * beatsPerMeasure];
        double end = beats[(measure + 1) * beatsPerMeasure];
        return 60 * beatsPerMeasure / Math.Max(0.001, end - start);
    }

    // Each beat of the measure within a little of an even split of it
    static bool IsEven(List<double> beats, int measure, int beatsPerMeasure)
    {
        double start = beats[measure * beatsPerMeasure];
        double end = beats[(measure + 1) * beatsPerMeasure];
        double step = (end - start) / beatsPerMeasure;
        for (int b = 1; b < beatsPerMeasure; ++b)
        {
            if (Math.Abs(beats[measure * beatsPerMeasure + b] - (start + step * b)) > EvenBeats)
                return false;
        }
        return true;
    }

    // A straight line through the beats from first to last (inclusive), by least squares
    static void Fit(List<double> beats, int first, int last, out double slope, out double start)
    {
        int n = last - first + 1;
        double meanX = (n - 1) / 2.0, meanY = 0;
        for (int i = 0; i < n; ++i)
            meanY += beats[first + i];
        meanY /= n;
        double sxy = 0, sxx = 0;
        for (int i = 0; i < n; ++i)
        {
            sxy += (i - meanX) * (beats[first + i] - meanY);
            sxx += (i - meanX) * (i - meanX);
        }
        slope = sxx > 0 ? sxy / sxx : 0.5;
        start = meanY - slope * meanX;
    }

    static double FitBpm(List<double> beats, int first, int last)
    {
        double slope, start;
        Fit(beats, first, last, out slope, out start);
        return 60 / Math.Max(0.001, slope);
    }

    static double FitStart(List<double> beats, int first, int last)
    {
        double slope, start;
        Fit(beats, first, last, out slope, out start);
        return start;
    }

    // Tapped tempos come out a hair off the whole or half BPM a song was made at
    static double TidyBpm(double bpm)
    {
        double half = Math.Round(bpm * 2) / 2;
        return Math.Abs(half - bpm) <= bpm * 0.0015 ? half : Math.Round(bpm * 1000) / 1000;
    }
}
