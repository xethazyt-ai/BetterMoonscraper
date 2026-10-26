// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using MoonscraperChartEditor.Song;

public class DisplayProperties : UpdateableService
{
    public Button menuTabButton;
    public RectTransform settingsMenuOption;
    public RectTransform statsMenuOption;

    [Header("Settings")]
    public Text songNameText;
    public Slider hyperspeedSlider;
    public InputField snappingStep;  
    public Text gameSpeed;
    public Slider gameSpeedSlider;
    public Toggle clapToggle; 
    public Toggle metronomeToggle;
    public Slider highwayLengthSlider;
    public Transform maxHighwayLength;
    public float minHighwayLength = 11.75f;
    [SerializeField]
    BGFadeHeightController bgFade;

    [Header("Stats")]
    public Text noteCount;
    public Text spCount;
    public Text localEventsCount;
    public Text bpmCount;
    public Text tsCount;
    public Text sectionCount;
    public Text globalEventsCount;

    public TMPro.TMP_Text stepNpsDisplay;

    ChartEditor editor;

    // Cache whether we need to modify strings or not
    int prevSnappingStep = 16;
    int prevNoteCount = -1;
    int prevSpCount = -1;
    int prevLocalEventCount = -1;
    int prevBpmCount = -1;
    int prevTsCount = -1;
    int prevSecCount = -1;
    int prevGlobalEventCount = -1;

    enum MenuTabOptions
    {
        Settings,
        Stats,
    }

    struct NpsTextValues
    {
        public int stepValue;
        public float stepNps;
        public float bpmValue;
    }

    MenuTabOptions currentMenuTab = MenuTabOptions.Settings;
    const string c_menuTabTextFormat = "< {0} >";

    NpsTextValues lastUpdatedNpsTextValues = new NpsTextValues();

    // The "Hyperspeed-" and "Highway Length-" labels, found by their text, so they can show the value next to them
    Text hyperspeedLabel;
    Text highwayLengthLabel;
    Text gemSizeLabel;
    const int GemSizeStep = 5;  // percent
    Text highwayTransparencyLabel;
    const int HighwayTransparencyStep = 5;  // percent
    const int FlameSizeStep = 5;            // percent

    // The sliders go further than Moonscraper's own (hyperspeed 22, highway length 6). The label's 100% stays at
    // Moonscraper's old maximum, so the new top of the highway slider reads 200%.
    const float MaxHyperspeed = 50;
    const float FullHighwayLength = 6;
    const float MaxHighwayLengthPercent = 300;

    // The highway visuals (fretboard and rails) and the camera's far plane, as the scene set them, so a longer
    // highway can stretch them to reach the new end
    Transform m_highwayVisuals;
    Renderer m_fretboard;
    Vector3 m_highwayVisualsPosition;
    Vector3 m_highwayVisualsScale;
    Vector2 m_fretboardTiling;
    float m_defaultFarClip = -1;

    // Clone Hero's scores for the open chart, under the stats
    Text cloneHeroScoresText;
    float m_nextScoresCheck;
    const float ScoresCheckSeconds = 2;
    const float ScoresTop = 136;

    protected override void Start()
    {
        editor = ChartEditor.Instance;
        hyperspeedSlider.maxValue = MaxHyperspeed;
        highwayLengthSlider.maxValue = highwayLengthSlider.minValue + (FullHighwayLength - highwayLengthSlider.minValue) * MaxHighwayLengthPercent / 100;
        hyperspeedSlider.value = Globals.gameSettings.hyperspeed;
        highwayLengthSlider.value = Globals.gameSettings.highwayLength;

        foreach (Text text in GetComponentsInChildren<Text>(true))
        {
            if (text.text.StartsWith("Hyperspeed"))
                hyperspeedLabel = text;
            else if (text.text.StartsWith("Highway Length"))
                highwayLengthLabel = text;
        }
        UpdateHyperspeedText();
        UpdateHighwayLengthText();
        float gemSizeRow = AddGemSizeSlider();
        float transparencyRow = AddHighwayTransparencySlider(gemSizeRow);
        float flameSizeRow = AddFlameSizeSlider(gemSizeRow, gemSizeRow + transparencyRow);
        float flameSpeedRow = AddFlameSpeedSlider(gemSizeRow, gemSizeRow + transparencyRow + flameSizeRow);
        AddSpeedToggles(gemSizeRow + transparencyRow + flameSizeRow + flameSpeedRow);
        HighwayTransparency.Apply();

        UpdateGameSpeedText();

        snappingStep.onValidateInput = Step.validateStepVal;
        UpdateSnappingStepText();

        OnEnable();

        editor.events.chartReloadedEvent.Register(OnChartReload);
        editor.events.editorStateChangedEvent.Register(OnApplicationModeChanged);

        AddCloneHeroScoresText();
        OnChartReload();

        base.Start();
    }

