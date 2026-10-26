// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The left side panels (Settings and Tools) can be dragged anywhere on screen and made bigger or smaller with the
/// grip in their bottom right corner, like the Collab Mode and Pattern Blueprints windows. Where they were left is
/// remembered, as a share of the window so it follows the window's size. The Reset Menu Positions key (F8 unless
/// rebound in Controls) puts these panels and the Collab Mode and Pattern Blueprints windows back where they started.
/// </summary>
public class MovablePanels : MonoBehaviour
{
    // The panels under the Editor Panels Canvas that can be moved, by object name, and the name their place is kept under
    static readonly string[,] s_panels =
    {
        { "Properties", "Settings" },
        { "Highway Object Tool Panel", "Tools" },
    };

    static MovablePanels s_instance;
    readonly List<MovablePanel> m_panels = new List<MovablePanel>();
    ChartEditor m_editor;
    bool m_built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => AttachToEditor();
        AttachToEditor();
    }

    static void AttachToEditor()
    {
        if (s_instance)
            return;

        ChartEditor editor = FindObjectOfType<ChartEditor>();
        if (editor)
            editor.gameObject.AddComponent<MovablePanels>();
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
    }

    void Update()
    {
        // After every panel has set itself up in Start
        if (!m_built)
        {
            m_built = true;
            Build();
        }

        if (!Services.IsTyping && MSChartEditorInput.GetInputDown(MSChartEditorInputActions.ResetMenuPositions))
            ResetAll();
    }

    void Build()
    {
        GameObject canvas = GameObject.Find("Editor Panels Canvas");
        if (!canvas)
        {
            Debug.LogWarning("Couldn't find the editor panels, so they can't be moved");
            return;
        }

        for (int i = 0; i < s_panels.GetLength(0); ++i)
        {
            RectTransform panel = canvas.transform.Find(s_panels[i, 0]) as RectTransform;
            if (panel)
                m_panels.Add(MovablePanel.Make(panel, s_panels[i, 1]));
            else
                Debug.LogWarning("Couldn't find the " + s_panels[i, 1] + " panel, so it can't be moved");
        }
    }

    void ResetAll()
    {
        foreach (MovablePanel panel in m_panels)
        {
            if (panel)
                panel.ResetPlace();
        }
        DuetWindow.ResetAll();

        if (m_editor && m_editor.services && m_editor.services.notificationBar)
            m_editor.services.notificationBar.PushNotification("MENU POSITIONS RESET", 2, true);
    }
}

