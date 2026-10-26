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
    const int TextureWidth = 64, TextureHeight = 128;

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
        float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(flame.seed, Time.time * 18);
        float height, brightness, width;
        if (flame.held)
        {
            height = 1.5f * flicker;
            width = 1;
            brightness = 0.8f;
        }
        else
        {
            height = Mathf.Lerp(1.1f, 2.0f, Mathf.Sqrt(t)) * flicker;
            width = Mathf.Lerp(1.0f, 0.6f, t);
            brightness = 1 - t * t;
        }

        Transform transform = flame.gameObject.transform;
        transform.rotation = camera.transform.rotation;
        Vector3 size = new Vector3(flame.width * width, flame.width * height, 1);
        transform.localScale = size;
        transform.position = flame.basePosition + camera.transform.up * (flame.lift + size.y * 0.42f);

        Color color = flame.color;
        color.a = brightness;
        m_properties.SetColor("_Color", color);
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

    // A flame shape: R is the glow, wide at the bottom and licking up to a point, G its white-hot core
    static Texture2D MakeFlameTexture()
    {
        Texture2D texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.name = "Note Flame";

        Color32[] pixels = new Color32[TextureWidth * TextureHeight];
        for (int y = 0; y < TextureHeight; ++y)
        {
            float v = (y + 0.5f) / TextureHeight;                       // 0 bottom, 1 top
            float halfWidth = Mathf.Lerp(0.46f, 0.02f, Mathf.Pow(v, 0.8f));
            float rise = Mathf.SmoothStep(0, 1, v / 0.12f);               // Soft bottom edge
            float fade = 1 - Mathf.SmoothStep(0.55f, 1, v);               // Fades out towards the tip

            for (int x = 0; x < TextureWidth; ++x)
            {
                float u = Mathf.Abs((x + 0.5f) / TextureWidth - 0.5f);
                float across = 1 - Mathf.SmoothStep(0, 1, u / Mathf.Max(halfWidth, 0.001f));
                float glow = across * rise * fade;
                float core = Mathf.Clamp01((across - 0.55f) / 0.45f) * (1 - Mathf.SmoothStep(0.15f, 0.6f, v));

                pixels[y * TextureWidth + x] = new Color32((byte)(glow * 255), (byte)(core * 255), 0, 255);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
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
