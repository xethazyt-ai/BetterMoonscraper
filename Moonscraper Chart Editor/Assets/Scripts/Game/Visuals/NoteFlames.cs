// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using MoonscraperChartEditor.Song;

/// <summary>
/// Flames at the strikers when a note is hit while previewing, like Clone Hero's. They take the color profile's
/// striker_hit_flame colors (open notes and kicks have their own), and keep burning, smaller, while a sustain is held.
/// Turned on and off with the Note Flames checkbox on the left panel; only this computer's view changes.
/// </summary>
public class NoteFlames : MonoBehaviour
{
    const string PrefEnabled = "Display.NoteFlames";
    const string PrefSize = "Display.NoteFlameSizePercent";
    public const int MinSizePercent = 25, MaxSizePercent = 200;
    const string PrefSpeed = "Display.NoteFlameSpeed";    // New name, so everyone starts at the 300% default once
    public const int MinSpeedPercent = 25, MaxSpeedPercent = 300, DefaultSpeedPercent = 300;

    // Clone Hero's own colors, for when no color profile is picked or it leaves these out
    static readonly Color DefaultFlame = new Color32(0xFF, 0xB7, 0x6D, 0xFF);
    static readonly Color DefaultOpenFlame = new Color32(0xBA, 0x00, 0xFF, 0xFF);
    static readonly Color DefaultKickFlame = new Color32(0xFF, 0xB3, 0x00, 0xFF);

    const float BurstSeconds = 0.3f;
    const float HoldSeconds = 0.08f;    // A held flame goes out this long after the sustain stops asking for it
    const float HeldLooksLike = 0.3f;   // How far through a hit's burst a held flame looks
    const int MaxFlames = 48;
    const float LiftLanes = 0.25f;      // Starts just up the highway from the strikeline, in front of the strikers
    const int FlameOrder = 1000, OpenFlameOrder = 1001;
    const float OpenScale = 0.7f;       // Open notes and kicks: a smaller flame on every lane
    const int FrameWidth = 64, FrameHeight = 128, FrameCount = 16;
    const int NoisePeriod = 4;
    const float FramesPerSecond = 24;

    class Flame
    {
        public GameObject gameObject;
        public MeshRenderer renderer;
        public Vector3 basePosition;    // y is ignored: it follows the strikeline, which moves up the chart as the song plays
        public Color color;
        public float width;
        public float age;
        public float life;
        public bool held;
        public int key;                 // The lane it's held for (flames are reused, so the lane's old flame may now be another's)
        public float seed;
        public bool active { get { return gameObject.activeSelf; } }
    }

    static NoteFlames s_instance;
    static bool? s_enabled;
    static int? s_sizePercent;
    static int? s_speedPercent;