/// <summary>
/// One panel that can be dragged and scaled. Moving shifts its anchors, so it keeps stretching with the window as before.
/// </summary>
public class MovablePanel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public const float MinScale = 0.5f, MaxScale = 3f;
    const float GripSize = 16;
    const float TopMargin = 0.025f;     // Of the window's height: kept clear for the menu bar
    const float KeepVisible = 60;       // Canvas units of a panel always left on screen

    string m_prefKey;
    RectTransform m_rect;
    Vector2 m_anchorMin, m_anchorMax;   // Where it started
    Vector3 m_startScale;
    Vector2 m_shift;                    // In shares of the parent's size
    float m_scale = 1;
    bool m_dragging;
    Vector2 m_screenSize;

    public static MovablePanel Make(RectTransform panel, string name)
    {
        MovablePanel movable = panel.gameObject.AddComponent<MovablePanel>();
        movable.Init(name);
        return movable;
    }

    void Init(string name)
    {
        m_prefKey = "Panels." + name;
        m_rect = (RectTransform)transform;
        m_anchorMin = m_rect.anchorMin;
        m_anchorMax = m_rect.anchorMax;
        m_startScale = m_rect.localScale;

        // Something on the panel itself has to take the mouse, for dragging by its edges and empty space
        UnityEngine.UI.Graphic background = GetComponent<UnityEngine.UI.Graphic>();
        if (!background)
        {
            UnityEngine.UI.Image clear = gameObject.AddComponent<UnityEngine.UI.Image>();
            clear.color = new Color(0, 0, 0, 0);
        }
        else
            background.raycastTarget = true;

        MakeGrip();

        m_shift = new Vector2(PlayerPrefs.GetFloat(m_prefKey + ".X", 0), PlayerPrefs.GetFloat(m_prefKey + ".Y", 0));
        m_scale = Mathf.Clamp(PlayerPrefs.GetFloat(m_prefKey + ".Scale", 1), MinScale, MaxScale);
        Apply();
    }

    void LateUpdate()
    {
        // The window changed size: make sure it's still all on screen
        Vector2 screen = new Vector2(Screen.width, Screen.height);
        if (screen != m_screenSize)
        {
            m_screenSize = screen;
            Apply();
        }
    }

    public void ResetPlace()
    {
        m_shift = Vector2.zero;
        m_scale = 1;
        foreach (string key in new[] { ".X", ".Y", ".Scale" })
            PlayerPrefs.DeleteKey(m_prefKey + key);
        PlayerPrefs.Save();
        Apply();
    }

    void Save()
    {
        PlayerPrefs.SetFloat(m_prefKey + ".X", m_shift.x);
        PlayerPrefs.SetFloat(m_prefKey + ".Y", m_shift.y);
        PlayerPrefs.SetFloat(m_prefKey + ".Scale", m_scale);
        PlayerPrefs.Save();
    }

    void Apply()
    {
        m_rect.anchorMin = m_anchorMin + m_shift;
        m_rect.anchorMax = m_anchorMax + m_shift;
        m_rect.localScale = m_startScale * m_scale;
        KeepOnScreen();
    }

    RectTransform parent { get { return (RectTransform)m_rect.parent; } }

    // Moves it by a distance in world units, as a change of anchors
    void MoveBy(Vector3 world)
    {
        Vector2 local = parent.InverseTransformVector(world);
        Vector2 size = parent.rect.size;
        if (size.x <= 0 || size.y <= 0)
            return;
        m_shift += new Vector2(local.x / size.x, local.y / size.y);
        m_rect.anchorMin = m_anchorMin + m_shift;
        m_rect.anchorMax = m_anchorMax + m_shift;
    }

    void KeepOnScreen()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (!canvas)
            return;
        RectTransform root = (RectTransform)canvas.rootCanvas.transform;

        Vector3[] panel = new Vector3[4];
        Vector3[] screen = new Vector3[4];
        m_rect.GetWorldCorners(panel);      // Bottom left, top left, top right, bottom right
        root.GetWorldCorners(screen);
        screen[1].y -= (screen[1].y - screen[0].y) * TopMargin;

        // Panels may hang off the sides and bottom (Moonscraper's own Settings panel starts partly off the left
        // edge), as long as enough stays on screen to grab. The top stays below the menu bar so the title shows.
        float keep = KeepVisible * root.lossyScale.x;
        Vector3 move = Vector3.zero;
        if (panel[2].x < screen[0].x + keep)
            move.x = screen[0].x + keep - panel[2].x;
        if (panel[0].x > screen[2].x - keep)
            move.x = screen[2].x - keep - panel[0].x;
        if (panel[1].y < screen[0].y + keep)
            move.y = screen[0].y + keep - panel[1].y;
        if (panel[1].y > screen[1].y)
            move.y = screen[1].y - panel[1].y;

        if (move != Vector3.zero)
            MoveBy(move);
    }

    Vector3 ScreenToWorld(Vector2 screenPoint, Camera camera)
    {
        Vector3 world;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(m_rect, screenPoint, camera, out world);
        return world;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        m_dragging = eventData.button == PointerEventData.InputButton.Left;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!m_dragging)
            return;

        Vector3 now = ScreenToWorld(eventData.position, eventData.pressEventCamera);
        Vector3 before = ScreenToWorld(eventData.position - eventData.delta, eventData.pressEventCamera);
        MoveBy(now - before);
        KeepOnScreen();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!m_dragging)
            return;
        m_dragging = false;
        Save();
    }

    // Scaling from the grip keeps the top left corner where it is
    Vector3 m_gripTopLeft;
    Vector2 m_gripStartMouse;
    float m_gripStartScale;

    public void BeginScale(PointerEventData eventData)
    {
        Vector3[] corners = new Vector3[4];
        m_rect.GetWorldCorners(corners);
        m_gripTopLeft = corners[1];
        m_gripStartMouse = eventData.position;
        m_gripStartScale = m_scale;
    }

    public void Scale(PointerEventData eventData)
    {
        Vector2 topLeft = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, m_gripTopLeft);
        Vector2 from = m_gripStartMouse - topLeft;
        Vector2 to = eventData.position - topLeft;
        if (from.sqrMagnitude < 1)
            return;

        float ratio = Vector2.Dot(to, from) / from.sqrMagnitude;
        m_scale = Mathf.Clamp(m_gripStartScale * ratio, MinScale, MaxScale);
        m_rect.localScale = m_startScale * m_scale;

        Vector3[] corners = new Vector3[4];
        m_rect.GetWorldCorners(corners);
        MoveBy(m_gripTopLeft - corners[1]);
        KeepOnScreen();
    }

    public void EndScale()
    {
        Save();
    }

    void MakeGrip()
    {
        GameObject made = new GameObject("Resize Grip", typeof(RectTransform));
        made.layer = gameObject.layer;
        RectTransform rect = (RectTransform)made.transform;
        rect.SetParent(m_rect, false);
        rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
        rect.pivot = new Vector2(1, 0);
        rect.anchoredPosition = new Vector2(-2, 2);
        rect.sizeDelta = new Vector2(GripSize, GripSize);
        rect.SetAsLastSibling();

        UnityEngine.UI.Image image = made.AddComponent<UnityEngine.UI.Image>();
        image.sprite = GripSprite();
        image.color = new Color(1, 1, 1, 0.6f);
        UITheme.KeepColor(image);
        made.AddComponent<MovablePanelGrip>().panel = this;
    }

    static Sprite s_gripSprite;

    // Three diagonal lines in the corner, like a window's resize grip
    static Sprite GripSprite()
    {
        if (s_gripSprite)
            return s_gripSprite;

        const int Size = 16;
        Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color[] pixels = new Color[Size * Size];
        for (int y = 0; y < Size; ++y)
        {
            for (int x = 0; x < Size; ++x)
            {
                int line = x - y;
                bool on = line >= 3 && line % 4 != 1 && line % 4 != 2;
                pixels[y * Size + x] = on ? Color.white : new Color(1, 1, 1, 0);
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        s_gripSprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(0.5f, 0.5f));
        s_gripSprite.name = "Resize Grip";
        return s_gripSprite;
    }
}

/// <summary>The corner grip that scales a movable panel.</summary>
public class MovablePanelGrip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public MovablePanel panel;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (panel)
            panel.BeginScale(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (panel)
            panel.Scale(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (panel)
            panel.EndScale();
    }
}
