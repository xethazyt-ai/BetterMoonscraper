// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using MoonscraperChartEditor.Song;

/// <summary>
/// Gradual tempo changes. Hold right click on a BPM marker and drag it onto the next one: on release, the tempo
/// slides from the first marker's BPM to the next one's, as BPM markers every 16th note in between (one undo).
/// A wavy line follows the mouse while dragging, and turns gold once it's over the next marker.
/// The marker itself never moves, and with no BPM marker after it nothing happens.
/// </summary>
public class BpmRamp : MonoBehaviour
{
    const int StepsPerBeat = 4;         // A marker every 16th note
    const float SnapDistance = 0.35f;   // How near the next marker (world units) counts as on top of it
    const int LinePoints = 64;
    const float Waves = 1.6f;           // Wiggles per world unit
    const float Amplitude = 0.12f;
    const float WaveSpeed = 6;

    static readonly Color DragColor = new Color(1f, 1f, 1f, 0.85f);
    static readonly Color ReadyColor = new Color(1f, 0.8f, 0.2f, 1f);

    static BpmRamp s_instance;

    ChartEditor m_editor;
    BPM m_from, m_to;
    LineRenderer m_line;
    float m_phase;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => AttachToEditor();
        AttachToEditor();
    }

    static void AttachToEditor()
    {
        if (s_instance)
            return;

        ChartEditor editor = FindObjectOfType<ChartEditor>();
        if (editor)
            editor.gameObject.AddComponent<BpmRamp>();
    }

    void Awake()
    {
        s_instance = this;
        m_editor = GetComponent<ChartEditor>();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
        if (m_line)
        {
            Destroy(m_line.sharedMaterial);
            Destroy(m_line.gameObject);
        }
    }

    /// <summary>
    /// Right click went down on this BPM marker. Starts the drag if there's a marker after it.
    /// </summary>
    public static void Begin(BPM from, GameObject marker)
    {
        if (!s_instance || from == null || from.song == null)
            return;

        BPM next = SongObjectHelper.GetNextNonInclusive(from.song.bpms, from.tick);
        if (next == null)
            return;

        s_instance.m_from = from;
        s_instance.m_to = next;
        s_instance.ShowLine(marker.layer);
    }

    public static bool isDragging { get { return s_instance && s_instance.m_from != null; } }

    void Update()
    {
        if (m_from == null)
            return;

        // Anything that changed the song under us (undo, a Collab edit, another chart) ends the drag
        Song song = m_editor.currentSong;
        if (m_editor.currentState != ChartEditor.State.Editor || m_from.song != song || m_to.song != song || Input.GetKeyDown(KeyCode.Escape))
        {
            End();
            return;
        }

        float fromY = song.TickToWorldYPosition(m_from.tick);
        float toY = song.TickToWorldYPosition(m_to.tick);
        float mouseY = fromY;
        Vector2? mouse = m_editor.services.mouseMonitorSystem.world2DPosition;
        if (mouse != null)
            mouseY = ((Vector2)mouse).y;
        bool onNext = mouseY >= toY - SnapDistance;

        if (!Input.GetMouseButton(1))
        {
            if (onNext)
                Apply();
            End();
            return;
        }

        DrawLine(fromY, Mathf.Clamp(mouseY, fromY, toY), onNext);
    }

    void Apply()
    {
        Song song = m_editor.currentSong;
        uint stepTicks = (uint)Mathf.Max(1, Mathf.RoundToInt(song.resolution / (float)StepsPerBeat));
        uint span = m_to.tick - m_from.tick;
        int steps = (int)(span / stepTicks);
        if (steps < 2)
        {
            m_editor.services.notificationBar.PushNotification("BPM MARKERS TOO CLOSE TO RAMP", 2, true);
            return;
        }

        List<SongObject> between = new List<SongObject>();
        foreach (BPM bpm in song.bpms)
        {
            if (bpm.tick > m_from.tick && bpm.tick < m_to.tick)
                between.Add(bpm);
        }

        // Even steps from the first BPM up (or down) to the next, each held for a 16th note
        List<SongObject> ramp = new List<SongObject>();
        double start = m_from.value, end = m_to.value;
        for (int k = 1; k < steps; ++k)
        {
            uint tick = m_from.tick + (uint)k * stepTicks;
            if (m_to.tick - tick < stepTicks / 2)
                break;      // A sliver before the next marker isn't worth its own tempo
            uint value = (uint)Mathf.Max(1, (float)System.Math.Round(start + (end - start) * k / steps));
            ramp.Add(new BPM(tick, value));
        }

        List<SongEditCommand> commands = new List<SongEditCommand>();
        if (between.Count > 0)
            commands.Add(new SongEditDelete(between));
        commands.Add(new SongEditAdd(ramp));
        m_editor.commandStack.Push(new BatchedSongEditCommand(commands));

        Debug.Log(string.Format("BPM ramp from {0} at {1} to {2} at {3}: {4} markers", m_from.displayValue, m_from.tick, m_to.displayValue, m_to.tick, ramp.Count));
        m_editor.services.notificationBar.PushNotification("BPM RAMP " + m_from.displayValue + " TO " + m_to.displayValue, 2, true);
    }

    void End()
    {
        m_from = null;
        m_to = null;
        if (m_line)
            m_line.gameObject.SetActive(false);
    }

    void ShowLine(int layer)
    {
        if (!m_line)
        {
            GameObject lineObject = new GameObject("BPM Ramp Line");
            lineObject.transform.SetParent(transform, false);
            m_line = lineObject.AddComponent<LineRenderer>();
            m_line.useWorldSpace = true;
            m_line.positionCount = LinePoints;
            m_line.widthMultiplier = 0.06f;
            m_line.numCapVertices = 2;
            m_line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_line.receiveShadows = false;
            m_line.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
            SortingLayer[] sortingLayers = SortingLayer.layers;
            if (sortingLayers.Length > 0)
                m_line.sortingLayerID = sortingLayers[sortingLayers.Length - 1].id;
            m_line.sortingOrder = 900;
        }

        m_line.gameObject.layer = layer;
        m_line.gameObject.SetActive(true);
        m_phase = 0;
    }

    // A sine wave down the BPM lane from the first marker to the mouse, rolling along as it's held
    void DrawLine(float fromY, float toY, bool ready)
    {
        if (!m_line)
            return;

        m_phase += Time.deltaTime * WaveSpeed;
        float x = SongObjectController.CHART_CENTER_POS + BPMController.position;
        for (int i = 0; i < LinePoints; ++i)
        {
            float t = i / (float)(LinePoints - 1);
            float y = Mathf.Lerp(fromY, toY, t);
            float wave = Mathf.Sin((y - fromY) * Waves * Mathf.PI * 2 - m_phase) * Amplitude;
            m_line.SetPosition(i, new Vector3(x + wave, y, -0.05f));
        }

        Color color = ready ? ReadyColor : DragColor;
        m_line.startColor = color;
        m_line.endColor = color;
    }
}
