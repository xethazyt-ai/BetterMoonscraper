// See LICENSE in project root for license information.

using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A small frames-per-second box, shown and hidden with F4 (rebindable in Controls). It works like the Collab Mode
/// boxes: drag it by its title, resize it from any corner, it stays where it was left between runs, it hides while a
/// Moonscraper menu is open over it, and Reset Menu Positions puts it back.
/// </summary>
public class FpsCounter : MonoBehaviour
{
    const int WindowId = 0x4650;    // Any number unique among IMGUI windows
    const string PrefVisible = "FpsCounter.Visible";
    const float SampleSeconds = 0.5f;

    static FpsCounter s_instance;

    readonly DuetWindow m_window = new DuetWindow(WindowId, "FpsCounter", new Rect(0, 0, 150, 10), new Vector2(110, 60), false, false);
    ChartEditor m_editor;
    bool m_visible;

    // Averaged over half a second so the number can be read; the slowest frame in that time is shown too
    int m_frames;
    float m_elapsed;
    float m_slowest;
    string m_fpsText = "FPS: -";
    string m_frameText = string.Empty;

    public static bool IsPointerOver()
    {
        if (s_instance == null || !s_instance.m_visible || DuetPanel.isHudHidden)
            return false;

        float scale = DuetPanel.GuiScale;
        Vector2 pointer = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
        return s_instance.m_window.rect.Contains(pointer) && !DuetMenuCover.Covers(s_instance.m_window.rect, scale);
    }

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
            editor.gameObject.AddComponent<FpsCounter>();
    }

    void Awake()
    {
        s_instance = this;
        m_editor = GetComponent<ChartEditor>();
        m_visible = PlayerPrefs.GetInt(PrefVisible, 0) != 0;
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    void Update()
    {
        if (!Services.IsTyping && MSChartEditorInput.GetInputDown(MSChartEditorInputActions.FpsCounter))
        {
            m_visible = !m_visible;
            PlayerPrefs.SetInt(PrefVisible, m_visible ? 1 : 0);
            PlayerPrefs.Save();
            m_frames = 0;
            m_elapsed = 0;
            m_slowest = 0;
        }

        if (!m_visible)
            return;

        float frame = Time.unscaledDeltaTime;
        ++m_frames;
        m_elapsed += frame;
        m_slowest = Mathf.Max(m_slowest, frame);
        if (m_elapsed >= SampleSeconds)
        {
            m_fpsText = "FPS: " + Mathf.RoundToInt(m_frames / m_elapsed);
            m_frameText = string.Format("{0:0.0} ms (slowest {1:0.0})", m_elapsed / m_frames * 1000, m_slowest * 1000);
            m_frames = 0;
            m_elapsed = 0;
            m_slowest = 0;
        }
    }

    void OnGUI()
    {
        if (!m_visible || m_editor == null || DuetPanel.isHudHidden)
            return;

        float scale = DuetPanel.GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        if (!m_window.isPlaced)
            m_window.PlaceOnce(new Vector2(Screen.width / scale - m_window.rect.width - 20, 80));
        if (!DuetMenuCover.Covers(m_window.rect, scale))
            m_window.Draw("FPS", DrawWindow, scale);
    }

    void DrawWindow(int id)
    {
        GUILayout.Label(m_fpsText);
        if (m_frameText.Length > 0)
            GUILayout.Label(m_frameText);
    }
}
