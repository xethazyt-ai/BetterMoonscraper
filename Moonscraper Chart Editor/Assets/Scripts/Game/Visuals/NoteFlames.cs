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

    // Clone Hero's own colors, for when no color profile is picked or it leaves these out
    static readonly Color DefaultFlame = new Color32(0xFF, 0xB7, 0x6D, 0xFF);
    static readonly Color DefaultOpenFlame = new Color32(0xBA, 0x00, 0xFF, 0xFF);
    static readonly Color DefaultKickFlame = new Color32(0xFF, 0xB3, 0x00, 0xFF);

    const float BurstSeconds = 0.3f;
    const float HoldSeconds = 0.08f;    // A held flame goes out this long after the sustain stops asking for it
    const int MaxFlames = 48;
    const float LiftLanes = 0.75f;      // In lane widths: the striker heads sit about this far up the screen from the strikeline
    const int FrameWidth = 64, FrameHeight = 128, FrameCount = 16;
    const int NoisePeriod = 4;
    const float FramesPerSecond = 24;

    class Flame
    {
        public GameObject gameObject;
        public MeshRenderer renderer;
        public Vector3 basePosition;
        public Color color;
        public float width;
        public float lift;          // How far above the strikeline it starts, so it rises from the striker head in front of the note
        public float age;
        public float life;
        public bool held;
        public float seed;
        public bool active { get { return gameObject.activeSelf; } }
    }

    static NoteFlames s_instance;
    static bool? s_enabled;

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
        bool open = note.IsOpenNote();
        int lane = open ? -1 : note.rawNote;

        if (held)
        {
            Flame existing;
            if (m_heldByLane.TryGetValue(lane, out existing) && existing.active && existing.held)
            {
                existing.age = 0;
                return;
            }
        }

        Flame flame = Take();
        if (flame == null)
            return;

        float laneSpacing = LaneSpacing(editor);
        Vector3 position = note.controller.transform.position;
        position.y = editor.visibleStrikeline.position.y;
        position.z = editor.visibleStrikeline.position.z;

        flame.basePosition = position;
        flame.color = FlameColor(editor, note);
        flame.width = laneSpacing * (open ? 3.6f : 0.95f) * (held ? 0.7f : 1);
        flame.lift = laneSpacing * LiftLanes;
        flame.age = 0;
        flame.life = held ? HoldSeconds : BurstSeconds;
        flame.held = held;
        flame.seed = Random.value * 100;
        flame.gameObject.SetActive(true);

        if (held)
            m_heldByLane[lane] = flame;

        Place(flame);
    }

    void LateUpdate()
    {
        if (!m_material)
            return;

        bool playing = ChartEditor.Instance && ChartEditor.Instance.currentState == ChartEditor.State.Playing;
        float delta = Time.deltaTime;

        foreach (Flame flame in m_flames)
        {
            if (!flame.active)
                continue;

            flame.age += delta;
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

        float t = Mathf.Clamp01(flame.age / flame.life);
        float height, brightness, width, flash;
        if (flame.held)
        {
            height = 1.6f;
            width = 1;
            brightness = 0.85f;
            flash = 0.3f;
        }
        else
        {
            // A hit bursts up, then dies down
            height = Mathf.Lerp(1.3f, 2.1f, Mathf.Sqrt(t));
            width = Mathf.Lerp(1.15f, 0.8f, t);
            brightness = 1 - t * t;
            flash = (1 - t) * (1 - t);
        }

        Transform transform = flame.gameObject.transform;
        transform.rotation = camera.transform.rotation;
        Vector3 size = new Vector3(flame.width * width, flame.width * height, 1);
        transform.localScale = size;
        transform.position = flame.basePosition + camera.transform.up * (flame.lift + size.y * 0.42f);

        Color color = flame.color;
        color.a = brightness;
        int frame = (int)(Time.time * FramesPerSecond + flame.seed) % FrameCount;
        m_properties.SetColor("_Color", color);
        m_properties.SetFloat("_Flash", flash);
        m_properties.SetVector("_MainTex_ST", new Vector4(1f / FrameCount, 1, frame / (float)FrameCount, 0));
        flame.renderer.SetPropertyBlock(m_properties);
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
        gameObject.layer = 0;   // Drawn by the highway's cameras
        gameObject.transform.SetParent(transform, false);
        gameObject.AddComponent<MeshFilter>().sharedMesh = m_quad;
        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = m_material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sortingOrder = 1000;   // In front of the strikers and notes
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

    Camera m_camera;

    // The camera drawing the highway: the 3D one, or the 2D one in 2D mode
    Camera FindCamera()
    {
        if (m_camera && m_camera.isActiveAndEnabled)
            return m_camera;

        m_camera = null;
        foreach (Camera camera in Camera.allCameras)
        {
            if ((camera.cullingMask & 1) != 0 && camera.isActiveAndEnabled && (m_camera == null || camera.depth < m_camera.depth))
                m_camera = camera;
        }
        return m_camera;
    }

    // A strip of flame frames, drawn like Clone Hero's: three licks of fire (a tall one in the middle, shorter ones
    // leaning out) that flicker on their own. R is the glow, G its white-hot core, B a flash of light at the base for
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
                float rise = Mathf.SmoothStep(0, 1, v / 0.05f);             // Soft bottom edge
                for (int x = 0; x < FrameWidth; ++x)
                {
                    float u = (x + 0.5f) / FrameWidth - 0.5f;
                    float n = Fbm(u * 6, v * 3.5f - t * NoisePeriod);
                    float wavy = u + (n - 0.5f) * 0.3f * v;

                    float body = 0;
                    for (int lick = 0; lick < 3; ++lick)
                    {
                        float offset = (lick - 1) * 0.17f;
                        float tall = lick == 1 ? 1 : 0.62f;
                        float top = tall * (0.75f + 0.4f * Fbm((lick + 1) * 7.3f, -t * NoisePeriod));
                        if (v >= top)
                            continue;

                        float centre = offset + (lick - 1) * 0.22f * v;
                        float halfWidth = (lick == 1 ? 0.24f : 0.2f) * Mathf.Pow(1 - v / top, 0.6f) + 0.005f;
                        float across = 1 - SmoothStep(0, halfWidth, Mathf.Abs(wavy - centre));
                        body = Mathf.Max(body, across * (1 - SmoothStep(top * 0.6f, top, v)));
                    }
                    body *= rise;

                    float glow = Mathf.Clamp01((body + (n - 0.5f) * 0.9f * v * body) * 1.6f);
                    float core = Mathf.Clamp01(Mathf.Clamp01((body - 0.4f) / 0.6f) * (1 - SmoothStep(0.05f, 0.45f, v)) * (0.7f + 0.6f * n));
                    float flashDistance = u * u + (v - 0.08f) * 1.6f * (v - 0.08f) * 1.6f;
                    float flash = Mathf.Exp(-flashDistance / 0.035f);

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
