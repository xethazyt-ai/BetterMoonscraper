// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drawn with the formatted copy of a Song Properties field (SongPropertiesFormatBar): the selection highlight and
/// caret where the formatted letters really are (bold and bigger letters are wider than the plain ones the field
/// measures), and the underline and strikethrough lines uGUI Text can't draw itself.
/// One sits behind the formatted text for the highlight, and one in front of it for the caret and the lines.
/// </summary>
public class FormattedTextMarks : MaskableGraphic
{
    readonly List<Rect> m_rects = new List<Rect>();
    readonly List<Color> m_colors = new List<Color>();

    public void Clear()
    {
        if (m_rects.Count == 0)
            return;
        m_rects.Clear();
        m_colors.Clear();
        SetVerticesDirty();
    }

    public void Add(Rect rect, Color color)
    {
        m_rects.Add(rect);
        m_colors.Add(color);
    }

    public void Done()
    {
        if (material != defaultMaterial)
            material = null;    // The menu color's material would turn the blue highlight pink
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        for (int i = 0; i < m_rects.Count; ++i)
        {
            Rect rect = m_rects[i];
            Color32 color = m_colors[i];
            int start = vh.currentVertCount;
            vh.AddVert(new Vector3(rect.xMin, rect.yMin), color, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMin, rect.yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMax, rect.yMax), color, Vector2.zero);
            vh.AddVert(new Vector3(rect.xMax, rect.yMin), color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }

    /// <summary>A mark the same size and place as the text, drawn just before it (behind) or just after it</summary>
    public static FormattedTextMarks Make(Text text, string name, bool behind)
    {
        GameObject markObject = new GameObject(name, typeof(RectTransform));
        markObject.layer = text.gameObject.layer;
        RectTransform rect = (RectTransform)markObject.transform;
        rect.SetParent(text.transform.parent, false);
        rect.SetSiblingIndex(text.transform.GetSiblingIndex() + (behind ? 0 : 1));
        FormattedTextMarks marks = markObject.AddComponent<FormattedTextMarks>();
        marks.raycastTarget = false;
        UITheme.KeepColor(marks);     // Drawn in exactly the colors given, in any theme or menu color
        marks.Follow(text);
        return marks;
    }

    /// <summary>Keeps it exactly over the text, which moves and shrinks to fit long names</summary>
    public void Follow(Text text)
    {
        RectTransform ours = rectTransform, theirs = text.rectTransform;
        if (ours.anchorMin != theirs.anchorMin) ours.anchorMin = theirs.anchorMin;
        if (ours.anchorMax != theirs.anchorMax) ours.anchorMax = theirs.anchorMax;
        if (ours.pivot != theirs.pivot) ours.pivot = theirs.pivot;
        if (ours.offsetMin != theirs.offsetMin) ours.offsetMin = theirs.offsetMin;
        if (ours.offsetMax != theirs.offsetMax) ours.offsetMax = theirs.offsetMax;
        if (ours.localScale != theirs.localScale) ours.localScale = theirs.localScale;
    }
}