    /// <summary>
    /// How fast the flames flicker and a hit's flame burns out, from the Flame Speed slider
    /// </summary>
    public static int speedPercent
    {
        get
        {
            if (!s_speedPercent.HasValue)
                s_speedPercent = Mathf.Clamp(PlayerPrefs.GetInt(PrefSpeed, DefaultSpeedPercent), MinSpeedPercent, MaxSpeedPercent);
            return s_speedPercent.Value;
        }
        set
        {
            s_speedPercent = Mathf.Clamp(value, MinSpeedPercent, MaxSpeedPercent);
            PlayerPrefs.SetInt(PrefSpeed, s_speedPercent.Value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// How big the flames are, from the Flame Size slider
    /// </summary>
    public static int sizePercent
    {
        get
        {
            if (!s_sizePercent.HasValue)
                s_sizePercent = Mathf.Clamp(PlayerPrefs.GetInt(PrefSize, 100), MinSizePercent, MaxSizePercent);
            return s_sizePercent.Value;
        }
        set
        {
            s_sizePercent = Mathf.Clamp(value, MinSizePercent, MaxSizePercent);
            PlayerPrefs.SetInt(PrefSize, s_sizePercent.Value);
            PlayerPrefs.Save();
        }
    }

    readonly List<Flame> m_flames = new List<Flame>();
    readonly Dictionary<int, Flame> m_heldByLane = new Dictionary<int, Flame>();
    MaterialPropertyBlock m_properties;
    Material m_material;
    Mesh m_quad;
    Texture2D m_texture;

    public static bool isEnabled
    {
        get
        {
            if (!s_enabled.HasValue)
                s_enabled = PlayerPrefs.GetInt(PrefEnabled, 1) != 0;
            return s_enabled.Value;
        }
        set
        {
            s_enabled = value;
            PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0);
            PlayerPrefs.Save();
            if (!value && s_instance)
                s_instance.HideAll();
        }
    }

    public static void AttachTo(GameObject editorObject)
    {
        if (!s_instance)
            editorObject.AddComponent<NoteFlames>();
    }

    /// <summary>
    /// A note was hit: a burst of flame at its lane
    /// </summary>
    public static void Hit(Note note)
    {
        if (s_instance && isEnabled)
            s_instance.Spawn(note, false);
    }

    /// <summary>
    /// A sustain is still being held: keeps a smaller flame burning at its lane. Called every frame while it's held.
    /// </summary>
    public static void Hold(Note note)
    {
        if (s_instance && isEnabled)
            s_instance.Spawn(note, true);
    }

    void Awake()
    {
        s_instance = this;
        m_properties = new MaterialPropertyBlock();

        Shader shader = Resources.Load<Shader>("Shaders/NoteFlame");
        if (!shader)
        {
            Debug.LogWarning("Couldn't find the note flame shader, so there won't be flames");
            enabled = false;
            return;
        }

        m_texture = MakeFlameTexture();
        m_material = new Material(shader);
        m_material.mainTexture = m_texture;
        m_quad = MakeQuad();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
        if (m_material)
            Destroy(m_material);
        if (m_texture)
            Destroy(m_texture);
        if (m_quad)
            Destroy(m_quad);
    }

    void Spawn(Note note, bool held)
    {
        if (!m_material || note == null || note.controller == null)
            return;

        ChartEditor editor = ChartEditor.Instance;
        float laneSpacing = LaneSpacing(editor);
        m_layer = note.controller.gameObject.layer;
        Vector3 position = note.controller.transform.position;
        position.y = editor.visibleStrikeline.position.y + laneSpacing * LiftLanes;
        // z stays the note's own: the strikeline object sits nearer the camera, which put the flames below the strikers
        Color color = FlameColor(editor, note);

        if (!note.IsOpenNote())
        {
            SpawnAt(note.rawNote, position, laneSpacing, 1, color, held);
            return;
        }

        // Open notes and kicks burn on every lane, smaller, rather than as one flame across the highway
        LaneInfo laneInfo = editor.laneInfo;
        int lanes = laneInfo != null ? laneInfo.laneCount : 5;
        for (int lane = 0; lane < lanes; ++lane)
        {
            Vector3 lanePosition = position;
            if (laneInfo != null)
                lanePosition.x = position.x + laneInfo.GetLanePosition(lane, false);
            SpawnAt(-1 - lane, lanePosition, laneSpacing, OpenScale, color, held);
        }
    }

    void SpawnAt(int key, Vector3 position, float laneSpacing, float scale, Color color, bool held)
    {
        if (held)
        {
            Flame existing;
            if (m_heldByLane.TryGetValue(key, out existing) && existing.active && existing.held && existing.key == key)
            {
                existing.age = 0;
                return;
            }
        }

        // A hit's burst takes over any burst already on its lane, open or fret, so quick notes that aren't sustains
        // restart one flame each rather than piling up into what looks like a held sustain. Only held sustain flames
        // (like an extended open sustain under fret notes) share a lane.
        Flame flame = held ? null : TakeBurstOnLane(LaneOf(key));
        if (flame == null)
            flame = Take();
        if (flame == null)
            return;

        flame.basePosition = position;
        flame.gameObject.layer = m_layer;
        // An open note's smaller flame goes in front, so on a lane that's also holding a fret both show at once
        flame.renderer.sortingOrder = key < 0 ? OpenFlameOrder : FlameOrder;
        flame.color = color;
        flame.width = laneSpacing * 0.95f * scale;
        flame.age = 0;
        flame.life = held ? HoldSeconds : BurstSeconds;
        flame.held = held;
        flame.key = key;
        flame.seed = Random.value * 100;
        flame.gameObject.SetActive(true);

        if (held)
            m_heldByLane[key] = flame;

        Place(flame);
    }

    void LateUpdate()
    {
        if (!m_material)
            return;

        bool playing = ChartEditor.InstanceExists && ChartEditor.Instance.currentState == ChartEditor.State.Playing;
        float delta = Time.deltaTime;
        m_animationClock += delta * animationSpeed;
        if (m_animationClock > 3600)
            m_animationClock -= 3600;     // Keeps float precision over long sessions

        foreach (Flame flame in m_flames)
        {
            if (!flame.active)
                continue;

            flame.age += flame.held ? delta : delta * animationSpeed;     // A held flame lasts as long as the sustain asks
            if (playing && flame.held && flame.age >= flame.life)
            {
                // The sustain ended: it dies down the way a hit's flame does, rather than vanishing
                flame.held = false;
                flame.life = BurstSeconds;
                flame.age = BurstSeconds * HeldLooksLike;
            }
            if (!playing || flame.age >= flame.life)
            {
                flame.gameObject.SetActive(false);
                continue;
            }

            Place(flame);
        }
    }

    // Faces the camera and grows up from the strikeline, flickering, then fades
    void Place(Flame flame)
    {
        Camera camera = FindCamera();
        if (!camera)
            return;

        // A hit bursts up, then dies down. A held sustain looks like a hit's flame partway through, so a sustain's
        // flames match every other note's
        float t = flame.held ? HeldLooksLike : Mathf.Clamp01(flame.age / flame.life);
        float height = Mathf.Lerp(1.0f, 1.6f, Mathf.Sqrt(t));
        float width = Mathf.Lerp(1.15f, 0.8f, t);
        float brightness = 1 - t * t;
        float flash = (1 - t) * (1 - t);

        Transform transform = flame.gameObject.transform;
        transform.rotation = camera.transform.rotation;
        float sizeScale = sizePercent / 100f;
        Vector3 size = new Vector3(flame.width * width * sizeScale, flame.width * height * sizeScale, 1);
        transform.localScale = size;
        // Stays on its fret: the strikeline (and the camera with it) moves up the chart while the song plays
        Vector3 basePosition = flame.basePosition;
        ChartEditor editor = ChartEditor.Instance;
        if (editor && editor.visibleStrikeline)
            basePosition.y = editor.visibleStrikeline.position.y + LaneSpacing(editor) * LiftLanes;
        transform.position = basePosition + camera.transform.up * (size.y * 0.42f);    // Rises from the fret

        Color color = flame.color;
        color.a = brightness;
        int frame = (int)(m_animationClock * FramesPerSecond + flame.seed) % FrameCount;
        m_properties.SetColor("_Color", color);
        m_properties.SetFloat("_Flash", flash);
        m_properties.SetVector("_MainTex_ST", new Vector4(1f / FrameCount, 1, frame / (float)FrameCount, 0));
        flame.renderer.SetPropertyBlock(m_properties);
    }

    static int LaneOf(int key)
    {
        return key < 0 ? -1 - key : key;
    }

    Flame TakeBurstOnLane(int lane)
    {
        Flame found = null;
        foreach (Flame flame in m_flames)
        {
            if (!flame.active || flame.held || LaneOf(flame.key) != lane)
                continue;

            if (found == null)
                found = flame;
            else
                flame.gameObject.SetActive(false);
        }
        return found;
    }

    Flame Take()
    {
        foreach (Flame flame in m_flames)
        {
            if (!flame.active)
                return flame;
        }

        if (m_flames.Count >= MaxFlames)
        {
            // Reuse the oldest burst rather than skip the newest hit
            Flame oldest = null;
            foreach (Flame flame in m_flames)
            {
                if (!flame.held && (oldest == null || flame.age / flame.life > oldest.age / oldest.life))
                    oldest = flame;
            }
            return oldest;
        }

        GameObject gameObject = new GameObject("Note Flame");
        gameObject.layer = m_layer;     // The notes' layer, set again on each spawn
        gameObject.transform.SetParent(transform, false);
        gameObject.AddComponent<MeshFilter>().sharedMesh = m_quad;
        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = m_material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        // In front of the strikers, notes and sustains: on the last sorting layer (sustains have their own, after the
        // default one, so an open sustain across the highway was drawn over the flames)
        SortingLayer[] sortingLayers = SortingLayer.layers;
        if (sortingLayers.Length > 0)
            renderer.sortingLayerID = sortingLayers[sortingLayers.Length - 1].id;
        renderer.sortingOrder = FlameOrder;
        gameObject.SetActive(false);

        Flame created = new Flame { gameObject = gameObject, renderer = renderer };
        m_flames.Add(created);
        return created;
    }

    void HideAll()
    {
        foreach (Flame flame in m_flames)
            flame.gameObject.SetActive(false);
    }

    static Color FlameColor(ChartEditor editor, Note note)
    {
        Color color;
        if (note.IsOpenNote())
        {
            bool kick = editor.currentGameMode == Chart.GameMode.Drums;
            string key = kick ? "striker_hit_flame_kick" : "striker_hit_flame_open";
            if (CustomNoteColors.TryGetProfileColor("other", key, out color))
                return color;
            return kick ? DefaultKickFlame : DefaultOpenFlame;
        }

        if (CustomNoteColors.TryGetProfileColor("other", "striker_hit_flame", out color))
            return color;
        return DefaultFlame;
    }

    static float LaneSpacing(ChartEditor editor)
    {
        LaneInfo laneInfo = editor.laneInfo;
        if (laneInfo == null || laneInfo.laneCount < 2)
            return 1;
        return Mathf.Abs(laneInfo.GetLanePosition(1, false) - laneInfo.GetLanePosition(0, false));
    }

    float m_animationClock;
    static float animationSpeed { get { return speedPercent / 100f; } }

    Camera m_camera;
    int m_layer = 0;

    // The camera that draws the notes, which draws the flames too (on the notes' layer), so they go over the notes
    // and frets rather than under a camera drawn after them
    Camera FindCamera()
    {
        if (m_camera && m_camera.isActiveAndEnabled && (m_camera.cullingMask & (1 << m_layer)) != 0)
            return m_camera;

        m_camera = null;
        foreach (Camera camera in Camera.allCameras)
        {
            if ((camera.cullingMask & (1 << m_layer)) != 0 && camera.isActiveAndEnabled && (m_camera == null || camera.depth > m_camera.depth))
                m_camera = camera;
        }
        return m_camera;
    }

    // A strip of flame frames, drawn like Clone Hero's: one tall flame with two small licks hugging it, all flickering
    // on their own. R is the glow, G its white-hot core, B a flash of light at the base for
    // hits. The noise repeats every NoisePeriod cells, so the last frame runs back into the first.
    static Texture2D MakeFlameTexture()
    {
        Texture2D texture = new Texture2D(FrameWidth * FrameCount, FrameHeight, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.name = "Note Flame";

        Color32[] pixels = new Color32[FrameWidth * FrameCount * FrameHeight];
        for (int frame = 0; frame < FrameCount; ++frame)
        {
            float t = frame / (float)FrameCount;
            for (int y = 0; y < FrameHeight; ++y)
            {
                float v = (y + 0.5f) / FrameHeight;                         // 0 bottom, 1 top
                for (int x = 0; x < FrameWidth; ++x)
                {
                    float u = (x + 0.5f) / FrameWidth - 0.5f;
                    float n = Fbm(u * 6, v * 3.5f - t * NoisePeriod);
                    float n2 = Fbm(u * 11 + 40, v * 6 - t * NoisePeriod * 2);
                    float wavy = u + (n - 0.5f) * 0.32f * v + (n2 - 0.5f) * 0.08f * v;

                    // One big flame with two small licks hugging it, like Clone Hero's hit flame
                    float body = 0;
                    for (int lick = 0; lick < 3; ++lick)
                    {
                        int side = lick - 1;
                        float tall = side == 0 ? 0.95f : 0.5f;
                        float top = tall * (0.78f + 0.35f * Fbm((lick + 1) * 7.3f, -t * NoisePeriod));
                        if (v >= top)
                            continue;

                        float centre = side * 0.13f + side * 0.12f * v;
                        float baseWidth = side == 0 ? 0.3f : 0.15f;
                        float halfWidth = baseWidth * Mathf.Pow(1 - v / top, 0.75f) * (1 + 0.6f * SmoothStep(0, 0.25f, v) * (1 - v / top)) + 0.004f;
                        float across = 1 - SmoothStep(0, halfWidth, Mathf.Abs(wavy - centre));
                        body = Mathf.Max(body, across * (1 - SmoothStep(top * 0.55f, top, v)));
                    }
                    body *= SmoothStep(0, 0.04f, v);    // Soft bottom edge

                    float glow = Mathf.Clamp01((body + (n - 0.5f) * v * body + (n2 - 0.5f) * 0.4f * body) * 1.7f);
                    float core = Mathf.Clamp01(Mathf.Clamp01((body - 0.35f) / 0.65f) * (1 - SmoothStep(0.02f, 0.42f, v)) * (0.75f + 0.5f * n));
                    float flashDistance = u * u * 1.2f + (v - 0.06f) * 1.8f * (v - 0.06f) * 1.8f;
                    float flash = Mathf.Exp(-flashDistance / 0.03f);

                    pixels[y * FrameWidth * FrameCount + frame * FrameWidth + x] = new Color32((byte)(glow * 255), (byte)(core * 255), (byte)(flash * 255), 255);
                }
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static float SmoothStep(float from, float to, float x)
    {
        float t = Mathf.Clamp01((x - from) / (to - from));
        return t * t * (3 - 2 * t);
    }

    static float Fbm(float x, float y)
    {
        return 0.6f * ValueNoise(x, y, NoisePeriod) + 0.3f * ValueNoise(x * 2 + 17, y * 2, NoisePeriod * 2) + 0.1f * ValueNoise(x * 4 + 31, y * 4, NoisePeriod * 4);
    }

    // Smooth noise that repeats every period cells going up
    static float ValueNoise(float x, float y, int period)
    {
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        float sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);

        float a = Hash(ix, iy, period), b = Hash(ix + 1, iy, period);
        float c = Hash(ix, iy + 1, period), d = Hash(ix + 1, iy + 1, period);
        float bottom = a + (b - a) * sx, top = c + (d - c) * sx;
        return bottom + (top - bottom) * sy;
    }

    static float Hash(int x, int y, int period)
    {
        y = ((y % period) + period) % period;
        unchecked
        {
            uint n = (uint)(x * 374761393 + y * 668265263);
            n = (n ^ (n >> 13)) * 1274126177u;
            return ((n ^ (n >> 16)) & 0xffff) / 65535f;
        }
    }

    static Mesh MakeQuad()
    {
        Mesh mesh = new Mesh();
        mesh.name = "Note Flame";
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }
}
