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
    SongPropertiesFormatBar formatBar;
    const string LoadingPhraseKey = "loading_phrase";
    bool lookingUp = false;

    TimeSpan customTime = new TimeSpan();

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
        offset.text = song.offset.ToString();
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
        DuetEditorSong.onRemoteSongInfoApplied -= RefreshFromSong;
        UpdateIni();
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
        SetUnlessTyping(offset, song.offset.ToString());
        SetUnlessTyping(difficulty, metaData.difficulty.ToString());
        SetRichUnlessTyping(genre, RichTextIni.DisplayValue(ini, "genre", metaData.genre));
        SetUnlessTyping(mediaType, metaData.mediatype);
        if (loadingPhrase)
            SetRichUnlessTyping(loadingPhrase, RichTextIni.ReadIni(ini, LoadingPhraseKey) ?? string.Empty);
        init = false;

        if (!customIniSettings.isFocused)
            UpdateIniTextFromSongProperties();
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

            try
            {
                song.offset = float.Parse(offset.text);
            }
            catch
            {
                song.offset = 0;
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

        float bottom;
        formatBar = SongPropertiesFormatBar.Build(content, rowY - 20, richFields, genre, labelTemplate, buttonTemplate, LookUpOnMusicBrainz, setSongProperties, out bottom);
        content.sizeDelta = new Vector2(content.sizeDelta.x, Mathf.Max(content.sizeDelta.y, -bottom + 10));
    }

    // Fills in the song's details online. With audio matching in this build, the song is identified by its sound and
    // only an exact match counts; otherwise it's searched for by name and artist. Only empty fields are filled.
    void LookUpOnMusicBrainz()
    {
        if (lookingUp || !formatBar)
            return;

        lookingUp = true;
        string audioPath = editor.currentSong.GetAudioLocation(Song.AudioInstrument.Song);
        bool byAudio = MusicBrainzLookup.canMatchAudio && !string.IsNullOrEmpty(audioPath);
        formatBar.SetStatus(byAudio ? "Listening to the song's audio and looking it up..." : "Looking up \"" + RichTextIni.Strip(songName.text) + "\" online...");

        string audioFailure = null;
        Action<MusicBrainzLookup.Result, string> fill = null;
        fill = (result, error) =>
        {
            if (result == null && byAudio)
            {
                // No exact audio match (chart audio is often trimmed or a different mix): search by name instead
                byAudio = false;
                audioFailure = error;
                formatBar.SetStatus(error + " Searching by name and artist instead...");
                StartCoroutine(MusicBrainzLookup.Find(songName.text, artist.text, fill));
                return;
            }

            lookingUp = false;
            if (result == null)
            {
                formatBar.SetStatus((audioFailure != null ? audioFailure + " " : string.Empty) + error);
                return;
            }

            List<string> filled = new List<string>();
            if (byAudio)
            {
                // An exact audio match knows the song, so it can fill in the name and artist too
                FillIfEmpty(songName, result.title, "name", filled);
                FillIfEmpty(artist, result.artist, "artist", filled);
            }
            FillIfEmpty(album, result.album, "album", filled);
            FillIfEmpty(year, result.year, "year", filled);
            FillIfEmpty(genre, result.genre, "genre", filled);

            string found = "Found \"" + result.title + "\" by " + result.artist + " from " + result.source + ": album " + (result.album ?? "unknown")
                + ", year " + (result.year ?? "unknown") + ", genre " + (result.genre ?? "unknown") + ".";
            if (audioFailure != null)
                found = "No exact audio match, so this was matched by name. Check it's the right song. " + found;
            formatBar.SetStatus(found + (filled.Count > 0 ? " Filled in the " + string.Join(", ", filled.ToArray()) + "." : " Clear a field to have it filled in."));
        };

        StartCoroutine(byAudio ? MusicBrainzLookup.FindByAudio(audioPath, fill) : MusicBrainzLookup.Find(songName.text, artist.text, fill));
    }

    static void FillIfEmpty(InputField field, string value, string what, List<string> filled)
    {
        if (string.IsNullOrEmpty(value) || !string.IsNullOrEmpty(field.text.Trim()))
            return;

        field.text = value;     // Goes through setSongProperties like typing it, so Collab Mode shares it
        filled.Add(what);
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
