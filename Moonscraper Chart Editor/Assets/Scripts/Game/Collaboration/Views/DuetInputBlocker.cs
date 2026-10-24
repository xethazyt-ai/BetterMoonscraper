// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stops clicks and scrolls on a Duet window from reaching the Moonscraper buttons, panels and highway underneath.
/// The Duet windows are drawn with Unity's immediate mode GUI, which Moonscraper's own UI doesn't know about, so this
/// puts an invisible panel in Moonscraper's UI, in front of everything, wherever a Duet window is showing.
/// </summary>
public class DuetInputBlocker : MonoBehaviour
{
    class Wall
    {
        public RectTransform rectTransform;
        public Rect screenRect;     // Pixels from the top left
        public int frame;
    }

    static DuetInputBlocker s_instance;
    readonly Dictionary<int, Wall> m_walls = new Dictionary<int, Wall>();
    Canvas m_canvas;

    // Call from OnGUI every time the window with this id is drawn, with its rect in scaled GUI units
    public static void Block(int id, Rect guiRect, float scale)
    {
        if (s_instance == null)
            s_instance = new GameObject("Duet Input Blocker").AddComponent<DuetInputBlocker>();

        Wall wall;
        if (!s_instance.m_walls.TryGetValue(id, out wall))
            s_instance.m_walls[id] = wall = s_instance.MakeWall(id);

        wall.screenRect = new Rect(guiRect.x * scale, guiRect.y * scale, guiRect.width * scale, guiRect.height * scale);
        wall.frame = Time.frameCount;
    }

    void Awake()
    {
        m_canvas = gameObject.AddComponent<Canvas>();
        m_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        m_canvas.sortingOrder = short.MaxValue;     // In front of all of Moonscraper's UI
        gameObject.AddComponent<GraphicRaycaster>();
    }

    Wall MakeWall(int id)
    {
        GameObject go = new GameObject("Wall " + id, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        Image image = go.AddComponent<Image>();
        image.color = new Color(0, 0, 0, 0);    // Invisible, but still catches the mouse
        image.raycastTarget = true;

        RectTransform rectTransform = (RectTransform)go.transform;
        rectTransform.anchorMin = rectTransform.anchorMax = rectTransform.pivot = Vector2.zero;
        return new Wall { rectTransform = rectTransform };
    }

    void LateUpdate()
    {
        float pixels = m_canvas.scaleFactor > 0 ? m_canvas.scaleFactor : 1;
        foreach (Wall wall in m_walls.Values)
        {
            // A window that wasn't drawn last frame is closed or hidden, so its wall goes too
            bool shown = wall.frame >= Time.frameCount - 1;
            if (wall.rectTransform.gameObject.activeSelf != shown)
                wall.rectTransform.gameObject.SetActive(shown);

            if (shown)
            {
                Rect r = wall.screenRect;
                wall.rectTransform.anchoredPosition = new Vector2(r.x, Screen.height - r.yMax) / pixels;
                wall.rectTransform.sizeDelta = new Vector2(r.width, r.height) / pixels;
            }
        }
    }
}
