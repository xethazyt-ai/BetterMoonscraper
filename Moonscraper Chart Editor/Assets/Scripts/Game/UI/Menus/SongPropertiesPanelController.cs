// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

//#undef UNITY_EDITOR

using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System;
using MoonscraperEngine;
using MoonscraperChartEditor.Song;

public class SongPropertiesPanelController : TabMenu
{
    public Scrollbar verticalScroll;

    [Header("General Settings")]
    public InputField songName;
    public InputField artist;
    public InputField charter;
    public InputField album;
    public InputField year;
    public InputField offset;
    public InputField difficulty;
    public InputField genre;
    public InputField mediaType;

    [Header("Audio Settings")]
    public Text musicStream;
    public Text guitarStream;
    public Text bassStream;
    public Text rhythmStream;
	public Text keysStream;
	public Text vocalStream;
    public Text drum1Stream;
	public Text drum2Stream;
	public Text drum3Stream;
	public Text drum4Stream;
    public Text crowdStream;

    [Header("Advanced Settings")]
    public MS_TMPro_InputField customIniSettings;

    bool init = false;

    // Clone Hero can show these formatted (bold, colors and so on). The formatting is kept in song.ini only.
    InputField loadingPhrase;
    SongIconPicker iconPicker;
    const string IconKey = "icon";
    SongPropertiesFormatBar formatBar;
    const string LoadingPhraseKey = "loading_phrase";
    bool lookingUp = false;

    TimeSpan customTime = new TimeSpan();

    /// <summary>True while the window is open. Ctrl+S is strikethrough here, so the file shortcuts are off.</summary>
    public static bool isOpen { get; private set; }

    static readonly string[] validAudioExtensions = { "ogg", "wav", "mp3", "opus" };
    readonly ExtensionFilter audioExFilter = new ExtensionFilter("Audio files", validAudioExtensions);

    Dictionary<Song.AudioInstrument, Text> m_audioStreamTextLookup;
    readonly string[] INI_SECTION_HEADER = { "Song", "song" };

    protected void Start()
    {
        offset.onValidateInput = LocalesManager.ValidateDecimalInput;

        songName.onValidateInput = ValidateStringMetadataInput;
        artist.onValidateInput = ValidateStringMetadataInput;
        charter.onValidateInput = ValidateStringMetadataInput;
        album.onValidateInput = ValidateStringMetadataInput;
        genre.onValidateInput = ValidateStringMetadataInput;
        mediaType.onValidateInput = ValidateStringMetadataInput;
    }

    protected override void OnEnable()
    {
        isOpen = true;
        ResetToInitialMenuItem();

        m_audioStreamTextLookup = new Dictionary<Song.AudioInstrument, Text>()
        {
            { Song.AudioInstrument.Song, musicStream },
            { Song.AudioInstrument.Guitar, guitarStream },
            { Song.AudioInstrument.Bass, bassStream },
            { Song.AudioInstrument.Rhythm, rhythmStream },
            { Song.AudioInstrument.Keys, keysStream },
            { Song.AudioInstrument.Vocals, vocalStream },
            { Song.AudioInstrument.Drum, drum1Stream },
            { Song.AudioInstrument.Drums_2, drum2Stream },
            { Song.AudioInstrument.Drums_3, drum3Stream },
            { Song.AudioInstrument.Drums_4, drum4Stream },
            { Song.AudioInstrument.Crowd, crowdStream },
        };

        bool edit = ChartEditor.isDirty;

        base.OnEnable();

        BuildFormatting();
        if (iconPicker)
            iconPicker.Refresh();
        lookingUp = false;      // A look up still going when the menu closed was stopped

        init = true;
        Song song = editor.currentSong;
        Metadata metaData = song.metaData;
        INIParser ini = song.iniProperties;

        SetRich(songName, RichTextIni.DisplayValue(ini, "name", song.name));
        SetRich(artist, RichTextIni.DisplayValue(ini, "artist", metaData.artist));
        SetRich(charter, RichTextIni.DisplayValue(ini, "charter", metaData.charter));
        SetRich(album, RichTextIni.DisplayValue(ini, "album", metaData.album));
        year.text = metaData.year;
        offset.text = OffsetText(song.offset);
        difficulty.text = metaData.difficulty.ToString();
        SetRich(genre, RichTextIni.DisplayValue(ini, "genre", metaData.genre));
        mediaType.text = metaData.mediatype;
        if (loadingPhrase)
            SetRich(loadingPhrase, RichTextIni.ReadIni(ini, LoadingPhraseKey) ?? string.Empty);

        // Init audio names
        setAudioTextLabels();
        init = false;

        customTime = TimeSpan.FromSeconds(editor.currentSongLength);

        UpdateIniTextFromSongProperties();

        ChartEditor.isDirty = edit;
        StartCoroutine(ScrollSetDelay());

        DuetEditorSong.onRemoteSongInfoApplied += RefreshFromSong;
    }

