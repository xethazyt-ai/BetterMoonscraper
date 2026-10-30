// See LICENSE in project root for license information.

using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The Help > FAQ window's questions and answers. The window's text box was a fixed height, so anything past the
/// first few questions ran off the bottom with no way to scroll; the box now grows to fit, which brings up the scroll
/// bar the window already had. The keys named are the defaults and can be changed in Settings > Controls.
/// </summary>
public class FaqContent : MonoBehaviour
{
    const float Padding = 10;
    const float ScrollSensitivity = 30;

    static readonly string[,] s_questions =
    {
        { "How do I create and edit sustains?",
          "Right-click and drag on a note, a star power phrase or an existing sustain to set its length." },
        { "How do I place open notes?",
          "With the note tool selected, press 0 to switch to open notes. Press one of 1-5 (1-6 in GHL mode) to go back." },
        { "How do I turn notes into HOPOs, strums or taps?",
          "Select the notes and press H for HOPO, S for strum, T for tap or X to put them back to natural. F toggles " +
          "Forced on a single note. Any note can be forced, including the first one in the chart." },
        { "How do I change the step?",
          "Q and W (or the left and right arrow keys) go down and up a step. The current step is shown in the left panel." },
        { "How can I select whole sections?",
          "While dragging a selection with the cursor tool, hold Alt and scroll to jump to the next section, so the " +
          "selection covers all of it. Alt+Up and Alt+Down jump between sections at any time." },
        { "Can I use keyboard controls similar to Feedback?",
          "Yes. Press the ' key or click the toggle in the top right. Notes then go in with keys 1-6 from the note " +
          "tool, and other objects are added and removed with 1 from their own tools." },
        { "Why is part of my chart cut off at the end?",
          "The chart is as long as the song's audio. With no audio loaded, BetterMoonscraper gives the song 5 minutes." },
        { "How do I chart with friends (Collab Mode)?",
          "Press F6 to open Collab Mode. One person hosts and shares the room code; everyone else joins with it and " +
          "gets the song's audio, art and settings from the host. Everyone edits the same chart live, with their own " +
          "undo. Only the host can save, so the host keeps the finished chart." },
        { "What are Pattern Blueprints?",
          "Saved note patterns you can paste anywhere. Press F5 to open the window and save or pick a pattern. " +
          "Ctrl+B pastes it at the mouse (or the strikeline). With notes selected, Ctrl+B stamps those notes instead. " +
          "Pressing Ctrl+B again in the same spot adds another copy after the last one." },
        { "How do I watch the chart like it's in Clone Hero?",
          "Press F11 for Preview Mode: fullscreen with only the highway, notes and background. Space plays and stops. " +
          "Press F11 or Escape to go back to editing." },
        { "How do I slow the song down to chart fast parts?",
          "Use the Speed slider in the left panel. Below 1x the notes spread out so dense parts are easier to place; " +
          "tick Non-Spaced Notes for Slowdown to keep normal spacing. Pitched Speedups changes the pitch with the speed." },
        { "Can I find the BPM automatically?",
          "Tools > Auto Tempo Map listens to the audio and places BPM markers. Always check the result against the song." },
        { "How do I use my Clone Hero colors, highway and background?",
          "Settings > Customize. Pick your Clone Hero folder, then choose a color profile, highway and background. " +
          "The clap sound, theme (Light or Dark) and menu color are there too." },
        { "How do I export a song that's ready for Clone Hero?",
          "File > Export with the Whole Song format. It makes a folder named after the song with notes.chart, the " +
          "audio, song.ini, album art and background, ready to drop into your Songs folder." },
        { "How do I add a charter icon?",
          "Song Properties > General has an Icon row. Search by charter, setlist or source name and click an icon." },
        { "Can it save my chart automatically?",
          "Yes. Pick how often (1 to 10 minutes) in Settings > Editor > Autosave chart. In Collab Mode only the host's " +
          "copy is saved." },
        { "Editing feels stuttery. How do I make it smoother?",
          "Settings > Graphics > Framerate > VSync (smoothest) keeps frames in step with your monitor. Lowering " +
          "anti-aliasing in the same tab helps on laptops." },
        { "Can I change the keys?",
          "Yes, every action, including Collab Mode, Pattern Blueprints and Preview Mode, can be rebound in Settings > Controls." },
        { "Why does Windows say it protected me when I open a new version?",
          "It does this for any downloaded program that isn't code-signed. Before extracting a new version, right-click " +
          "the zip, open Properties, tick Unblock and press OK, and it won't appear." },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => AttachToWindow();
        AttachToWindow();
    }

    // The FAQ window is usually hidden, so it's found among inactive objects too
    static void AttachToWindow()
    {
        foreach (ScrollRect scroll in Resources.FindObjectsOfTypeAll<ScrollRect>())
        {
            Transform panel = scroll.transform.parent;
            if (!scroll.gameObject.scene.IsValid() || !panel || panel.name != "BG Panel" || !panel.parent || panel.parent.name != "FAQ")
                continue;
            if (!scroll.GetComponent<FaqContent>())
                scroll.gameObject.AddComponent<FaqContent>();
        }
    }

    bool m_filled;

    void OnEnable()
    {
        Fill();
    }

    void Fill()
    {
        ScrollRect scroll = GetComponent<ScrollRect>();
        RectTransform content = scroll ? scroll.content : null;
        Text text = content ? content.GetComponentInChildren<Text>(true) : null;
        if (!text)
            return;

        if (!m_filled)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < s_questions.GetLength(0); ++i)
            {
                if (i > 0)
                    builder.Append("\n\n");
                builder.Append("Q. ").Append(s_questions[i, 0]).Append("\nA. ").Append(s_questions[i, 1]);
            }
            text.text = builder.ToString();
            scroll.vertical = true;
            scroll.scrollSensitivity = Mathf.Max(scroll.scrollSensitivity, ScrollSensitivity);
            m_filled = true;
        }

        // The text box and the scrolled content grow to fit every question, measured at the box's own width
        RectTransform rect = text.rectTransform;
        rect.pivot = new Vector2(rect.pivot.x, 1);
        float height = text.cachedTextGeneratorForLayout.GetPreferredHeight(text.text, text.GetGenerationSettings(new Vector2(rect.rect.width, 0)))
            / text.pixelsPerUnit;
        rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -Padding);
        content.sizeDelta = new Vector2(content.sizeDelta.x, height + Padding * 2);
        scroll.verticalNormalizedPosition = 1;     // Start at the top
    }
}
