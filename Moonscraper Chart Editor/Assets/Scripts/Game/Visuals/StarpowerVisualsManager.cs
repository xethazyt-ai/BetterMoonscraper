using UnityEngine;
using MoonscraperChartEditor.Song;

[RequireComponent(typeof(Renderer))]
public class StarpowerVisualsManager : MonoBehaviour
{
    [SerializeField]
    StarpowerController spCon;
    Renderer spRenderer;

    [SerializeField]
    MeshNoteResources resources;
    Material[] resourceSharedMatsSp;
    Material[] resourceSharedMatsSpDrumFill;

    // Use this for initialization
    void Awake()
    {
        if (spRenderer)
            return;

        spRenderer = GetComponent<Renderer>();
        resourceSharedMatsSp = resources.starpowerMaterials;
        resourceSharedMatsSpDrumFill = resources.starpowerDrumFillMaterials;
    }

    // The star's cyan base and middle (materials 2 and 3)
    static readonly int[] s_colorSlots = { 2, 3 };
    Material[] m_profileMats;

    Material[] ProfileColored(Material[] materials)
    {
        Chart.GameMode gameMode = ChartEditor.Instance.currentGameMode;
        Material[] colored = null;
        foreach (int slot in s_colorSlots)
        {
            if (slot >= materials.Length)
                continue;
            Material recolored = CustomNoteColors.StarPower(gameMode, materials[slot], false);
            if (recolored == materials[slot])
                continue;
            if (colored == null)
                colored = m_profileMats != null && m_profileMats.Length == materials.Length ? m_profileMats : new Material[materials.Length];
            colored[slot] = recolored;
        }
        if (colored == null)
            return materials;

        for (int i = 0; i < materials.Length; ++i)
        {
            if (System.Array.IndexOf(s_colorSlots, i) < 0 || !colored[i])
                colored[i] = materials[i];
        }
        m_profileMats = colored;
        return colored;
    }

    public void UpdateVisuals()
    {
        if (!spRenderer)
            Awake();

        Starpower sp = spCon.starpower;
        if (sp != null)
        {
            Starpower.Flags flags = sp.flags;
            if (flags.HasFlag(Starpower.Flags.ProDrums_Activation))
            {
                spRenderer.sharedMaterials = resourceSharedMatsSpDrumFill;
            }
            else
            {
                // Default visuals, with the cyan parts in the color profile's Star Power color when it has one
                spRenderer.sharedMaterials = ProfileColored(resourceSharedMatsSp);
            }
        }
    }
}
