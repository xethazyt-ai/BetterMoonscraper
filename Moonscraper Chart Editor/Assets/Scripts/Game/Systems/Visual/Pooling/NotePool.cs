// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using UnityEngine;
using MoonscraperChartEditor.Song;

public class NotePool : SongObjectPool
{
    public NotePool(GameObject parent, GameObject prefab, int initialSize) : base(parent, prefab, initialSize)
    {
        if (!prefab.GetComponentInChildren<NoteController>())
            throw new System.Exception("No NoteController attached to prefab");   
    }

    // Enough for the densest real charts on a long highway; the notes nearest the strikeline come first
    protected override int maxSize { get { return 6000; } }

    protected override void Assign(SongObjectController sCon, SongObject songObject)
    {
        NoteController controller = sCon as NoteController;

        // Assign pooled objects
        controller.note = (Note)songObject;
        controller.Activate();
        controller.gameObject.SetActive(true);
    }
}