    protected override void OnDisable()
    {
        CollapseExtended();
        DuetEditorSong.onRemoteSongInfoApplied -= RefreshFromSong;
        UpdateIni();
        isOpen = false;
        base.OnDisable();
    }

    // Someone else in a Duet session changed the song's details. Show them, except in the field being typed in.
    void RefreshFromSong()
    {
        Song song = editor.currentSong;
        Metadata metaData = song.metaData;
        INIParser ini = song.iniProperties;

        init = true;
        SetRichUnlessTyping(songName, RichTextIni.DisplayValue(ini, "name", song.name));
        SetRichUnlessTyping(artist, RichTextIni.DisplayValue(ini, "artist", metaData.artist));
        SetRichUnlessTyping(charter, RichTextIni.DisplayValue(ini, "charter", metaData.charter));
        SetRichUnlessTyping(album, RichTextIni.DisplayValue(ini, "album", metaData.album));
        SetUnlessTyping(year, metaData.year);
        SetUnlessTyping(offset, OffsetText(song.offset));
        SetUnlessTyping(difficulty, metaData.difficulty.ToString());
        SetRichUnlessTyping(genre, RichTextIni.DisplayValue(ini, "genre", metaData.genre));
        SetUnlessTyping(mediaType, metaData.mediatype);
        if (loadingPhrase)
            SetRichUnlessTyping(loadingPhrase, RichTextIni.ReadIni(ini, LoadingPhraseKey) ?? string.Empty);
        init = false;
        if (iconPicker)
            iconPicker.Refresh();

        if (!customIniSettings.isFocused)
            UpdateIniTextFromSongProperties();
    }

    static string OffsetText(float value)
    {
        return value.ToString(System.Globalization.CultureInfo.CurrentCulture);
    }

    static void SetUnlessTyping(InputField field, string text)
    {
        if (!field.isFocused && field.text != text)
            field.text = text;
    }

    // The song details Clone Hero can show formatted: the field shows the text, the formatting is kept alongside
    void SetRich(InputField field, string tagged)
    {
        if (formatBar)
            formatBar.SetTagged(field, tagged);
        else
            field.text = RichTextIni.Strip(tagged);
    }

    void SetRichUnlessTyping(InputField field, string tagged)
    {
        if (!field.isFocused && Tagged(field) != tagged)
            SetRich(field, tagged);
    }

    string Tagged(InputField field)
    {
        return formatBar ? formatBar.GetTagged(field) : field.text;
    }

    bool BlockedForDuetGuest()
    {
        if (!DuetPanel.isGuest)
            return false;

        editor.services.notificationBar.PushNotification("ONLY THE HOST CAN CHANGE THE AUDIO IN A COLLAB SESSION", 3, true);
        return true;
    }

    IEnumerator ScrollSetDelay()
    {
        yield return null;
        verticalScroll.value = 1;
    }

    void Apply()
    {
        editor.currentSong.name = songName.text;
        editor.currentSong.metaData.artist = artist.text;
    }

