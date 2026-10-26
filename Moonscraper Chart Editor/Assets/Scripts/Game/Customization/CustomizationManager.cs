// See LICENSE in project root for license information.

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies the color profile, highway and background picked in Settings > Customize, when the editor opens and
/// whenever they change. Only this computer's view changes; none of it goes to anyone in Collab Mode.
/// </summary>
public class CustomizationManager : MonoBehaviour
{
    public static CustomizationManager instance { get; private set; }

    Renderer m_highway;
    Texture m_defaultHighwayTexture;
    BackgroundBlending[] m_backgrounds = new BackgroundBlending[0];     // The editor scene has more than one

    CustomImage m_highwayImage;
    CustomImage m_backgroundImage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => AttachToEditor();
        AttachToEditor();
    }

    static void AttachToEditor()
    {
        if (instance)
            return;

        ChartEditor editor = FindObjectOfType<ChartEditor>();
        if (!editor)
            return;     // Still on the loading screen

        editor.gameObject.AddComponent<CustomizationManager>();
        NoteFlames.AttachTo(editor.gameObject);
        CustomizationSettingsTab.AttachToSettingsMenus();
    }

    void Awake()
    {
        instance = this;
    }

    IEnumerator Start()
    {
        yield return null;      // Let the highway and background set up Moonscraper's own textures first

        AssignCustomResources highwaySetup = FindObjectOfType<AssignCustomResources>();
        if (highwaySetup && highwaySetup.fretboard)
        {
            m_highway = highwaySetup.fretboard;
            m_defaultHighwayTexture = m_highway.sharedMaterial.mainTexture;
        }
        m_backgrounds = FindObjectsOfType<BackgroundBlending>();

        ApplyColorProfile();
        ApplyHighway();
        ApplyBackground();
    }

    void Update()
    {
        if (m_highwayImage != null)
            m_highwayImage.Tick(Time.unscaledDeltaTime);
        if (m_backgroundImage != null)
            m_backgroundImage.Tick(Time.unscaledDeltaTime);
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;

        // The highway material is a shared asset, so put it back (it would otherwise stick in the Unity editor)
        if (m_highway)
            m_highway.sharedMaterial.mainTexture = m_defaultHighwayTexture;

        CustomNoteColors.Clear();
        DisposeImage(ref m_highwayImage);
        DisposeImage(ref m_backgroundImage);
    }

    /// <summary>
    /// Loads the saved color profile, or goes back to Moonscraper's colors. Returns an error to show, or null.
    /// </summary>
    public string ApplyColorProfile()
    {
        string error = null;
        string path = CustomizationPrefs.GetStored(CustomizationPrefs.Kind.ColorProfile);

        if (path != null)
        {
            try
            {
                CloneHeroColorProfile profile = CloneHeroColorProfile.Load(path);
                if (profile.count == 0)
                    throw new FormatException("It has no colors in it");
                CustomNoteColors.Apply(profile);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Couldn't load the color profile " + path + ". " + e.Message);
                error = "Couldn't read that color profile. " + e.Message;
                CustomNoteColors.Clear();
            }
        }
        else
        {
            CustomNoteColors.Clear();
        }

        RefreshNotes();
        return error;
    }

    public string ApplyHighway()
    {
        if (!m_highway)
            return null;

        string error = null;
        DisposeImage(ref m_highwayImage);
        m_highway.sharedMaterial.mainTexture = m_defaultHighwayTexture;

        string path = CustomizationPrefs.GetStored(CustomizationPrefs.Kind.Highway);
        if (path != null)
        {
            error = TryLoad(path, TextureWrapMode.Repeat, out m_highwayImage);
            if (m_highwayImage != null)
                m_highway.sharedMaterial.mainTexture = m_highwayImage.texture;
        }

        return error;
    }

    public string ApplyBackground()
    {
        if (m_backgrounds.Length == 0)
            return null;

        string error = null;
        CustomImage old = m_backgroundImage;
        m_backgroundImage = null;

        string path = CustomizationPrefs.GetStored(CustomizationPrefs.Kind.Background);
        if (path != null)
            error = TryLoad(path, TextureWrapMode.Clamp, out m_backgroundImage);

        foreach (BackgroundBlending background in m_backgrounds)
        {
            if (background)
                background.SetBackgroundOverride(m_backgroundImage != null ? m_backgroundImage.texture : null);
        }
        DisposeImage(ref old);

        return error;
    }

    static string TryLoad(string path, TextureWrapMode wrapMode, out CustomImage image)
    {
        image = null;
        try
        {
            image = CustomImage.Load(path, wrapMode);
            return null;
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't load the image " + path + ". " + e.Message);
            return "Couldn't load that image. " + e.Message;
        }
    }

    static void DisposeImage(ref CustomImage image)
    {
        if (image != null)
            image.Dispose();
        image = null;
    }

    // Redraw the notes and strikers with the new colors
    static void RefreshNotes()
    {
        if (!ChartEditor.InstanceExists)
            return;

        ChartEditor editor = ChartEditor.Instance;

        if (editor.songObjectPoolManager)
            editor.songObjectPoolManager.SetAllPoolsDirty();

        if (editor.laneInfo)
            editor.events.lanesChangedEvent.Fire(editor.laneInfo.laneCount);
    }
}