    void OnEnable()
    {
        clapToggle.isOn = Globals.gameSettings.clapEnabled;
        metronomeToggle.isOn = Globals.gameSettings.metronomeActive;

        UpdateMenuTabDisplay();
    }

    public override void OnServiceUpdate()
    {
        var currentSong = editor.currentSong;
        var currentChart = editor.currentChart;

        // Update strings
        {
            int currentNoteCount = currentChart.note_count;
            int currentSpCount = currentChart.starPower.Count;
            int currentLocalEventCount = currentChart.events.Count;
            int currentBpmCount = currentSong.bpms.Count;
            int currentTsCount = currentSong.timeSignatures.Count;
            int currentSecCount = currentSong.sections.Count;
            int currentGlobalEventCount = currentSong.events.Count;

            if (currentNoteCount != prevNoteCount)
                noteCount.text = "Notes: " + currentNoteCount.ToString();

            if (currentSpCount != prevSpCount)
                spCount.text = "Starpower: " + currentSpCount.ToString();

            if (currentLocalEventCount != prevLocalEventCount)
                localEventsCount.text = "Local Events: " + currentLocalEventCount.ToString();

            if (currentBpmCount != prevBpmCount)
                bpmCount.text = "BPM: " + currentBpmCount.ToString();

            if (currentTsCount != prevTsCount)
                tsCount.text = "Timesignatures: " + currentTsCount.ToString();

            if (currentSecCount != prevSecCount)
                sectionCount.text = "Sections: " + currentSecCount.ToString();

            if (currentGlobalEventCount != prevGlobalEventCount)
                globalEventsCount.text = "Global Events: " + currentGlobalEventCount.ToString();

            {
                int stepValue = Globals.gameSettings.step;

                if (stepValue != prevSnappingStep)
                    UpdateSnappingStepText();

                var bpm = editor.currentSong.GetPrevBPM(editor.currentTickPos);
                float bpmValue = bpm.value / 1000.0f;
                float stepFac = SongConfig.FULL_STEP / SongConfig.STANDARD_BEAT_RESOLUTION;
                float stepNps = bpmValue / TickFunctions.SECONDS_PER_MINUTE * (stepValue / stepFac);

                bool isDirty = lastUpdatedNpsTextValues.bpmValue != bpmValue || lastUpdatedNpsTextValues.stepNps != stepNps || lastUpdatedNpsTextValues.stepValue != stepValue;
                if (isDirty)
                {
                    stepNpsDisplay.text = string.Format("NPS ({2} bpm @ 1/{0}): {1}nps", stepValue, stepNps.ToString("n2"), bpmValue);

                    lastUpdatedNpsTextValues.bpmValue = bpmValue;
                    lastUpdatedNpsTextValues.stepValue = stepValue;
                    lastUpdatedNpsTextValues.stepNps = stepNps;
                }
            }

            prevNoteCount = currentNoteCount;
            prevSpCount = currentSpCount;
            prevLocalEventCount = currentLocalEventCount;
            prevBpmCount = currentBpmCount;
            prevTsCount = currentTsCount;
            prevSecCount = currentSecCount;
            prevGlobalEventCount = currentGlobalEventCount;
        }

        if (statsMenuOption.gameObject.activeInHierarchy && Time.unscaledTime >= m_nextScoresCheck)
            UpdateCloneHeroScores();

        // Shortcuts
        if (MSChartEditorInput.GetInputDown(MSChartEditorInputActions.ToggleClap))
            clapToggle.isOn = !clapToggle.isOn;
    }

    void OnChartReload()
    {
        UpdateSongNameText();
        m_nextScoresCheck = 0;
    }