    public void setSongProperties()
    {
        if (!init)
        {
            Song song = editor.currentSong;
            Metadata metaData = song.metaData;

            // The chart gets the plain text. Formatting goes to song.ini, which is where Clone Hero reads it from.
            string plainName = RichTextIni.Strip(songName.text);
            if (!song.name.Equals(plainName))
            {
                song.name = plainName;
                editor.uiServices.editorPanels.displayProperties.UpdateSongNameText();
                editor.RepaintWindowText();
            }

            metaData.artist = RichTextIni.Strip(artist.text);
            metaData.charter = RichTextIni.Strip(charter.text);
            metaData.album = RichTextIni.Strip(album.text);
            metaData.year = year.text;

            // Kept as it was if the box holds something that isn't a number (empty, or just "-"), rather than becoming 0
            float typedOffset;
            if (float.TryParse(offset.text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out typedOffset)
                && !float.IsNaN(typedOffset) && !float.IsInfinity(typedOffset))
            {
                // Only a real change: the box shows a rounded copy, which mustn't nudge the exact value
                if (offset.text != OffsetText(song.offset))
                    song.offset = typedOffset;
            }

            try
            {
                metaData.difficulty = int.Parse(difficulty.text);
            }
            catch
            {
                metaData.difficulty = 0;
            }

            metaData.genre = RichTextIni.Strip(genre.text);
            metaData.mediatype = mediaType.text;

            INIParser ini = song.iniProperties;
            bool iniChanged = RichTextIni.StoreFormatted(ini, "name", Tagged(songName));
            iniChanged |= RichTextIni.StoreFormatted(ini, "artist", Tagged(artist));
            iniChanged |= RichTextIni.StoreFormatted(ini, "charter", Tagged(charter));
            iniChanged |= RichTextIni.StoreFormatted(ini, "album", Tagged(album));
            iniChanged |= RichTextIni.StoreFormatted(ini, "genre", Tagged(genre));
            if (loadingPhrase && (RichTextIni.ReadIni(ini, LoadingPhraseKey) ?? string.Empty) != Tagged(loadingPhrase))
            {
                RichTextIni.WriteIni(ini, LoadingPhraseKey, Tagged(loadingPhrase));
                iniChanged = true;
            }
            if (iniChanged)
                UpdateIniTextFromSongProperties();      // The Advanced tab's text is read back into the song when the menu closes

            if (editor.currentSong.manualLength.HasValue)   // if we were already using the manual length
            {
                editor.currentSong.manualLength = (float)customTime.TotalSeconds;
            }

            ChartEditor.isDirty = true;
        }
    }

    // Adds a Loading phrase field and the formatting buttons under the General settings, the first time the menu opens
    void BuildFormatting()
    {
        if (formatBar)
            return;

        RectTransform content = (RectTransform)genre.transform.parent;
        Text labelTemplate = null;
        foreach (Text text in content.GetComponentsInChildren<Text>(true))
        {
            if (text.transform.parent == content && text.text.StartsWith("Genre"))
                labelTemplate = text;
        }
        Button buttonTemplate = null;
        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (button.name == "From General Settings Button")
                buttonTemplate = button;
        }
        if (!labelTemplate || !buttonTemplate)
        {
            Debug.LogWarning("Couldn't find the controls to build the song.ini formatting bar from");
            return;
        }

        RectTransform lastLabel = (RectTransform)mediaType.transform;
        float rowY = lastLabel.anchoredPosition.y - 35;

        Text phraseLabel = Instantiate(labelTemplate, content);
        phraseLabel.name = "Loading Phrase";
        phraseLabel.text = "Loading phrase:";
        RectTransform phraseLabelRect = phraseLabel.rectTransform;
        phraseLabelRect.anchoredPosition = new Vector2(labelTemplate.rectTransform.anchoredPosition.x, rowY);

        loadingPhrase = Instantiate(genre, content);
        loadingPhrase.name = "Loading Phrase InputField";
        loadingPhrase.onValueChanged = new InputField.OnChangeEvent();
        loadingPhrase.onEndEdit = new InputField.SubmitEvent();
        loadingPhrase.onValueChanged.AddListener(value => setSongProperties());
        loadingPhrase.onValidateInput = ValidateStringMetadataInput;
        ((RectTransform)loadingPhrase.transform).anchoredPosition = new Vector2(((RectTransform)genre.transform).anchoredPosition.x, rowY);

