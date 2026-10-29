// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System.Collections;
using System.Collections.Generic;
using UnityEngine.UI;
using UnityEngine;

public class ClapSettingsMenu : DisplayMenu
{
    [System.Serializable]
    public struct ClapToggleSetting
    {
        public Toggle toggleUI;
        public GameSettings.ClapToggle clapToggleType;
    }

    public ClapToggleSetting[] clapToggles;
    bool toggleValueChangedBlockingEnabled = false;

    protected override void OnEnable()
    {
        base.OnEnable();

        if (!m_volumeBuilt)
            BuildVolumeRow();
        if (m_volumeSlider)
        {
            m_volumeSlider.value = ClapSound.volumePercent / VolumeStep;
            UpdateVolumeText();
        }

        toggleValueChangedBlockingEnabled = true;

        foreach (ClapToggleSetting clapToggleSetting in clapToggles)
        {
            InitClapToggle(clapToggleSetting.toggleUI, clapToggleSetting.clapToggleType);
        }

        toggleValueChangedBlockingEnabled = false;
    }

    // Clap volume, under the toggles: a copy of the Settings menu's SFX slider. Past 100% makes the clap louder.
    const int VolumeStep = 5;
    const float VolumeRowHeight = 44;
    bool m_volumeBuilt = false;
    Slider m_volumeSlider;
    Text m_volumeText;

    void BuildVolumeRow()
    {
        m_volumeBuilt = true;

        RectTransform content = transform.Find("BG Panel/Scroll View/Viewport/Content") as RectTransform;
        Slider template = null;
        foreach (SettingsController settings in Resources.FindObjectsOfTypeAll<SettingsController>())
        {
            if (settings.gameObject.scene.IsValid() && settings.sfxSlider)
                template = settings.sfxSlider;
        }
        Text labelTemplate = null;
        if (content)
        {
            foreach (Text text in content.GetComponentsInChildren<Text>(true))
            {
                if (labelTemplate == null || text.text.Trim() == "Notes:")
                    labelTemplate = text;
            }
        }
        if (!content || !template || !labelTemplate)
        {
            Debug.LogWarning("Clap Settings layout has changed, so the clap volume slider wasn't added");
            return;
        }

        // Below everything already in the list, measured from the top of the content
        float bottom = 0;
        Vector3[] corners = new Vector3[4];
        foreach (RectTransform child in content)
        {
            if (!child.gameObject.activeSelf)
                continue;
            child.GetWorldCorners(corners);
            foreach (Vector3 corner in corners)
                bottom = Mathf.Max(bottom, content.rect.yMax - content.InverseTransformPoint(corner).y);
        }
        float rowCentre = bottom + 12 + VolumeRowHeight / 2;
        float left = 0;
        labelTemplate.rectTransform.GetWorldCorners(corners);
        left = content.InverseTransformPoint(corners[0]).x - content.rect.xMin;

        Text label = Instantiate(labelTemplate, content);
        label.name = "Clap Volume Label";
        label.text = "Volume:";
        label.alignment = TextAnchor.MiddleLeft;
        PlaceInRow(label.rectTransform, left, 130, rowCentre);

        m_volumeSlider = Instantiate(template, content);
        m_volumeSlider.name = "Clap Volume Slider";
        m_volumeSlider.onValueChanged = new Slider.SliderEvent();    // Drops the SFX slider's own listeners
        m_volumeSlider.wholeNumbers = true;
        m_volumeSlider.minValue = ClapSound.MinVolumePercent / VolumeStep;
        m_volumeSlider.maxValue = ClapSound.MaxVolumePercent / VolumeStep;
        m_volumeSlider.value = ClapSound.volumePercent / VolumeStep;
        PlaceInRow((RectTransform)m_volumeSlider.transform, left + 140, 300, rowCentre);
        m_volumeSlider.onValueChanged.AddListener(value =>
        {
            UpdateVolumeText();
            m_volumePending = true;
        });

        m_volumeText = Instantiate(labelTemplate, content);
        m_volumeText.name = "Clap Volume Value";
        m_volumeText.alignment = TextAnchor.MiddleLeft;
        PlaceInRow(m_volumeText.rectTransform, left + 450, 90, rowCentre);
        UpdateVolumeText();

        content.sizeDelta = new Vector2(content.sizeDelta.x, content.sizeDelta.y + Mathf.Max(0, rowCentre + VolumeRowHeight / 2 + 10 - content.rect.height));
    }

    static void PlaceInRow(RectTransform rect, float left, float width, float centreFromTop)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 0.5f);
        rect.sizeDelta = new Vector2(width, Mathf.Min(rect.sizeDelta.y > 0 ? rect.sizeDelta.y : 30, VolumeRowHeight));
        rect.anchoredPosition = new Vector2(left, -centreFromTop);
    }

    void UpdateVolumeText()
    {
        if (m_volumeText && m_volumeSlider)
            m_volumeText.text = Mathf.RoundToInt(m_volumeSlider.value * VolumeStep) + "%";
    }

    // The clap is rebuilt when the slider is let go, rather than for every step of a drag
    bool m_volumePending = false;

    protected override void Update()
    {
        base.Update();
        if (m_volumePending && !Input.GetMouseButton(0))
        {
            m_volumePending = false;
            ClapSound.volumePercent = Mathf.RoundToInt(m_volumeSlider.value * VolumeStep);
        }
    }

    protected override void OnDisable()
    {
        if (m_volumePending && m_volumeSlider)
            ClapSound.volumePercent = Mathf.RoundToInt(m_volumeSlider.value * VolumeStep);
        m_volumePending = false;
        base.OnDisable();
    }

    void InitClapToggle(Toggle toggle, GameSettings.ClapToggle setting)
    {
        if (setting.HasFlag(GameSettings.ClapToggle.HOPO) 
            || setting.HasFlag(GameSettings.ClapToggle.STRUM)
            || setting.HasFlag(GameSettings.ClapToggle.TAP)
            )
        {
            bool forceAllNoteClaps = (Globals.gameSettings.clapProperties & GameSettings.ClapToggle.ALL_NOTES) != 0;
            toggle.interactable = !forceAllNoteClaps;
        }
        
        toggle.isOn = (Globals.gameSettings.clapProperties & setting) != 0;
    }

    void SetClapSetting(GameSettings.ClapToggle setting, bool value)
    {
        if (value)
            Globals.gameSettings.clapProperties.value |= setting;
        else
            Globals.gameSettings.clapProperties.value &= ~setting;
    }

    public void ApplyCurrentToggleProperties()
    {
        if (toggleValueChangedBlockingEnabled)
            return;

        foreach (ClapToggleSetting clapToggleSetting in clapToggles)
        {
            SetClapSetting(clapToggleSetting.clapToggleType, clapToggleSetting.toggleUI.isOn);
        }

        foreach (ClapToggleSetting clapToggleSetting in clapToggles)
        {
            InitClapToggle(clapToggleSetting.toggleUI, clapToggleSetting.clapToggleType);
        }
    }
}
