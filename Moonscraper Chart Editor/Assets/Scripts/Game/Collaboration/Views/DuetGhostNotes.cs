// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using MoonscraperChartEditor.Song;

/// <summary>
/// Other people's ghost notes, drawn by copies of the editor's own note tool so they look exactly like your own:
/// the current skin's strum, HOPO, tap, star power and open notes. Each has an outline in that person's colour.
/// </summary>
public class DuetGhostNotes
{
    const float OutlineScale = 1.07f;     // A thin line round the note
    const string SilhouetteShaderName = "Duet/Silhouette";

    class Slot
    {
        public NoteController ghost;
        public NoteController outline;
        public Note ghostNote = new Note(0, 0);
        public Note outlineNote = new Note(0, 0);
        public Material outlineMaterial;
    }

    readonly GameObject m_template;
    readonly Transform m_root;
    readonly Shader m_silhouette;
    readonly List<Slot> m_slots = new List<Slot>();
    readonly List<Renderer> m_renderers = new List<Renderer>();
    int m_used = 0;

    public DuetGhostNotes(GameObject template)
    {
        m_template = template;
        m_root = new GameObject("Duet Ghost Notes").transform;
        m_silhouette = Resources.Load<Shader>("DuetSilhouette");
        if (m_silhouette == null)
            m_silhouette = Shader.Find(SilhouetteShaderName);
        if (m_silhouette == null)
            Debug.LogWarning("Duet: the outline shader is missing, so ghost notes won't have an outline");
    }

    public void Begin()
    {
        m_used = 0;
    }

    // Shows one ghost note this frame. Call between Begin and End.
    public void Show(DuetPresence.Ghost ghost, Color color)
    {
        ChartEditor editor = ChartEditor.Instance;
        if (ghost.tick < editor.minPos || ghost.tick > editor.maxPos)
            return;

        Slot slot = m_used < m_slots.Count ? m_slots[m_used] : AddSlot();
        ++m_used;

        Place(slot.ghost, slot.ghostNote, ghost);
        if (slot.outline != null)
        {
            Place(slot.outline, slot.outlineNote, ghost);
            slot.outlineMaterial.color = color;
        }
    }

    public void End()
    {
        for (int i = m_used; i < m_slots.Count; ++i)
        {
            Hide(m_slots[i].ghost);
            Hide(m_slots[i].outline);
        }
    }

    public void HideAll()
    {
        Begin();
        End();
    }

    // The editor redoes each note's materials and draw order every frame, so the outlines are fixed up after it
    public void LateUpdate()
    {
        Camera camera = Camera.main;
        for (int i = 0; i < m_used && i < m_slots.Count; ++i)
        {
            Slot slot = m_slots[i];
            if (slot.outline == null || !slot.outline.gameObject.activeSelf)
                continue;

            int ghostOrder = 0;
            Renderer ghostRenderer = FindNoteRenderer(slot.ghost);
            if (ghostRenderer != null)
                ghostOrder = ghostRenderer.sortingOrder;

            slot.outline.GetComponentsInChildren(false, m_renderers);
            foreach (Renderer renderer in m_renderers)
            {
                if (renderer is LineRenderer || renderer.GetComponent<TMPro.TextMeshPro>() != null)
                {
                    renderer.enabled = false;   // No outline round the sustain or the "Tap" label
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (materials.Length == 0)
                    materials = new Material[1];
                for (int m = 0; m < materials.Length; ++m)
                    materials[m] = slot.outlineMaterial;
                renderer.sharedMaterials = materials;
                renderer.sortingOrder = ghostOrder - 1;     // Behind its note
            }

            // A touch further from the camera than its note, so the note is drawn over it
            if (camera != null)
                slot.outline.transform.position = slot.ghost.transform.position + camera.transform.forward * 0.02f;
        }
    }

    public void Destroy()
    {
        if (m_root != null)
            Object.Destroy(m_root.gameObject);
        foreach (Slot slot in m_slots)
        {
            if (slot.outlineMaterial != null)
                Object.Destroy(slot.outlineMaterial);
        }
        m_slots.Clear();
    }

    static Renderer FindNoteRenderer(NoteController controller)
    {
        if (controller.noteObjectSelector == null)
            return null;
        if (controller.noteObjectSelector.note2D != null && controller.noteObjectSelector.note2D.gameObject.activeSelf)
            return controller.noteObjectSelector.note2D.GetComponent<Renderer>();
        if (controller.noteObjectSelector.note3D != null)
            return controller.noteObjectSelector.note3D.GetComponent<Renderer>();
        return null;
    }

    static void Place(NoteController controller, Note note, DuetPresence.Ghost ghost)
    {
        ChartEditor editor = ChartEditor.Instance;
        note.song = editor.currentSong;
        note.chart = editor.currentChart;
        note.tick = ghost.tick;
        note.rawNote = ghost.rawNote;
        note.flags = (Note.Flags)ghost.flags;
        note.length = 0;
        PlaceNote.UpdatePrevAndNext(note, editor.currentChart);

        if (!controller.gameObject.activeSelf)
            controller.gameObject.SetActive(true);
        controller.note = note;     // Moves it into place and picks the visuals for this note
    }

    static void Hide(NoteController controller)
    {
        if (controller != null && controller.gameObject.activeSelf)
            controller.gameObject.SetActive(false);
    }

    Slot AddSlot()
    {
        Slot slot = new Slot();
        slot.ghost = MakeCopy("Duet Ghost Note");
        if (m_silhouette != null)
        {
            slot.outline = MakeCopy("Duet Ghost Note Outline");
            slot.outline.transform.localScale = new Vector3(OutlineScale, OutlineScale, 1);
            slot.outlineMaterial = new Material(m_silhouette);
        }
        m_slots.Add(slot);
        return slot;
    }

    // A copy of the note tool that only draws: no placing notes, no clicks
    NoteController MakeCopy(string name)
    {
        // Made under a switched off parent so none of its scripts start before they're taken off
        GameObject holder = new GameObject("Duet Ghost Note Holder");
        holder.SetActive(false);
        GameObject copy = Object.Instantiate(m_template, holder.transform);
        copy.name = name;

        foreach (ToolObject tool in copy.GetComponentsInChildren<ToolObject>(true))
            Object.DestroyImmediate(tool);
        foreach (Collider collider in copy.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(collider);
        foreach (Collider2D collider in copy.GetComponentsInChildren<Collider2D>(true))
            Object.DestroyImmediate(collider);

        copy.SetActive(false);
        copy.transform.SetParent(m_root, false);
        Object.Destroy(holder);
        return copy.GetComponent<NoteController>();
    }
}