        InputField[] richFields = { songName, artist, charter, album, genre, loadingPhrase };
        foreach (InputField field in richFields)
            field.characterLimit = 0;   // Tags make the text longer than it looks

        AddExtendButtons(content, buttonTemplate, new[] { songName, artist, charter, album, genre, mediaType, loadingPhrase });

        float bottom;
        formatBar = SongPropertiesFormatBar.Build(content, rowY - 30, richFields, genre, labelTemplate, buttonTemplate, LookUpSongInfo, setSongProperties, out bottom);
        iconPicker = SongIconPicker.Build(content, bottom - 10, SongPropertiesFormatBar.Right, genre, labelTemplate, buttonTemplate, GetIcon, SetIcon, out bottom);
        content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(content.sizeDelta.y, -bottom + 10));
    }

    // A small + button beside each text box. It makes that box wider and several lines tall, over the rows under it,
    // so long text can be seen whole; - (or opening another one, or closing the menu) puts it back.
    class Extendable
    {
        public InputField field;
        public Button button;
        public Text buttonText;
        public Vector2 position, size, pivot;
        public int siblingIndex;
        public InputField.LineType lineType;
        public HorizontalWrapMode wrap, placeholderWrap;
    }
    readonly List<Extendable> extendables = new List<Extendable>();
    Extendable extended;
    float extendButtonX;
    const float ExtendButtonSize = 24;
    const float ExtendedHeight = 96;    // About five lines

    void AddExtendButtons(RectTransform content, Button buttonTemplate, InputField[] fields)
    {
        extendButtonX = Mathf.Max(SongPropertiesFormatBar.Right + 6, content.rect.width - ExtendButtonSize - 10);
        foreach (InputField field in fields)
        {
            if (!field)
                continue;

            Extendable entry = new Extendable { field = field };
            RectTransform rect = (RectTransform)field.transform;
            entry.button = Instantiate(buttonTemplate, content);
            entry.button.name = field.name + " Extend Button";
            entry.button.onClick = new Button.ButtonClickedEvent();
            entry.button.onClick.AddListener(() => ToggleExtended(entry));
            foreach (Text text in entry.button.GetComponentsInChildren<Text>(true))
            {
                text.text = "+";
                entry.buttonText = text;
            }
            RectTransform buttonRect = (RectTransform)entry.button.transform;
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0, 1);
            buttonRect.pivot = new Vector2(0, 0.5f);
            buttonRect.sizeDelta = new Vector2(ExtendButtonSize, ExtendButtonSize);
            buttonRect.anchoredPosition = new Vector2(extendButtonX, rect.anchoredPosition.y + rect.sizeDelta.y * (0.5f - rect.pivot.y));
            extendables.Add(entry);
        }
    }

    void ToggleExtended(Extendable entry)
    {
        bool wasExtended = extended == entry;
        CollapseExtended();
        if (wasExtended)
            return;

        extended = entry;
        InputField field = entry.field;
        RectTransform rect = (RectTransform)field.transform;
        entry.position = rect.anchoredPosition;
        entry.size = rect.sizeDelta;
        entry.pivot = rect.pivot;
        entry.siblingIndex = rect.GetSiblingIndex();
        entry.lineType = field.lineType;
        entry.wrap = field.textComponent ? field.textComponent.horizontalOverflow : HorizontalWrapMode.Overflow;
        Text placeholder = field.placeholder as Text;
        entry.placeholderWrap = placeholder ? placeholder.horizontalOverflow : HorizontalWrapMode.Overflow;

        // Same top left corner, out to just before the + buttons and down over the rows below
        float left = entry.position.x - entry.size.x * entry.pivot.x;
        float top = entry.position.y + entry.size.y * (1 - entry.pivot.y);
        rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(Mathf.Max(entry.size.x, extendButtonX - 4 - left), Mathf.Max(entry.size.y, ExtendedHeight));
        rect.anchoredPosition = new Vector2(left, top);
        field.lineType = InputField.LineType.MultiLineSubmit;     // Wraps; Enter still finishes, as song.ini is one line
        if (field.textComponent)
            field.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
        if (placeholder)
            placeholder.horizontalOverflow = HorizontalWrapMode.Wrap;
        rect.SetAsLastSibling();     // Drawn over the rows it covers
        entry.button.transform.SetAsLastSibling();
        if (entry.buttonText)
            entry.buttonText.text = "-";
        field.ForceLabelUpdate();
    }

    void CollapseExtended()
    {
        Extendable entry = extended;
        extended = null;
        if (entry == null || !entry.field)
            return;

        InputField field = entry.field;
        RectTransform rect = (RectTransform)field.transform;
        rect.pivot = entry.pivot;
        rect.sizeDelta = entry.size;
        rect.anchoredPosition = entry.position;
        rect.SetSiblingIndex(entry.siblingIndex);
        field.lineType = entry.lineType;
        if (field.textComponent)
            field.textComponent.horizontalOverflow = entry.wrap;
        Text placeholder = field.placeholder as Text;
        if (placeholder)
            placeholder.horizontalOverflow = entry.placeholderWrap;
        if (entry.buttonText)
            entry.buttonText.text = "+";
        field.ForceLabelUpdate();
    }

    // Ctrl+Z / Ctrl+Y (or Ctrl+Shift+Z) undo and redo changes made here. A burst of typing is one step: a change
    // becomes a step once nothing has changed for a moment. Kept while the same song is open.
    const float UndoPollSeconds = 0.2f;
    const float UndoSettleSeconds = 0.8f;
    const int MaxUndoSteps = 100;
    readonly List<Dictionary<string, string>> undoSteps = new List<Dictionary<string, string>>();
    readonly List<Dictionary<string, string>> redoSteps = new List<Dictionary<string, string>>();
    Dictionary<string, string> undoCommitted, undoLastSeen;
    Song undoSong;
    float undoPolledAt, undoChangedAt;

    protected override void Update()
    {
        base.Update();

        Song song = editor.currentSong;
        if (song == null)
            return;
        if (song != undoSong)
        {
            undoSong = song;
            undoSteps.Clear();
            redoSteps.Clear();
            undoCommitted = undoLastSeen = DuetSongInfo.Read(song);
            undoChangedAt = Time.unscaledTime;
        }

        bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
        bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
        if (ctrl && UnityEngine.Input.GetKeyDown(KeyCode.Z) && !shift)
            UndoSongProperties();
        else if (ctrl && (UnityEngine.Input.GetKeyDown(KeyCode.Y) || (shift && UnityEngine.Input.GetKeyDown(KeyCode.Z))))
            RedoSongProperties();
        else if (Time.unscaledTime - undoPolledAt >= UndoPollSeconds)
            PollSongProperties(false);
    }

    void PollSongProperties(bool commitNow)
    {
        undoPolledAt = Time.unscaledTime;
        Dictionary<string, string> current = DuetSongInfo.Read(undoSong);
        if (!SameInfo(current, undoLastSeen))
        {
            undoLastSeen = current;
            undoChangedAt = Time.unscaledTime;
        }

        if (!SameInfo(undoLastSeen, undoCommitted) && (commitNow || Time.unscaledTime - undoChangedAt >= UndoSettleSeconds))
        {
            undoSteps.Add(undoCommitted);
            if (undoSteps.Count > MaxUndoSteps)
                undoSteps.RemoveAt(0);
            undoCommitted = undoLastSeen;
            redoSteps.Clear();
        }
    }

    void UndoSongProperties()
    {
        PollSongProperties(true);
        if (undoSteps.Count == 0)
            return;

        redoSteps.Add(undoCommitted);
        Dictionary<string, string> previous = undoSteps[undoSteps.Count - 1];
        undoSteps.RemoveAt(undoSteps.Count - 1);
        RestoreSongProperties(previous);
    }

    void RedoSongProperties()
    {
        PollSongProperties(true);
        if (redoSteps.Count == 0)
            return;

        undoSteps.Add(undoCommitted);
        Dictionary<string, string> next = redoSteps[redoSteps.Count - 1];
        redoSteps.RemoveAt(redoSteps.Count - 1);
        RestoreSongProperties(next);
    }

    void RestoreSongProperties(Dictionary<string, string> info)
    {
        Song song = undoSong;
        DuetSongInfo.Apply(song, new Dictionary<string, string>(info));
        ChartEditor.isDirty = true;

        // Every box shows it, even the one being typed in
        init = true;
        Metadata metaData = song.metaData;
        INIParser ini = song.iniProperties;
        SetRich(songName, RichTextIni.DisplayValue(ini, "name", song.name));
        SetRich(artist, RichTextIni.DisplayValue(ini, "artist", metaData.artist));
        SetRich(charter, RichTextIni.DisplayValue(ini, "charter", metaData.charter));
        SetRich(album, RichTextIni.DisplayValue(ini, "album", metaData.album));
        year.text = metaData.year;
        offset.text = OffsetText(song.offset);
        difficulty.text = metaData.difficulty.ToString();
        SetRich(genre, RichTextIni.DisplayValue(ini, "genre", metaData.genre));
        mediaType.text = metaData.mediatype;
        if (loadingPhrase)
            SetRich(loadingPhrase, RichTextIni.ReadIni(ini, LoadingPhraseKey) ?? string.Empty);
        init = false;
        UpdateIniTextFromSongProperties();
        if (iconPicker)
            iconPicker.Refresh();

        undoCommitted = undoLastSeen = DuetSongInfo.Read(song);
        undoChangedAt = undoPolledAt = Time.unscaledTime;
    }

    static bool SameInfo(Dictionary<string, string> a, Dictionary<string, string> b)
    {
        if (a == null || b == null || a.Count != b.Count)
            return a == b;
        foreach (KeyValuePair<string, string> pair in a)
        {
            string other;
            if (!b.TryGetValue(pair.Key, out other) || other != pair.Value)
                return false;
        }
        return true;
    }

    // The source or charter icon Clone Hero shows for the song: song.ini's "icon"
    string GetIcon()
    {
        return RichTextIni.ReadIni(editor.currentSong.iniProperties, IconKey) ?? string.Empty;
    }

    void SetIcon(string id)
    {
        INIParser ini = editor.currentSong.iniProperties;
        if (string.IsNullOrEmpty(id))
            RichTextIni.DeleteIni(ini, IconKey);
        else
            RichTextIni.WriteIni(ini, IconKey, id);
        UpdateIniTextFromSongProperties();
        ChartEditor.isDirty = true;
    }

    // Looks the typed name and artist up on iTunes and fills in the name and artist as iTunes spells them, the album,
    // year and genre. Also credits whoever is charting, in their Collab colors.
    void LookUpSongInfo()
    {
        if (lookingUp || !formatBar)
            return;

        string credit = DuetPanel.CharterCredit();
        if (!string.IsNullOrEmpty(credit))
        {
            SetRich(charter, credit);
            setSongProperties();    // The words may be the same with new colors, which typing alone wouldn't send
        }

        lookingUp = true;
        formatBar.SetStatus("Looking up \"" + RichTextIni.Strip(songName.text) + "\" on iTunes...");
        StartCoroutine(ITunesLookup.Find(songName.text, artist.text, (result, error) =>
        {
            lookingUp = false;
            if (result == null)
            {
                formatBar.SetStatus(error + (credit != null ? " The charter is filled in." : string.Empty));
                return;
            }

            FillRich(songName, result.title);
            FillRich(artist, result.artist);
            FillRich(album, result.album);
            FillRich(genre, result.genre);
            if (!string.IsNullOrEmpty(result.year))
                year.text = result.year;
            setSongProperties();

            formatBar.SetStatus("Filled in from iTunes: \"" + result.title + "\" by " + result.artist + ", album " + (result.album ?? "unknown")
                + ", year " + (result.year ?? "unknown") + ", genre " + (result.genre ?? "unknown") + "."
                + (credit != null ? " Charter: " + RichTextIni.Strip(credit) + "." : string.Empty));
        }));
    }

    // Replaces what's there, formatting and all, with iTunes's spelling. Left alone if iTunes doesn't know it.
    void FillRich(InputField field, string value)
    {
        if (!string.IsNullOrEmpty(value))
            SetRich(field, value);
    }

    public void UpdateIni()
    {
        Song song = editor.currentSong;
        song.iniProperties = SongIniFunctions.SongIniFromString(customIniSettings.text);
    }

    void UpdateIniTextFromSongProperties()
    {
        customIniSettings.text = SongIniFunctions.IniTextFromSongProperties(editor.currentSong.iniProperties);
    }

    public void RefreshIniDisplay()
    {
        UpdateIni();
        UpdateIniTextFromSongProperties();
        ChartEditor.isDirty = true;
    }

    public void PopulateIniFromGeneralSettings()
    {
        RefreshIniDisplay();
        SongIniFunctions.PopulateIniWithSongMetadata(editor.currentSong, editor.currentSong.iniProperties, editor.currentSongLength);
        UpdateIniTextFromSongProperties();
        ChartEditor.isDirty = true;
    }

    public void AddCloneHeroIniTags()
    {
        RefreshIniDisplay();

        var song = editor.currentSong;
        var iniParser = song.iniProperties;

        SongIniFunctions.AddCloneHeroIniTags(song, iniParser, editor.currentSongLength);
        UpdateIniTextFromSongProperties();
        ChartEditor.isDirty = true;
    }

    public void OnIniInputValueChanged()
    {
        // Keep the song's ini up to date while typing, so Collab Mode can send it to everyone straight away
        UpdateIni();
        ChartEditor.isDirty = true;
    }

    public void OnIniInputEndEdit()
    {
        RefreshIniDisplay();
    }

    void ClipText(Text text)
    {
        float maxWidth = text.rectTransform.rect.width;
        if (text.preferredWidth > maxWidth)
        {
            int removePos = text.text.Length - 1;
            text.text += "...";

            while (removePos > 0 && text.preferredWidth > maxWidth)
            {
                text.text = text.text.Remove(removePos--, 1);
            }
        }
    }

   void setAudioTextLabels()
    {
        Song song = editor.currentSong;

        foreach (var audio in EnumX<Song.AudioInstrument>.Values)
        {
            Text audioStreamText;

            if (!m_audioStreamTextLookup.TryGetValue(audio, out audioStreamText))
            {
                Debug.Assert(false, "Audio stream UI Text not linked to an Audio Instrument for instrument " + audio.ToString());
                continue;
            }

            if (editor.currentSongAudio.GetAudioIsLoaded(audio))
            {
                audioStreamText.color = Color.white;
                audioStreamText.text = song.GetAudioName(audio);
                ClipText(audioStreamText);
            }
            else
            {
                audioStreamText.color = Color.red;
                audioStreamText.text = "No audio";
            }
        }

        ChartEditor.isDirty = true;
    }

    string GetAudioFile()
    {
        string audioFilepath = string.Empty;
        string defExt = string.Empty;
        foreach(string extention in validAudioExtensions)
        {
            if (defExt != string.Empty)
                defExt += ",";

            defExt += extention;
        }

        FileExplorer.OpenFilePanel(audioExFilter, defExt, out audioFilepath);
        return audioFilepath;
    }

    void LoadAudioStream(Song.AudioInstrument audioInstrument)
    {
        if (BlockedForDuetGuest())
            return;

        try
        {
            string filepath = GetAudioFile();
            if (editor.currentSongAudio.LoadAudio(filepath, audioInstrument))
            {
                // Record the filepath
                editor.currentSong.SetAudioLocation(audioInstrument, filepath);
                StartCoroutine(SetAudio());
            }
        }
        catch (Exception e)
        {
            Logger.LogException(e, "Could not open audio");
        }
    }

    void ClearAudioStream(Song.AudioInstrument audio)
    {
        if (BlockedForDuetGuest())
            return;

        editor.currentSongAudio.Clear(audio);
        editor.currentSong.SetAudioLocation(audio, string.Empty);

        setAudioTextLabels();
    }

    public void RefreshAllAudioStreams()
    {
        if (BlockedForDuetGuest())
            return;

        StartCoroutine(_RefreshAllAudioStreams());
    }

    IEnumerator _RefreshAllAudioStreams()
    {
        editor.ReloadAudio();

        LoadingTasksManager tasksManager = editor.services.loadingTasksManager;

        while (tasksManager.isRunningTask)
            yield return null;

        setAudioTextLabels();
    }

    IEnumerator SetAudio()
    {
        LoadingTasksManager tasksManager = editor.services.loadingTasksManager;

        List<LoadingTask> tasks = new List<LoadingTask>()
        {
            new LoadingTask("Loading audio", () =>
            {
                while (editor.currentSongAudio.isAudioLoading) ;
            })
        };

        tasksManager.KickTasks(tasks);

        while (tasksManager.isRunningTask)
            yield return null;

        setAudioTextLabels();
    }

    static readonly HashSet<char> s_metadataBannedChars = new HashSet<char>()
    {
        '"',
        '\n'
    };

    public static char ValidateStringMetadataInput(string text, int charIndex, char addedChar)
    {
        if (s_metadataBannedChars.Contains(addedChar) || char.IsControl(addedChar))
        {
            return '\0';
        }

        return addedChar;
    }

    // Unity doesn't support calling methods with enum parameters
    // From button click handlers, so we define load/clear audio
    // implementations for each case here.
    #region Unity button click handlers
    public void LoadMusicStream()
    {
        LoadAudioStream(Song.AudioInstrument.Song);
    }

    public void ClearMusicStream()
    {
        ClearAudioStream(Song.AudioInstrument.Song);
    }

    public void LoadGuitarStream()
    {
        LoadAudioStream(Song.AudioInstrument.Guitar);
    }

    public void ClearGuitarStream()
    {
        ClearAudioStream(Song.AudioInstrument.Guitar);
    }

    public void LoadBassStream()
    {
        LoadAudioStream(Song.AudioInstrument.Bass);
    }

    public void ClearBassStream()
    {
        ClearAudioStream(Song.AudioInstrument.Bass);
    }

    public void LoadRhythmStream()
    {
        LoadAudioStream(Song.AudioInstrument.Rhythm);
    }

    public void ClearRhythmStream()
    {
        ClearAudioStream(Song.AudioInstrument.Rhythm);
    }

    public void LoadVocalStream()
    {
        LoadAudioStream(Song.AudioInstrument.Vocals);
    }

    public void ClearVocalStream()
    {
        ClearAudioStream(Song.AudioInstrument.Vocals);
    }

    public void LoadKeysStream()
    {
        LoadAudioStream(Song.AudioInstrument.Keys);
    }

    public void ClearKeysStream()
    {
        ClearAudioStream(Song.AudioInstrument.Keys);
    }

    public void LoadDrum1Stream()
    {
        LoadAudioStream(Song.AudioInstrument.Drum);
    }

    public void ClearDrum1Stream()
    {
        ClearAudioStream(Song.AudioInstrument.Drum);
    }

    public void LoadDrum2Stream()
    {
        LoadAudioStream(Song.AudioInstrument.Drums_2);
    }

    public void ClearDrum2Stream()
    {
        ClearAudioStream(Song.AudioInstrument.Drums_2);
    }

    public void LoadDrum3Stream()
    {
        LoadAudioStream(Song.AudioInstrument.Drums_3);
    }

    public void ClearDrum3Stream()
    {
        ClearAudioStream(Song.AudioInstrument.Drums_3);
    }

    public void LoadDrum4Stream()
    {
        LoadAudioStream(Song.AudioInstrument.Drums_4);
    }

    public void ClearDrum4Stream()
    {
        ClearAudioStream(Song.AudioInstrument.Drums_4);
    }

    public void LoadCrowdStream()
    {
        LoadAudioStream(Song.AudioInstrument.Crowd);
    }

    public void ClearCrowdStream()
    {
        ClearAudioStream(Song.AudioInstrument.Crowd);
    }
    #endregion
}
