// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using MoonscraperChartEditor.Song;

/// <summary>
/// Other people's selections, drawn with the same yellow boxes Moonscraper puts around your own selected notes, and the
/// box they're dragging with the select tool: green while selecting, red while deselecting (a Ctrl drag).
/// The same colors for everyone, like Moonscraper's own.
/// </summary>
public class DuetHighlights
{
    const int MaxHighlights = 512;  // Drawn at once, across everyone. Only what's on screen counts.

    // As Moonscraper's own select tool colors its box
    static readonly Color SelectingColor = Color.green;
    static readonly Color DeselectingColor = Color.red;     // A Ctrl drag

    readonly GameObject m_highlightTemplate;
    readonly GroupSelect m_groupSelect;
    readonly Transform m_root;
    readonly List<GameObject> m_highlights = new List<GameObject>();
    readonly List<SpriteRenderer> m_dragBoxes = new List<SpriteRenderer>();
    int m_highlightsUsed = 0;
    int m_dragBoxesUsed = 0;

    public DuetHighlights(GameObject highlightTemplate, GroupSelect groupSelect)
    {
        m_highlightTemplate = highlightTemplate;
        m_groupSelect = groupSelect;
        m_root = new GameObject("Duet Highlights").transform;
    }

    public void Begin()
    {
        m_highlightsUsed = 0;
        m_dragBoxesUsed = 0;
    }

    // Highlights whatever in this selection is on screen. Call between Begin and End.
    public void ShowSelection(DuetSelection selection, bool sameChart)
    {
        ChartEditor editor = ChartEditor.Instance;
        Song song = editor.currentSong;
        Chart chart = editor.currentChart;
        if (m_highlightTemplate == null || song == null || chart == null)
            return;

        foreach (DuetSelection.Item item in selection.items)
        {
            if (m_highlightsUsed >= MaxHighlights)
                return;

            bool chartObject = DuetSelection.IsChartObject(item.classID);
            if (chartObject && !sameChart)
                continue;   // Notes on another chart aren't on this highway

            SongObject songObject = chartObject ? Find(selection, item, chart.chartObjects)
                : item.classID == (int)SongObject.ID.BPM || item.classID == (int)SongObject.ID.TimeSignature ? Find(selection, item, song.syncTrack)
                : Find(selection, item, song.eventsAndSections);

            // Only objects on screen have a controller showing them
            if (songObject == null || songObject.controller == null || !songObject.controller.gameObject.activeSelf)
                continue;

            ShowHighlight(songObject.controller);
        }
    }

    public void ShowDrag(DuetPresence presence)
    {
        ChartEditor editor = ChartEditor.Instance;
        if (!presence.hasDrag || m_groupSelect == null || editor.currentSong == null)
            return;

        SpriteRenderer box = m_dragBoxesUsed < m_dragBoxes.Count ? m_dragBoxes[m_dragBoxesUsed] : AddDragBox();
        ++m_dragBoxesUsed;
        if (box == null)
            return;

        // Placed the way the select tool places its own box
        float y1 = editor.currentSong.TickToWorldYPosition(presence.dragTick1);
        float y2 = editor.currentSong.TickToWorldYPosition(presence.dragTick2);
        Transform transform = box.transform;
        transform.localScale = new Vector3(Mathf.Abs(presence.dragX1 - presence.dragX2), Mathf.Abs(y1 - y2), m_groupSelect.transform.localScale.z);
        transform.position = new Vector3((presence.dragX1 + presence.dragX2) / 2, (y1 + y2) / 2, m_groupSelect.transform.position.z);

        Color color = presence.dragRemoving ? DeselectingColor : SelectingColor;
        color.a = m_groupSelect.GetComponent<SpriteRenderer>().color.a;
        box.color = color;

        if (!box.gameObject.activeSelf)
            box.gameObject.SetActive(true);
    }

    public void End()
    {
        for (int i = m_highlightsUsed; i < m_highlights.Count; ++i)
        {
            if (m_highlights[i].activeSelf)
                m_highlights[i].SetActive(false);
        }

        for (int i = m_dragBoxesUsed; i < m_dragBoxes.Count; ++i)
        {
            if (m_dragBoxes[i] != null && m_dragBoxes[i].gameObject.activeSelf)
                m_dragBoxes[i].gameObject.SetActive(false);
        }
    }

    public void HideAll()
    {
        Begin();
        End();
    }

    public void Destroy()
    {
        if (m_root != null)
            Object.Destroy(m_root.gameObject);
        foreach (SpriteRenderer box in m_dragBoxes)
        {
            if (box != null)
                Object.Destroy(box.gameObject);
        }
        m_highlights.Clear();
        m_dragBoxes.Clear();
    }

    static SongObject Find<T>(DuetSelection selection, DuetSelection.Item item, IList<T> objects) where T : SongObject
    {
        int start, length;
        SongObjectHelper.FindObjectsAtPosition(item.tick, objects, out start, out length);
        for (int i = start; i < start + length; ++i)
        {
            if (selection.Matches(item, objects[i]))
                return objects[i];
        }
        return null;
    }

    // As SelectedHighlightDisplaySystem does for your own selection
    void ShowHighlight(SongObjectController controller)
    {
        Collider collider3d = controller.GetComponent<Collider>();
        Collider2D collider = controller.GetComponent<Collider2D>();
        if (collider3d == null && collider == null)
            return;

        Bounds bounds = collider3d != null ? collider3d.bounds : collider.bounds;
        if (bounds.size.z == 0)
        {
            Vector3 size = bounds.size;
            size.z = 0.1f;
            bounds.size = size;
        }

        GameObject highlight;
        if (m_highlightsUsed < m_highlights.Count)
        {
            highlight = m_highlights[m_highlightsUsed];
        }
        else
        {
            highlight = Object.Instantiate(m_highlightTemplate, m_root);
            highlight.name = "Duet Highlight";
            m_highlights.Add(highlight);
        }
        ++m_highlightsUsed;

        highlight.transform.localPosition = bounds.center;
        highlight.transform.localScale = bounds.size;
        if (!highlight.activeSelf)
            highlight.SetActive(true);
    }

    // A box that looks like the select tool's, without any of its scripts
    SpriteRenderer AddDragBox()
    {
        SpriteRenderer source = m_groupSelect.GetComponent<SpriteRenderer>();
        if (source == null)
        {
            m_dragBoxes.Add(null);
            return null;
        }

        GameObject go = new GameObject("Duet Drag Box");
        go.transform.SetParent(m_groupSelect.transform.parent, false);
        go.layer = m_groupSelect.gameObject.layer;

        SpriteRenderer box = go.AddComponent<SpriteRenderer>();
        box.sprite = source.sprite;
        box.sharedMaterial = source.sharedMaterial;
        box.sortingLayerID = source.sortingLayerID;
        box.sortingOrder = source.sortingOrder;
        box.drawMode = source.drawMode;
        if (source.drawMode != SpriteDrawMode.Simple)
            box.size = source.size;

        m_dragBoxes.Add(box);
        return box;
    }
}
