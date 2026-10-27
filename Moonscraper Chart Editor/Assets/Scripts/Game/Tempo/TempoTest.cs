// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

/// <summary>
/// Tests Auto Tempo Map against a folder of finished charts. Each chart's own BPM markers are the answer: the song's
/// audio is mapped from scratch and every beat of the chart is compared with the nearest mapped beat. Charts are only
/// read, never changed. Writes a report (one line a song) and, for tuning the detector later without the audio, a
/// small file per song with what the detector heard plus the chart's tempo map.
/// </summary>
public class TempoTest
{
    public const int MaxPackedSongs = 300;
    const double MaxSecondsPerSong = 90;   // Mapping past this is a bug: the song is skipped and noted in the report

    static readonly string[] s_audioExtensions = { ".ogg", ".opus", ".mp3", ".wav", ".flac" };

    public delegate float[] Decoder(string path, out int sampleRate);

    // The chart's tempo map, and where its notes are
    public class Truth
    {
        public int resolution = 192;
        public double offset;           // Seconds: the chart's Offset plus song.ini's delay
        public List<long> bpmTicks = new List<long>();
        public List<double> bpms = new List<double>();
        public bool onlyFourFour = true;
        public long firstNote = -1, lastNote = -1;

        public double TickToTime(long tick)
        {
            double time = offset;
            for (int i = 0; i < bpmTicks.Count; ++i)
            {
                long next = i + 1 < bpmTicks.Count ? bpmTicks[i + 1] : long.MaxValue;
                if (tick <= bpmTicks[i])
                    break;
                time += (Math.Min(tick, next) - bpmTicks[i]) / (double)resolution * 60 / bpms[i];
            }
            return time;
        }

        public void Write(BinaryWriter writer)
        {
            writer.Write(resolution);
            writer.Write(offset);
            writer.Write(onlyFourFour);
            writer.Write(firstNote);
            writer.Write(lastNote);
            writer.Write(bpmTicks.Count);
            for (int i = 0; i < bpmTicks.Count; ++i)
            {
                writer.Write(bpmTicks[i]);
                writer.Write(bpms[i]);
            }
        }