    // A wrapping text under "Step NPS" in the stats tab, copied from the Global Events line
    void AddCloneHeroScoresText()
    {
        RectTransform content = globalEventsCount.transform.parent as RectTransform;
        if (!content)
            return;

        cloneHeroScoresText = Instantiate(globalEventsCount, content);
        cloneHeroScoresText.name = "Clone Hero Scores Text";
        cloneHeroScoresText.horizontalOverflow = HorizontalWrapMode.Wrap;
        cloneHeroScoresText.verticalOverflow = VerticalWrapMode.Overflow;
        cloneHeroScoresText.supportRichText = true;
        cloneHeroScoresText.alignment = TextAnchor.UpperLeft;

        RectTransform source = globalEventsCount.rectTransform;
        RectTransform rect = cloneHeroScoresText.rectTransform;
        float left = source.anchoredPosition.x - source.sizeDelta.x * source.pivot.x;
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0, 1);
        rect.offsetMin = new Vector2(left, -ScoresTop - 20);
        rect.offsetMax = new Vector2(-4, -ScoresTop);
    }

    void UpdateCloneHeroScores()
    {
        m_nextScoresCheck = Time.unscaledTime + ScoresCheckSeconds;
        if (!cloneHeroScoresText)
            return;

        string text = CloneHeroScoresText(CloneHeroScores.Lookup(editor.currentChartFile));
        if (text == cloneHeroScoresText.text)
            return;

        cloneHeroScoresText.text = text;

        // Fit the text's height, and the scroll view's content around it
        RectTransform rect = cloneHeroScoresText.rectTransform;
        float height = Mathf.Ceil(cloneHeroScoresText.preferredHeight) + 2;
        rect.offsetMin = new Vector2(rect.offsetMin.x, rect.offsetMax.y - height);

        RectTransform content = rect.parent as RectTransform;
        content.sizeDelta = new Vector2(content.sizeDelta.x, ScoresTop + height + 4);
    }

    static string CloneHeroScoresText(CloneHeroScores.Result result)
    {
        switch (result.status)
        {
            case CloneHeroScores.Status.NoChart: return "Clone Hero: save the chart to see its scores";
            case CloneHeroScores.Status.NoScoresFile: return "Clone Hero: no scores file found";
            case CloneHeroScores.Status.Unreadable: return "Clone Hero: can't read its scores file";
            case CloneHeroScores.Status.NotPlayed: return "Clone Hero: no scores for this chart file yet";
        }

        CloneHeroScores.Song song = result.song;
        var builder = new System.Text.StringBuilder();
        builder.Append("<b>Clone Hero</b>\n");
        builder.Append(song.playCount == 1 ? "Played once" : string.Format("Played {0} times", song.playCount));

        List<CloneHeroScores.Score> scores = new List<CloneHeroScores.Score>(song.scores);
        scores.Sort((a, b) => a.instrument != b.instrument ? a.instrument.CompareTo(b.instrument) : b.difficulty.CompareTo(a.difficulty));
        foreach (CloneHeroScores.Score score in scores)
        {
            builder.AppendFormat("\n\n<b>{0} {1}</b>\n", CloneHeroScores.DifficultyName(score.difficulty), CloneHeroScores.InstrumentName(score.instrument));
            builder.Append(score.fc ? "FC" : Mathf.FloorToInt(score.percent) + "%");
            builder.AppendFormat(score.stars == 1 ? ", {0} star" : ", {0} stars", score.stars);
            builder.Append("\n").Append(score.score.ToString("N0"));
            if (score.speed != 100 && score.speed > 0)
                builder.AppendFormat(" at {0}% speed", score.speed);

            string modifiers = CloneHeroScores.ModifierNames(score.modifiers);
            if (modifiers != null)
                builder.Append("\n").Append(modifiers);
        }

        return builder.ToString();
    }

    public void UpdateSongNameText()
    {
        songNameText.text = editor.currentSong.name + " - " + editor.currentChart.name;
    }

    void OnApplicationModeChanged(in ChartEditor.State editorState)
    {
        // Moonscraper locked these while playing; they now change the preview as it plays
        hyperspeedSlider.interactable = true;
        gameSpeedSlider.interactable = true;
        highwayLengthSlider.interactable = true;
    }

    public void SetHyperspeed(float value)
    {
        float playTime = PlayTime();
        Globals.gameSettings.hyperspeed.value = value;
        UpdateHyperspeedText();
        editor.events.hyperspeedChangeEvent.Fire();
        KeepPlayTime(playTime);
    }

    // While previewing, the song's place is kept as a height up the highway, which changes meaning when the note
    // spacing does, so it's read before a change and put back after
    float PlayTime()
    {
        if (editor.currentState != ChartEditor.State.Playing || !editor.movement)
            return float.NaN;
        return ChartEditor.WorldYPositionToTime(editor.movement.transform.position.y - editor.movement.initPos.y);
    }

    void KeepPlayTime(float playTime)
    {
        if (!float.IsNaN(playTime) && editor.currentState == ChartEditor.State.Playing && editor.movement)
            editor.movement.SetTime(playTime);
    }

    void UpdateHyperspeedText()
    {
        if (!hyperspeedLabel)
            return;

        hyperspeedLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        hyperspeedLabel.text = string.Format("Hyperspeed- {0:0.#}", (float)Globals.gameSettings.hyperspeed);
    }

    void UpdateHighwayLengthText()
    {
        if (!highwayLengthLabel)
            return;

        float range = FullHighwayLength - highwayLengthSlider.minValue;
        float percent = range > 0 ? (Globals.gameSettings.highwayLength - highwayLengthSlider.minValue) / range * 100 : 100;
        highwayLengthLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        highwayLengthLabel.text = string.Format("Highway Length- {0:0}%", percent);
    }

    public void SetGameSpeed(float value)
    {
        value = Mathf.Round(value / 5.0f) * 5;
        float playTime = PlayTime();
        bool changed = !Mathf.Approximately(Globals.gameSettings.gameSpeed, value / 100.0f);
        Globals.gameSettings.gameSpeed = value / 100.0f;
        UpdateGameSpeedText();

        editor.events.hyperspeedChangeEvent.Fire();
        KeepPlayTime(playTime);
        if (changed)
            editor.RefreshPlayingAudio();   // The audio plays at the new speed straight away
    }

    void UpdateGameSpeedText()
    {
        gameSpeed.text = string.Format("Speed- x{0:0.00}", Globals.gameSettings.gameSpeed);
    }

    public void SetHighwayLength(float value)
    {
        Globals.gameSettings.highwayLength.value = value;

        Vector3 pos = Vector3.zero;
        pos.y = value * 5 + minHighwayLength;
        maxHighwayLength.transform.localPosition = pos;
        FitHighwayVisuals();

        bgFade.AdjustHeight();
        UpdateHighwayLengthText();

        editor.events.hyperspeedChangeEvent.Fire();
    }

    // "Pitched Speedups", "Non-Spaced Notes for Slowdown", "Note Flames" and "Song Video", under the Gem Size and Highway Transparency sliders
    // (slidersHeight below the highway length slider), copied from the Clap row
    void AddSpeedToggles(float slidersHeight)
    {
        RectTransform content = clapToggle.transform.parent as RectTransform;
        RectTransform lastRow = highwayLengthSlider.transform as RectTransform;
        if (!content || !lastRow)
            return;

        float y = lastRow.anchoredPosition.y - slidersHeight - 27;
        MakeSpeedToggle("Pitched Speedups:", y, SpeedOptions.pitchedSpeedups, (on) =>
        {
            SpeedOptions.pitchedSpeedups = on;
            editor.RefreshPlayingAudio();   // Straight away, even mid-preview
        });
        MakeSpeedToggle("Non-Spaced Notes for Slowdown:", y - 30, SpeedOptions.nonSpacedSlowdown, (on) =>
        {
            float playTime = PlayTime();
            SpeedOptions.nonSpacedSlowdown = on;
            editor.events.hyperspeedChangeEvent.Fire();
            KeepPlayTime(playTime);
        });
        MakeSpeedToggle("Note Flames:", y - 64, NoteFlames.isEnabled, (on) => NoteFlames.isEnabled = on);
        MakeSpeedToggle("Song Video:", y - 98, SongVideoBackground.isEnabled, (on) =>
        {
            SongVideoBackground.isEnabled = on;
            if (on && !string.IsNullOrEmpty(SongVideoBackground.status) && !SongVideoBackground.status.StartsWith("Loading")
                && !SongVideoBackground.status.StartsWith("Showing") && !SongVideoBackground.status.StartsWith("Ready"))
                editor.services.notificationBar.PushNotification(SongVideoBackground.status.TrimEnd('.').ToUpperInvariant(), 3, true);
        });

        content.sizeDelta = new Vector2(content.sizeDelta.x, content.sizeDelta.y + 153);
    }

    // "Gem Size", right under Highway Length, copied from its label and slider. Returns how far down it went.
    float AddGemSizeSlider()
    {
        RectTransform content = clapToggle.transform.parent as RectTransform;
        if (!content || !highwayLengthLabel || !gameSpeed)
            return 0;

        // One label-and-slider row further down than Highway Length's
        float rowStep = content.InverseTransformPoint(gameSpeed.transform.position).y - content.InverseTransformPoint(highwayLengthLabel.transform.position).y;
        float drop = rowStep;

        gemSizeLabel = Instantiate(highwayLengthLabel, highwayLengthLabel.transform.parent);
        gemSizeLabel.name = "Gem Size Text";
        MoveDown(gemSizeLabel.transform, content, drop);

        Slider slider = Instantiate(highwayLengthSlider, highwayLengthSlider.transform.parent);
        slider.name = "Gem Size Slider";
        slider.onValueChanged = new Slider.SliderEvent();
        slider.wholeNumbers = true;
        slider.minValue = GemSize.MinPercent / GemSizeStep;
        slider.maxValue = GemSize.MaxPercent / GemSizeStep;
        slider.value = GemSize.percent / GemSizeStep;
        slider.interactable = true;
        slider.onValueChanged.AddListener((value) =>
        {
            GemSize.percent = Mathf.RoundToInt(value) * GemSizeStep;
            UpdateGemSizeText();
        });
        MoveDown(slider.transform, content, drop);

        UpdateGemSizeText();
        content.sizeDelta = new Vector2(content.sizeDelta.x, content.sizeDelta.y + rowStep);
        return rowStep;
    }

    // "Highway Transparency", under Gem Size (gemSizeRow further down than Highway Length), made the same way.
    // Returns how far down it went.
    float AddHighwayTransparencySlider(float gemSizeRow)
    {
        RectTransform content = clapToggle.transform.parent as RectTransform;
        if (!content || !highwayLengthLabel || !gameSpeed || gemSizeRow <= 0)
            return 0;

        float rowStep = gemSizeRow;
        float drop = gemSizeRow + rowStep;

        highwayTransparencyLabel = Instantiate(highwayLengthLabel, highwayLengthLabel.transform.parent);
        highwayTransparencyLabel.name = "Highway Transparency Text";
        MoveDown(highwayTransparencyLabel.transform, content, drop);

        Slider slider = Instantiate(highwayLengthSlider, highwayLengthSlider.transform.parent);
        slider.name = "Highway Transparency Slider";
        slider.onValueChanged = new Slider.SliderEvent();
        slider.wholeNumbers = true;
        slider.minValue = HighwayTransparency.MinPercent / HighwayTransparencyStep;
        slider.maxValue = HighwayTransparency.MaxPercent / HighwayTransparencyStep;
        slider.value = HighwayTransparency.percent / HighwayTransparencyStep;
        slider.interactable = true;
        slider.onValueChanged.AddListener((value) =>
        {
            HighwayTransparency.percent = Mathf.RoundToInt(value) * HighwayTransparencyStep;
            UpdateHighwayTransparencyText();
        });
        MoveDown(slider.transform, content, drop);

        UpdateHighwayTransparencyText();
        content.sizeDelta = new Vector2(content.sizeDelta.x, content.sizeDelta.y + rowStep);
        return rowStep;
    }

    // "Flame Size", under Highway Transparency (above, how far the sliders before it reach below Highway Length)
    float AddFlameSizeSlider(float rowStep, float above)
    {
        return AddPercentSlider("Flame Size", rowStep, above, NoteFlames.MinSizePercent, NoteFlames.MaxSizePercent,
            () => NoteFlames.sizePercent, (percent) => NoteFlames.sizePercent = percent);
    }

    // "Flame Speed", under Flame Size: how fast the flames flicker and how quickly a hit's flame burns out
    float AddFlameSpeedSlider(float rowStep, float above)
    {
        return AddPercentSlider("Flame Speed", rowStep, above, NoteFlames.MinSpeedPercent, NoteFlames.MaxSpeedPercent,
            () => NoteFlames.speedPercent, (percent) => NoteFlames.speedPercent = percent);
    }

    // A "<name>- N%" label and slider in 5% steps, copied from Highway Length's and placed `above` + rowStep below it
    float AddPercentSlider(string name, float rowStep, float above, int min, int max, System.Func<int> get, System.Action<int> set)
    {
        RectTransform content = clapToggle.transform.parent as RectTransform;
        if (!content || !highwayLengthLabel || rowStep <= 0)
            return 0;

        float drop = above + rowStep;

        Text label = Instantiate(highwayLengthLabel, highwayLengthLabel.transform.parent);
        label.name = name + " Text";
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        MoveDown(label.transform, content, drop);
        System.Action updateText = () => label.text = string.Format("{0}- {1}%", name, get());

        Slider slider = Instantiate(highwayLengthSlider, highwayLengthSlider.transform.parent);
        slider.name = name + " Slider";
        slider.onValueChanged = new Slider.SliderEvent();
        slider.wholeNumbers = true;
        slider.minValue = min / FlameSizeStep;
        slider.maxValue = max / FlameSizeStep;
        slider.value = get() / FlameSizeStep;
        slider.interactable = true;
        slider.onValueChanged.AddListener((value) =>
        {
            set(Mathf.RoundToInt(value) * FlameSizeStep);
            updateText();
        });
        MoveDown(slider.transform, content, drop);

        updateText();
        content.sizeDelta = new Vector2(content.sizeDelta.x, content.sizeDelta.y + rowStep);
        return rowStep;
    }

    void UpdateHighwayTransparencyText()
    {
        if (!highwayTransparencyLabel)
            return;

        highwayTransparencyLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        highwayTransparencyLabel.text = string.Format("Hwy Transparency- {0}%", HighwayTransparency.percent);
    }

    static void MoveDown(Transform item, RectTransform content, float distance)
    {
        Vector3 local = content.InverseTransformPoint(item.position);
        local.y -= distance;
        item.position = content.TransformPoint(local);
    }

    void UpdateGemSizeText()
    {
        if (!gemSizeLabel)
            return;

        gemSizeLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
        gemSizeLabel.text = string.Format("Gem Size- {0}%", GemSize.percent);
    }

    void MakeSpeedToggle(string label, float y, bool isOn, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        Toggle toggle = Instantiate(clapToggle, clapToggle.transform.parent);
        toggle.name = label.TrimEnd(':') + " Toggle";
        toggle.onValueChanged = new Toggle.ToggleEvent();
        toggle.isOn = isOn;
        toggle.onValueChanged.AddListener(onChanged);

        RectTransform rect = (RectTransform)toggle.transform;
        rect.anchoredPosition = new Vector2(((RectTransform)clapToggle.transform).anchoredPosition.x, y);

        // The label starts where "Clap:" does and wraps onto a second line before reaching the checkbox
        Text text = toggle.GetComponentInChildren<Text>(true);
        if (text)
        {
            text.text = label;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            RectTransform textRect = text.rectTransform;
            float parentWidth = rect.rect.width;
            textRect.offsetMax = new Vector2(textRect.offsetMin.x + 72 - parentWidth, textRect.offsetMax.y);
        }
    }

    // Past Moonscraper's old maximum the fretboard and rails would stop short of the highway's end, and the camera
    // would clip it, so stretch them from the bottom up and push the far plane back
    void FitHighwayVisuals()
    {
        if (!m_highwayVisuals)
        {
            FretboardWrapMovement fretboard = FindObjectOfType<FretboardWrapMovement>();
            if (!fretboard || !fretboard.transform.parent)
                return;

            m_fretboard = fretboard.GetComponent<Renderer>();
            m_highwayVisuals = fretboard.transform.parent;
            m_highwayVisualsPosition = m_highwayVisuals.localPosition;
            m_highwayVisualsScale = m_highwayVisuals.localScale;
            if (m_fretboard && m_fretboard.sharedMaterial)
                m_fretboardTiling = m_fretboard.sharedMaterial.mainTextureScale;
        }

        // How far past the highway's end the visuals went at Moonscraper's old maximum
        float extra = Mathf.Max(0, Globals.gameSettings.highwayLength - FullHighwayLength) * 5;
        float stretch = (m_highwayVisualsScale.y * 16 + extra) / (m_highwayVisualsScale.y * 16);     // The fretboard is 16 units tall before its parent's scale

        Vector3 scale = m_highwayVisualsScale;
        scale.y *= stretch;
        m_highwayVisuals.localScale = scale;

        // Keep the bottom where it was: children are centred near 0.35 and 8 units tall each way
        Vector3 position = m_highwayVisualsPosition;
        position.y += (8 - 0.35f) * (scale.y - m_highwayVisualsScale.y);
        m_highwayVisuals.localPosition = position;

        // Repeat the texture more instead of stretching it, which also keeps its scrolling in step
        if (m_fretboard && m_fretboard.sharedMaterial)
            m_fretboard.sharedMaterial.mainTextureScale = new Vector2(m_fretboardTiling.x, m_fretboardTiling.y * stretch);

        Camera camera = Camera.main;
        if (camera)
        {
            if (m_defaultFarClip < 0)
                m_defaultFarClip = camera.farClipPlane;

            float endDepth = Vector3.Dot(maxHighwayLength.position - camera.transform.position, camera.transform.forward);
            camera.farClipPlane = Mathf.Max(m_defaultFarClip, endDepth + 10);
        }
    }

    void OnApplicationQuit()
    {
        // The fretboard material is a shared asset, so put its tiling back (it would otherwise stick in the Unity editor)
        if (m_fretboard && m_fretboard.sharedMaterial && m_highwayVisuals)
            m_fretboard.sharedMaterial.mainTextureScale = m_fretboardTiling;
        HighwayTransparency.Restore();
    }

    public void ToggleClap(bool value)
    {
        Globals.gameSettings.clapEnabled = value;

        Debug.Log("Clap toggled: " + value);
    }

    public void ToggleMetronome(bool value)
    {
        Globals.gameSettings.metronomeActive = value;

        Debug.Log("Metronome toggled: " + value);
    }

    public void IncrementSnappingStep()
    {
        Globals.gameSettings.snappingStep.Increment();
        UpdateSnappingStepText();
    }

    public void DecrementSnappingStep()
    {
        Globals.gameSettings.snappingStep.Decrement();
        UpdateSnappingStepText();
    }

    void UpdateSnappingStepText()
    {
        snappingStep.text = Globals.gameSettings.step.ToString();
        prevSnappingStep = Globals.gameSettings.step;
    }

    public void SetStep(string value)
    {
        if (value != string.Empty)
        {
            StepInputEndEdit(value);
        }
    }

    public void StepInputEndEdit(string value)
    {
        int stepVal;
        const int defaultControlsStepVal = 16;

        if (value == string.Empty)
            stepVal = defaultControlsStepVal;
        else
        {
            try
            {
                stepVal = int.Parse(value);

                if (stepVal < Step.MIN_STEP)
                    stepVal = Step.MIN_STEP;
                else if (stepVal > Step.FULL_STEP)
                    stepVal = Step.FULL_STEP;
            }
            catch
            {
                stepVal = defaultControlsStepVal;
            }
        }

        Globals.gameSettings.step = stepVal;
        UpdateSnappingStepText();
    }

    public void NextMenuTab()
    {
        currentMenuTab += 1;
        if ((int)currentMenuTab >= MoonscraperEngine.EnumX<MenuTabOptions>.Count)
        {
            currentMenuTab = 0;
        }

        UpdateMenuTabDisplay();
    }

    void UpdateMenuTabDisplay()
    {
        menuTabButton.GetComponentInChildren<Text>().text = string.Format(c_menuTabTextFormat, currentMenuTab.ToString());

        statsMenuOption.gameObject.SetActive(false);
        settingsMenuOption.gameObject.SetActive(false);

        switch (currentMenuTab)
        {
            case MenuTabOptions.Stats:
                {
                    statsMenuOption.gameObject.SetActive(true);
                    break;
                }
            case MenuTabOptions.Settings:
            default:
                {
                    settingsMenuOption.gameObject.SetActive(true);
                    break;
                }
        }
    }
}
