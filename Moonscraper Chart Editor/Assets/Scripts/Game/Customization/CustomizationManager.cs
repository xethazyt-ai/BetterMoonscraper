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
    float m_videoAspect;        // Width over height of the song video showing as the background, or 0 when none is

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
        editor.gameObject.AddComponent<SongVideoBackground>();
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

        UITheme.Apply();
        ChartEditor.Instance.events.editorStateChangedEvent.Register(OnEditorStateChanged);
    }

    Coroutine m_titleBarMatch;

    /// <summary>
    /// In the Dark theme, colors the Windows title bar like the menu bar under it, read off the screen so it matches
    /// whatever the menu bar ends up looking like
    /// </summary>
    public void MatchTitleBarToMenuBar(bool dark)
    {
        if (m_titleBarMatch != null)
            StopCoroutine(m_titleBarMatch);
        m_titleBarMatch = null;

        if (!dark)
        {
            DarkTitleBar.SetColor(null);
            return;
        }
        if (isActiveAndEnabled)
            m_titleBarMatch = StartCoroutine(MatchTitleBarNextFrame());
    }

    IEnumerator MatchTitleBarNextFrame()
    {
        yield return null;  // Let the recolored menu bar draw first
        yield return new WaitForEndOfFrame();

        // A few spots along the menu bar's empty right-hand side, a little below the window's top edge
        Texture2D pixel = new Texture2D(1, 1, TextureFormat.RGB24, false);
        System.Collections.Generic.List<Color> samples = new System.Collections.Generic.List<Color>();
        try
        {
            for (int i = 0; i < 5; ++i)
            {
                int x = Mathf.Clamp((int)(Screen.width * (0.72f + i * 0.05f)), 0, Screen.width - 1);
                int y = Mathf.Clamp(Screen.height - 6, 0, Screen.height - 1);
                pixel.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
                samples.Add(pixel.GetPixel(0, 0));
            }
        }
        finally
        {
            Destroy(pixel);
        }

        // The middle one by brightness, so a stray letter or icon doesn't count
        samples.Sort((a, b) => (a.r + a.g + a.b).CompareTo(b.r + b.g + b.b));
        DarkTitleBar.SetColor(samples[samples.Count / 2]);
        m_titleBarMatch = null;
    }

    // Menus can build parts of themselves the first time they open, so theme whatever is new
    void OnEditorStateChanged(in ChartEditor.State state)
    {
        if (UITheme.current != UITheme.Theme.Default && !QuitState.isQuitting)
            StartCoroutine(ThemeNewUINextFrame());
    }

    IEnumerator ThemeNewUINextFrame()
    {
        yield return null;
        if (ChartEditor.InstanceExists)
            UITheme.ApplyToNew(ChartEditor.Instance.uiServices.gameObject);
    }

    void Update()
    {
        if (m_highwayImage != null)
            m_highwayImage.Tick(Time.unscaledDeltaTime);
        if (m_backgroundImage != null)
            m_backgroundImage.Tick(Time.unscaledDeltaTime);
        if (m_videoAspect > 0)
            FitVideo();
    }

    /// <summary>
    /// Shows the song's video instead of the background, filling the screen without stretching it. ApplyBackground
    /// goes back to the picked or default background.
    /// </summary>
    public void ShowVideoBackground(Texture video, float aspect)
    {
        m_videoAspect = aspect;
        foreach (BackgroundBlending background in m_backgrounds)
        {
            if (background)
                background.SetBackgroundOverride(video);
        }
        FitVideo();
    }

    float m_fittedScreenAspect;

    void FitVideo()
    {
        float screenAspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 1;
        if (Mathf.Approximately(screenAspect, m_fittedScreenAspect))
            return;
        m_fittedScreenAspect = screenAspect;

        // Crop whichever way the video is too wide or too tall for the window
        Vector2 scale = Vector2.one;
        if (m_videoAspect > screenAspect)
            scale.x = screenAspect / m_videoAspect;
        else
            scale.y = m_videoAspect / screenAspect;
        Vector2 offset = (Vector2.one - scale) / 2;

        foreach (BackgroundBlending background in m_backgrounds)
        {
            if (background)
                background.SetTextureCrop(scale, offset);
        }
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
        if (ChartEditor.InstanceExists)
            ChartEditor.Instance.events.editorStateChangedEvent.Deregister(OnEditorStateChanged);

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
        m_videoAspect = 0;
        m_fittedScreenAspect = 0;

        string path = CustomizationPrefs.GetStored(CustomizationPrefs.Kind.Background);
        if (path != null)
            error = TryLoad(path, TextureWrapMode.Clamp, out m_backgroundImage);

        foreach (BackgroundBlending background in m_backgrounds)
        {
            if (background)
                background.SetBackgroundOverride(m_backgroundImage != null ? m_backgroundImage.texture : null);
        }
        DisposeImage(ref old);

        // The song's video stays in front of whatever background is picked
        if (SongVideoBackground.instance)
            SongVideoBackground.instance.ShowAgain();

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
