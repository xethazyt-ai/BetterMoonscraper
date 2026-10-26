// See LICENSE in project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// Keeps a copy of the log for each of the last few runs in the "Collab Logs" folder beside Unity's own log, which is
/// wiped every time Moonscraper starts. Also notes in it when Moonscraper stops responding, and what Duet was doing,
/// so a crash or a freeze can be looked into afterwards.
/// </summary>
public class DuetLogKeeper : MonoBehaviour
{
    const int RunsKept = 5;
    const double FrozenSeconds = 5;         // No new frame for this long counts as frozen
    const float SlowFrameSeconds = 1;       // A frame this long is noted, as a freeze that came back
    const float StatusEverySeconds = 5;

    static readonly object s_lock = new object();
    static StreamWriter s_writer;
    static readonly Stopwatch s_clock = Stopwatch.StartNew();
    static long s_lastFrameMs = 0;
    static volatile string s_status = "Starting";
    static Thread s_watchdog;

    float m_nextStatus = 0;
    string m_lastStatus;

    public static string folder { get { return Path.Combine(Application.persistentDataPath, "Collab Logs"); } }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Register()
    {
        try
        {
            Directory.CreateDirectory(folder);

            // Oldest first, including logs named from before the rename to BetterMoonscraper
            string[] old = Directory.GetFiles(folder, "*Moonscraper *.txt");
            Array.Sort(old, (a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
            for (int i = 0; i < old.Length - (RunsKept - 1); ++i)
                File.Delete(old[i]);

            string path = Path.Combine(folder, "BetterMoonscraper " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss") + ".txt");
            s_writer = new StreamWriter(path, false, new UTF8Encoding(false)) { AutoFlush = true };
            Write(Application.productName + " " + Application.version + " on " + SystemInfo.operatingSystem);
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogWarning("Duet: couldn't start the Duet log: " + e.Message);
            return;
        }

        Application.logMessageReceivedThreaded += OnLog;

        GameObject go = new GameObject("Duet Log Keeper");
        DontDestroyOnLoad(go);
        go.AddComponent<DuetLogKeeper>();

        Interlocked.Exchange(ref s_lastFrameMs, s_clock.ElapsedMilliseconds);
        s_watchdog = new Thread(Watch) { IsBackground = true, Name = "Duet freeze watch" };
        s_watchdog.Start();
    }

    static void OnLog(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Log)
            Write(message);
        else
            Write(type + ": " + message + (string.IsNullOrEmpty(stackTrace) ? string.Empty : "\n" + stackTrace.TrimEnd()));
    }

    static void Write(string line)
    {
        lock (s_lock)
        {
            if (s_writer == null)
                return;

            try
            {
                s_writer.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + line);
            }
            catch (Exception)
            {
                // Out of disk space or similar. The log is only a help, so carry on without it.
            }
        }
    }

    // Runs on its own thread, so it can still write while Moonscraper itself is stuck
    static void Watch()
    {
        bool reported = false;
        while (true)
        {
            Thread.Sleep(1000);

            double stuck = (s_clock.ElapsedMilliseconds - Interlocked.Read(ref s_lastFrameMs)) / 1000.0;
            if (stuck < FrozenSeconds)
            {
                reported = false;
                continue;
            }

            // A Windows pop-up such as the save window holds Moonscraper up on purpose
            if (!reported && DuetKeepAlive.blockedCount <= 0)
            {
                reported = true;
                Write("FROZEN: Moonscraper hasn't drawn a frame for " + stuck.ToString("0") + " seconds. Last known: " + s_status);
            }
        }
    }

    void Update()
    {
        Interlocked.Exchange(ref s_lastFrameMs, s_clock.ElapsedMilliseconds);

        if (Time.unscaledDeltaTime > SlowFrameSeconds && Time.frameCount > 10)
            Write("Slow frame: " + Time.unscaledDeltaTime.ToString("0.0") + " seconds. Before it: " + s_status);

        // Noted when it changes, at most every few seconds, since the edit number changes with every edit
        s_status = DescribeNow();
        if (s_status != m_lastStatus && Time.unscaledTime >= m_nextStatus)
        {
            m_nextStatus = Time.unscaledTime + StatusEverySeconds;
            m_lastStatus = s_status;
            Write("Status: " + s_status);
        }
    }

    static string DescribeNow()
    {
        ChartEditor editor = ChartEditor.InstanceExists ? ChartEditor.Instance : null;     // Instance logs an error every time while loading
        string editorState = editor != null ? editor.currentState.ToString() : "Loading";
        DuetSession session = DuetPanel.activeSession;
        return editorState + " | " + (session != null ? "Duet: " + session.Describe() : "Not in Duet");
    }

    void OnApplicationQuit()
    {
        Write("Closing");
    }
}
