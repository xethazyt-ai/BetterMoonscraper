// See LICENSE in project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Preview Mode (F11 unless rebound in Controls): BetterMoonscraper goes fullscreen and shows only the highway, the
/// notes and the background, like watching the chart in Clone Hero. Every menu, panel and window is hidden, along
/// with BPMs, time signatures, sections, events, star power bars, the note cursor and selection boxes. Space still
/// plays and stops. Press the key again or Escape to go back to editing as before.
/// </summary>
public class PreviewMode : MonoBehaviour
{
    public static bool isActive { get; private set; }
    public static int lastToggleFrame { get; private set; }    // The frame Preview Mode last went on or off

    static PreviewMode s_instance;
    ChartEditor m_editor;

    readonly List<Canvas> m_hiddenCanvases = new List<Canvas>();
    readonly List<GameObject> m_hiddenObjects = new List<GameObject>();
    EditorObjectToolManager.ToolID m_savedTool = EditorObjectToolManager.ToolID.Cursor;
    int m_oldWidth, m_oldHeight;
    FullScreenMode m_oldMode;
    bool m_wasMaximised;
    bool m_cursorWasVisible = true;
    float m_nextCanvasCheck;

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
            editor.gameObject.AddComponent<PreviewMode>();
    }

    void Awake()
    {
        s_instance = this;
        m_editor = GetComponent<ChartEditor>();
    }

    void OnDestroy()
    {
        if (s_instance == this)
        {
            s_instance = null;
            isActive = false;
        }
    }

    void Update()
    {
        if (m_editor == null || QuitState.isQuitting)
            return;

        if (isActive && m_editor.currentState == ChartEditor.State.Menu)
        {
            Exit();     // A menu opened (Escape, or the quit box): bring everything back so it can be used
            return;
        }

        if (isActive && Input.GetKeyDown(KeyCode.Escape))
        {
            Exit();
            return;
        }

        if (!Services.IsTyping && MSChartEditorInput.GetInputDown(MSChartEditorInputActions.PreviewMode))
        {
            if (isActive)
                Exit();
            else if (m_editor.currentState != ChartEditor.State.Menu)
                Enter();
        }
    }

    void LateUpdate()
    {
        if (!isActive)
            return;

        // Anything that switched itself back on (a notification, a panel opened by a key) goes away again
        if (Time.unscaledTime >= m_nextCanvasCheck)
        {
            m_nextCanvasCheck = Time.unscaledTime + 0.2f;
            HideCanvases();
        }
        if (m_editor.toolManager.currentToolId != EditorObjectToolManager.ToolID.None)
        {
            m_savedTool = m_editor.toolManager.currentToolId;
            m_editor.toolManager.ChangeTool(EditorObjectToolManager.ToolID.None);
        }
    }

    void Enter()
    {
        isActive = true;
        lastToggleFrame = Time.frameCount;

        m_savedTool = m_editor.toolManager.currentToolId;
        if (m_savedTool == EditorObjectToolManager.ToolID.None)
            m_savedTool = EditorObjectToolManager.ToolID.Cursor;
        m_editor.toolManager.ChangeTool(EditorObjectToolManager.ToolID.None);    // No note cursor following the mouse

        HideCanvases();
        if (m_editor.songObjectPoolManager)
            m_editor.songObjectPoolManager.SetMarkersVisible(false);
        // The editing tools (note cursor, select boxes, the red paste arrow) and the selection highlights
        foreach (string name in new[] { "Hover Highlights", "Selected Highlight Pool", "Tools", "Group Select Area", "Clipboard" })
        {
            GameObject found = GameObject.Find(name);
            if (found)
            {
                found.SetActive(false);
                m_hiddenObjects.Add(found);
            }
        }

        m_cursorWasVisible = Cursor.visible;
        Cursor.visible = false;

        // Fullscreen at the screen's own resolution, remembering the window to go back to
        m_oldWidth = Screen.width;
        m_oldHeight = Screen.height;
        m_oldMode = Screen.fullScreenMode;
        m_wasMaximised = !Screen.fullScreen && NativeWindowState.IsMaximised();
        Resolution native = Screen.currentResolution;
        if (!Screen.fullScreen)
            Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);

        Debug.Log("Preview Mode on");
    }

    void Exit()
    {
        isActive = false;
        lastToggleFrame = Time.frameCount;

        foreach (Canvas canvas in m_hiddenCanvases)
        {
            if (canvas)
                canvas.enabled = true;
        }
        m_hiddenCanvases.Clear();

        foreach (GameObject hidden in m_hiddenObjects)
        {
            if (hidden)
                hidden.SetActive(true);
        }
        m_hiddenObjects.Clear();

        if (m_editor.songObjectPoolManager)
            m_editor.songObjectPoolManager.SetMarkersVisible(true);

        if (m_editor.toolManager.currentToolId == EditorObjectToolManager.ToolID.None)
            m_editor.toolManager.ChangeTool(m_savedTool);

        Cursor.visible = m_cursorWasVisible;

        if (m_oldMode != FullScreenMode.FullScreenWindow && m_oldMode != FullScreenMode.ExclusiveFullScreen)
        {
            Screen.SetResolution(m_oldWidth, m_oldHeight, m_oldMode);
            if (m_wasMaximised)
                StartCoroutine(MaximiseSoon());
        }

        Debug.Log("Preview Mode off");
    }

    // Unity needs a frame or two to leave fullscreen before the window can be maximised again
    IEnumerator MaximiseSoon()
    {
        yield return null;
        yield return null;
        NativeWindowState.Maximise();
    }

    // Every screen-space and world-space UI root, except the parts of the highway itself (its pooled labels are
    // hidden with their parents)
    void HideCanvases()
    {
        foreach (Canvas canvas in FindObjectsOfType<Canvas>())
        {
            if (canvas.enabled && canvas.isRootCanvas)
            {
                canvas.enabled = false;
                m_hiddenCanvases.Add(canvas);
            }
        }
    }

    // Whether the window is maximised, and maximising it again, on Windows
    static class NativeWindowState
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int command);
        const int SW_MAXIMIZE = 3;

        public static bool IsMaximised()
        {
            try
            {
                IntPtr window = WindowFitOnLaunch.FindOwnWindow();
                return window != IntPtr.Zero && IsZoomed(window);
            }
            catch (Exception) { return false; }
        }

        public static void Maximise()
        {
            try
            {
                IntPtr window = WindowFitOnLaunch.FindOwnWindow();
                if (window != IntPtr.Zero)
                    ShowWindow(window, SW_MAXIMIZE);
            }
            catch (Exception) { }
        }
#else
        public static bool IsMaximised() { return false; }
        public static void Maximise() { }
#endif
    }
}