        public static Truth Read(BinaryReader reader)
        {
            Truth truth = new Truth();
            truth.resolution = reader.ReadInt32();
            truth.offset = reader.ReadDouble();
            truth.onlyFourFour = reader.ReadBoolean();
            truth.firstNote = reader.ReadInt64();
            truth.lastNote = reader.ReadInt64();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; ++i)
            {
                truth.bpmTicks.Add(reader.ReadInt64());
                truth.bpms.Add(reader.ReadDouble());
            }
            return truth;
        }
    }

    public class Score
    {
        public int beats;               // The chart's beats from its first note to its last
        public double shiftMs;          // The median gap to our nearest beat: a constant offset, not a tempo problem
        public double meanErrorMs;      // Average gap once that offset is taken away
        public double withinShare;      // Share of beats within 25 ms (after the offset)
        public double beatRatio;        // Our beats over the chart's, in the same stretch (2 = we're double time)
        public double worstAt = -1;     // Middle of the 8 beats furthest off, in seconds
        public double driftFrom = -1;   // First place 4 beats in a row are over 50 ms off
    }

    // ---- Reading charts ----

    public static Truth ReadChart(string path)
    {
        Truth truth = new Truth();
        string section = string.Empty;
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.StartsWith("["))
            {
                section = line;
                continue;
            }
            int equals = line.IndexOf('=');
            if (equals < 0)
                continue;
            string key = line.Substring(0, equals).Trim();
            string[] value = line.Substring(equals + 1).Trim().Split(' ');
            if (section == "[Song]")
            {
                if (key == "Resolution")
                    int.TryParse(value[0], out truth.resolution);
                else if (key == "Offset")
                    double.TryParse(value[0], NumberStyles.Float, CultureInfo.InvariantCulture, out truth.offset);
                continue;
            }
            long tick;
            if (!long.TryParse(key, out tick) || value.Length < 2)
                continue;
            if (section == "[SyncTrack]")
            {
                long number;
                if (value[0] == "B" && long.TryParse(value[1], out number) && number > 0)
                {
                    truth.bpmTicks.Add(tick);
                    truth.bpms.Add(number / 1000.0);
                }
                else if (value[0] == "TS" && long.TryParse(value[1], out number))
                {
                    if (number != 4 || (value.Length > 2 && value[2] != "2"))
                        truth.onlyFourFour = false;
                }
            }
            else if (value[0] == "N" && section != "[Events]")
            {
                NoteAt(truth, tick);
            }
        }
        return truth;
    }

    public static Truth ReadMidi(string path)
    {
        Truth truth = new Truth();
        byte[] data = File.ReadAllBytes(path);
        int pos = 0;
        if (ReadTag(data, ref pos) != "MThd")
            throw new Exception("not a MIDI file");
        int headerLength = ReadInt(data, ref pos, 4);
        int start = pos;
        ReadInt(data, ref pos, 2);
        int tracks = ReadInt(data, ref pos, 2);
        int division = ReadInt(data, ref pos, 2);
        if ((division & 0x8000) != 0)
            throw new Exception("SMPTE timing isn't supported");
        truth.resolution = division;
        pos = start + headerLength;

        for (int t = 0; t < tracks && pos + 8 <= data.Length; ++t)
        {
            string tag = ReadTag(data, ref pos);
            int length = ReadInt(data, ref pos, 4);
            int end = Math.Min(data.Length, pos + length);
            if (tag == "MTrk")
                ReadTrack(data, pos, end, t == 0, truth);
            pos = end;
        }
        return truth;
    }

    static void ReadTrack(byte[] data, int pos, int end, bool tempoTrack, Truth truth)
    {
        long tick = 0;
        int status = 0;
        while (pos < end)
        {
            tick += ReadVariable(data, ref pos);
            if (pos >= end)
                break;
            int b = data[pos];
            if (b >= 0x80)
            {
                status = b;
                ++pos;
            }
            if (status == 0xFF)
            {
                int type = data[pos++];
                int length = (int)ReadVariable(data, ref pos);
                if (tempoTrack && type == 0x51 && length == 3)
                {
                    int micros = (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];
                    if (micros > 0)
                    {
                        truth.bpmTicks.Add(tick);
                        truth.bpms.Add(60000000.0 / micros);
                    }
                }
                else if (tempoTrack && type == 0x58 && length >= 2)
                {
                    if (data[pos] != 4 || data[pos + 1] != 2)
                        truth.onlyFourFour = false;
                }
                pos += length;
                status = 0;
            }
            else if (status == 0xF0 || status == 0xF7)
            {
                pos += (int)ReadVariable(data, ref pos);
                status = 0;
            }
            else
            {
                int kind = status & 0xF0;
                int size = kind == 0xC0 || kind == 0xD0 ? 1 : 2;
                if (kind == 0x90 && size == 2 && pos + 1 < end && data[pos + 1] > 0 && !tempoTrack)
                    NoteAt(truth, tick);
                pos += size;
            }
        }
    }

    static void NoteAt(Truth truth, long tick)
    {
        if (truth.firstNote < 0 || tick < truth.firstNote)
            truth.firstNote = tick;
        truth.lastNote = Math.Max(truth.lastNote, tick);
    }

    static string ReadTag(byte[] data, ref int pos)
    {
        string tag = Encoding.ASCII.GetString(data, pos, 4);
        pos += 4;
        return tag;
    }

    static int ReadInt(byte[] data, ref int pos, int bytes)
    {
        int value = 0;
        for (int i = 0; i < bytes; ++i)
            value = (value << 8) | data[pos++];
        return value;
    }

    static long ReadVariable(byte[] data, ref int pos)
    {
        long value = 0;
        while (pos < data.Length)
        {
            int b = data[pos++];
            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
                break;
        }
        return value;
    }

    // song.ini's delay, in seconds
    static double IniDelay(string folder)
    {
        string ini = Path.Combine(folder, "song.ini");
        if (!File.Exists(ini))
            return 0;
        foreach (string raw in File.ReadAllLines(ini))
        {
            string line = raw.Trim();
            int equals = line.IndexOf('=');
            if (equals < 0 || line.Substring(0, equals).Trim().ToLowerInvariant() != "delay")
                continue;
            double ms;
            if (double.TryParse(line.Substring(equals + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ms))
                return ms / 1000;
        }
        return 0;
    }

    // ---- Comparing ----

    // Each beat of a map made of markers, from the chart's start until 'end' seconds
    public static List<double> BeatTimes(List<AutoTempoDetector.Marker> markers, int beatsPerMeasure, double end)
    {
        List<double> beats = new List<double>();
        if (markers.Count == 0)
            return beats;
        double time = 0;
        for (int i = 0; i < markers.Count; ++i)
        {
            double position = markers[i].measure * beatsPerMeasure + markers[i].beat;
            double next = i + 1 < markers.Count ? markers[i + 1].measure * beatsPerMeasure + markers[i + 1].beat : double.MaxValue;
            double beatLength = 60 / markers[i].bpm;
            for (double b = Math.Ceiling(position - 1e-9); b < next - 1e-9; ++b)
            {
                double at = time + (b - position) * beatLength;
                if (at > end)
                    return beats;
                beats.Add(at);
            }
            if (next < double.MaxValue)
                time += (next - position) * beatLength;
        }
        return beats;
    }

    public static Score Compare(Truth truth, List<double> ours)
    {
        Score score = new Score();
        if (truth.firstNote < 0 || ours.Count == 0)
            return score;
        double from = truth.TickToTime(truth.firstNote), to = truth.TickToTime(truth.lastNote);
        List<double> chartBeats = new List<double>();
        long beat = truth.firstNote / truth.resolution * truth.resolution;
        for (; beat <= truth.lastNote; beat += truth.resolution)
            chartBeats.Add(truth.TickToTime(beat));
        if (chartBeats.Count < 8)
            return score;

        double[] errors = new double[chartBeats.Count];
        for (int i = 0; i < chartBeats.Count; ++i)
            errors[i] = Nearest(ours, chartBeats[i]) - chartBeats[i];
        double[] sorted = (double[])errors.Clone();
        Array.Sort(sorted);
        double shift = sorted[sorted.Length / 2];

        double total = 0;
        int within = 0;
        for (int i = 0; i < errors.Length; ++i)
        {
            errors[i] = Math.Abs(errors[i] - shift);
            total += errors[i];
            if (errors[i] < 0.025)
                ++within;
        }
        score.beats = chartBeats.Count;
        score.shiftMs = shift * 1000;
        score.meanErrorMs = total / errors.Length * 1000;
        score.withinShare = within / (double)errors.Length;

        int ourCount = 0;
        foreach (double t in ours)
        {
            if (t >= chartBeats[0] - 0.03 && t <= chartBeats[chartBeats.Count - 1] + 0.03)
                ++ourCount;
        }
        score.beatRatio = ourCount / (double)chartBeats.Count;

        double worst = -1;
        for (int i = 0; i + 8 <= errors.Length; ++i)
        {
            double sum = 0;
            for (int j = i; j < i + 8; ++j)
                sum += errors[j];
            if (sum > worst)
            {
                worst = sum;
                score.worstAt = chartBeats[i + 4];
            }
        }
        for (int i = 0; i + 4 <= errors.Length; ++i)
        {
            if (errors[i] > 0.05 && errors[i + 1] > 0.05 && errors[i + 2] > 0.05 && errors[i + 3] > 0.05)
            {
                score.driftFrom = chartBeats[i];
                break;
            }
        }
        return score;
    }

    static double Nearest(List<double> sorted, double t)
    {
        int index = sorted.BinarySearch(t);
        if (index >= 0)
            return sorted[index];
        index = ~index;
        if (index == 0)
            return sorted[0];
        if (index == sorted.Count)
            return sorted[sorted.Count - 1];
        return t - sorted[index - 1] < sorted[index] - t ? sorted[index - 1] : sorted[index];
    }

    // ---- Running over a folder ----

    public volatile string status = string.Empty;
    public volatile bool stop;
    public volatile bool finished;
    public volatile int done, failed;
    public string reportPath;

    readonly string m_songsFolder;
    readonly string m_outFolder;
    readonly Decoder m_decode;

    public TempoTest(string songsFolder, string outFolder, Decoder decode)
    {
        m_songsFolder = songsFolder;
        m_outFolder = outFolder;
        m_decode = decode;
    }

    public void Run()
    {
        try
        {
            RunAll();
        }
        catch (Exception e)
        {
            status = "The test stopped: " + e.Message;
        }
        finally
        {
            finished = true;
        }
    }

    void RunAll()
    {
        status = "Looking for charts...";
        List<string> charts = new List<string>();
        FindCharts(m_songsFolder, charts);
        if (charts.Count == 0)
        {
            status = "No notes.chart or notes.mid found in that folder.";
            return;
        }
        // A fixed shuffle, so stopping early still covers a spread of the library
        Random random = new Random(12345);
        for (int i = charts.Count - 1; i > 0; --i)
        {
            int j = random.Next(i + 1);
            string swap = charts[i];
            charts[i] = charts[j];
            charts[j] = swap;
        }

        string packFolder = Path.Combine(m_outFolder, "Test Pack");
        Directory.CreateDirectory(packFolder);
        reportPath = Path.Combine(m_outFolder, "Tempo Test Report.csv");
        m_charts = charts;
        m_packFolder = packFolder;
        m_means = new List<double>();
        using (m_report = new StreamWriter(reportPath, false, new UTF8Encoding(true)))
        {
            m_report.WriteLine("Song,Chart,Chart BPM,Chart markers,Our BPM,Our markers,Beats,Offset ms,Mean error ms,Within 25 ms %,Beat ratio,Worst at,Drift from,Unsure measures,Not 4/4,Seconds,Pack file");

            // Several songs at once, one a core (leaving one for the editor)
            int workers = Math.Max(1, Math.Min(MaxWorkers, Environment.ProcessorCount - 1));
            List<System.Threading.Thread> threads = new List<System.Threading.Thread>();
            for (int w = 0; w < workers; ++w)
            {
                System.Threading.Thread thread = new System.Threading.Thread(Work);
                thread.IsBackground = true;
                thread.Start();
                threads.Add(thread);
            }
            foreach (System.Threading.Thread thread in threads)
                thread.Join();
        }

        m_means.Sort();
        string summary = done + " songs tested" + (failed > 0 ? ", " + failed + " skipped" : "");
        if (m_means.Count > 0)
        {
            int good = m_means.FindAll(m => m < 10).Count;
            summary += ". Median error " + Num(m_means[m_means.Count / 2]) + " ms; " + good + " of " + m_means.Count + " within 10 ms on average";
        }
        status = (stop ? "Stopped. " : "Done. ") + summary + ".";
        File.WriteAllText(Path.Combine(m_outFolder, "Summary.txt"), status);
    }

    const int MaxWorkers = 8;
    readonly object m_lock = new object();
    List<string> m_charts;
    string m_packFolder;
    StreamWriter m_report;
    List<double> m_means;
    int m_next, m_packed;
    readonly List<AutoTempoDetector> m_active = new List<AutoTempoDetector>();

    /// <summary>Stops the test: songs being mapped give up at once.</summary>
    public void Stop()
    {
        stop = true;
        lock (m_lock)
        {
            foreach (AutoTempoDetector detector in m_active)
                detector.deadline = DateTime.MinValue;
        }
    }

    void Work()
    {
        while (!stop)
        {
            int i;
            lock (m_lock)
            {
                if (m_next >= m_charts.Count)
                    return;
                i = m_next++;
                status = "Testing " + (i + 1) + " of " + m_charts.Count + ": " + MakeRelative(Path.GetDirectoryName(m_charts[i]));
            }
            TestSong(i);
        }
    }

    void TestSong(int i)
    {
        string chart = m_charts[i];
        string name = MakeRelative(Path.GetDirectoryName(chart));
        DateTime started = DateTime.Now;
        AutoTempoDetector detector = null;
        try
        {
            Truth truth = chart.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ? ReadMidi(chart) : ReadChart(chart);
            truth.offset += IniDelay(Path.GetDirectoryName(chart));
            if (truth.bpms.Count == 0 || truth.firstNote < 0)
                throw new Exception("chart has no tempo map or notes");

            int rate;
            float[] mix = MixAudio(Path.GetDirectoryName(chart), out rate);
            if (stop)
                return;
            detector = new AutoTempoDetector(mix, rate);
            mix = null;
            detector.deadline = DateTime.UtcNow.AddSeconds(MaxSecondsPerSong);
            lock (m_lock)
                m_active.Add(detector);
            AutoTempoDetector.Map map = detector.MapAuto(4, 0, AutoTempoDetector.Mode.Auto);
            List<AutoTempoDetector.Marker> markers = AutoTempoDetector.ToMarkers(map.segments, 0, 4);
            List<double> ours = BeatTimes(markers, 4, detector.duration);
            Score score = Compare(truth, ours);

            // The first songs, and after that only those that went wrong, are kept for tuning
            string packName = string.Empty;
            lock (m_lock)
            {
                if (m_packed < MaxPackedSongs && (m_packed < MaxPackedSongs / 2 || score.meanErrorMs > 15 || Math.Abs(score.beatRatio - 1) > 0.05))
                {
                    packName = (i + 1).ToString("00000") + ".tempo";
                    ++m_packed;
                }
            }
            if (packName.Length > 0)
                WritePack(Path.Combine(m_packFolder, packName), name, truth, detector);

            string row = string.Join(",", new string[] {
                Csv(name), Path.GetFileName(chart), Num(truth.bpms[0]), truth.bpms.Count.ToString(),
                markers.Count > 1 ? Num(markers[1].bpm) : markers.Count > 0 ? Num(markers[0].bpm) : "", markers.Count.ToString(),
                score.beats.ToString(), Num(score.shiftMs), Num(score.meanErrorMs), Num(score.withinShare * 100),
                Num(score.beatRatio), Time(score.worstAt), Time(score.driftFrom), map.unsureMeasures.Count.ToString(),
                truth.onlyFourFour ? "" : "yes", Num((DateTime.Now - started).TotalSeconds), packName });
            lock (m_lock)
            {
                m_report.WriteLine(row);
                m_report.Flush();
                m_means.Add(score.meanErrorMs);
                ++done;
            }
        }
        catch (Exception e)
        {
            // A song cut off by Stop isn't a result
            if (stop)
                return;
            lock (m_lock)
            {
                m_report.WriteLine(Csv(name) + "," + Path.GetFileName(chart) + ",,,,,,,,,,,,,," + Num((DateTime.Now - started).TotalSeconds) + ",skipped: " + Csv(e.Message));
                m_report.Flush();
                ++failed;
            }
        }
        finally
        {
            if (detector != null)
            {
                lock (m_lock)
                    m_active.Remove(detector);
            }
        }
    }

    void FindCharts(string folder, List<string> charts)
    {
        try
        {
            string chart = Path.Combine(folder, "notes.chart"), midi = Path.Combine(folder, "notes.mid");
            if (File.Exists(chart))
                charts.Add(chart);
            else if (File.Exists(midi))
                charts.Add(midi);
            foreach (string sub in Directory.GetDirectories(folder))
                FindCharts(sub, charts);
        }
        catch (Exception)
        {
            // A folder that can't be read is skipped
        }
    }

    // All the song's audio added together (stems play together), skipping the crowd and preview tracks
    float[] MixAudio(string folder, out int rate)
    {
        float[] mix = null;
        rate = 0;
        foreach (string file in Directory.GetFiles(folder))
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();
            string name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
            if (Array.IndexOf(s_audioExtensions, extension) < 0 || name.StartsWith("crowd") || name.StartsWith("preview"))
                continue;
            int fileRate;
            float[] samples = m_decode(file, out fileRate);
            if (samples == null)
                continue;
            if (mix == null)
            {
                mix = samples;
                rate = fileRate;
            }
            else if (fileRate == rate)
            {
                if (samples.Length > mix.Length)
                {
                    float[] longer = samples;
                    samples = mix;
                    mix = longer;
                }
                for (int i = 0; i < samples.Length; ++i)
                    mix[i] += samples[i];
            }
        }
        if (mix == null)
            throw new Exception("no audio that could be read");
        return mix;
    }

    static void WritePack(string path, string name, Truth truth, AutoTempoDetector detector)
    {
        using (FileStream file = File.Create(path))
        using (GZipStream zip = new GZipStream(file, CompressionMode.Compress))
        using (BinaryWriter writer = new BinaryWriter(zip))
        {
            writer.Write("BMTT1");
            writer.Write(name);
            truth.Write(writer);
            detector.WriteFeatures(writer);
        }
    }

    // Reads a pack file back: for tuning
    public static AutoTempoDetector ReadPack(string path, out string name, out Truth truth)
    {
        using (FileStream file = File.OpenRead(path))
        using (GZipStream zip = new GZipStream(file, CompressionMode.Decompress))
        using (BinaryReader reader = new BinaryReader(zip))
        {
            if (reader.ReadString() != "BMTT1")
                throw new Exception("not a tempo test file");
            name = reader.ReadString();
            truth = Truth.Read(reader);
            return AutoTempoDetector.ReadFeatures(reader);
        }
    }

    string MakeRelative(string folder)
    {
        string root = m_songsFolder.TrimEnd('\\', '/');
        return folder.StartsWith(root) ? folder.Substring(root.Length).TrimStart('\\', '/') : folder;
    }

    static string Num(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    static string Time(double seconds)
    {
        if (seconds < 0)
            return string.Empty;
        return ((int)(seconds / 60)) + ":" + (seconds % 60).ToString("00.0", CultureInfo.InvariantCulture);
    }

    static string Csv(string text)
    {
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
