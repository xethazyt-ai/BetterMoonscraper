// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using UnityEngine.UI;
using MoonscraperEngine;
using MoonscraperEngine.Audio;
using MoonscraperChartEditor.Song;
using MoonscraperChartEditor.Song.IO;

public class Export : DisplayMenu {
    public Text exportingInfo;
    public Dropdown fileTypeDropdown;
    public Toggle chPackageToggle;
    public Toggle forcedToggle;
    public InputField targetResolution;
    public InputField delayInputField;
    public Toggle copyDifficultiesToggle;
    public Toggle generateIniToggle;
    public Button magmaButton;
    public RectTransform midiSettings;
    public Dropdown midiInstrumentDifficultyDropdown;
    public Dropdown midiRbFormatDropdown;

    ExportOptions exportOptions;
    float delayTime = 0;

    // Exporting at the speed set on the left panel's Speed slider: rows added under the other options
    Text m_speedInfo;
    Toggle m_speedSuffixToggle;
    static readonly string[] s_iniTimeKeysMs = { "song_length", "preview_start_time", "preview_end_time", "delay", "video_start_time" };

    const string FILE_EXT_CHART = ".chart";
    const string FILE_EXT_MIDI = ".mid";
    readonly string[] COPY_IF_EXTENTION = { ".opus" };

    string chartInfoText = "Exports into the .chart format, as notes.chart inside a folder named after the song.";
    string chPackageText = "Will export and organise all chart and audio files into the selected folder to be compatible with Clone Hero's naming structure.\n" +
        "This will also automatically re-encode audio into the .ogg format as needed.";
    string midInfoText = "Exports into the .mid format, as notes.mid inside a folder named after the song. \n\n" +
        "Notes: \n" +
        "\t-Audio will disconnect from file.\n" +
        "\t-Starpower, tap notes, open notes, solo markers and cymbal toggle events will be defined by Midi Settings: Instrument Events.\n" +
        "\t-Rock Band Format will define the section marker prefix. RB2 uses \"section \" while RB3 will use \"prc_\".\n" +
        "\n" +

        "Exporting to Magma (Rock Band) notes: \n" +
        "\t-Resolution must be 480 \n" +
        "\t-Notes cannot be within the first 2.45 seconds of a song \n" +
        "\t-Charts must be UNFORCED and contain no open notes \n" +
        "\t-Magma has reserved names for sections that must be followed for successful compilation. " +
        "For example, for Magma to read a section called \"Intro a\", the section should be labeled as \"intro_a\" in Moonscraper. " +
        "A full list of sections can be found at http://pksage.com/rbndocs/index.php?title=All_Practice_Sections \n";

    void Start()
    {
        setAsChartFile();
        BuildSpeedRows();
        RefreshSpeedRows();
    }

    protected override void Update()
    {
        // Only the host of a Duet session keeps the chart, so joiners can't export it either
        if (editor.BlockedSaveForDuetGuest())
        {
            Disable();
            return;
        }

        base.Update();
    }

    protected override void OnEnable()
    {
        base.OnEnable();


        fileTypeDropdown.value = 0;

        exportOptions.forced = true;
        forcedToggle.isOn = exportOptions.forced;

        exportOptions.copyDownEmptyDifficulty = false;
        copyDifficultiesToggle.isOn = exportOptions.copyDownEmptyDifficulty;

        exportOptions.targetResolution = editor.currentSong.resolution;
        targetResolution.text = exportOptions.targetResolution.ToString();

        SetTrackEventDifficulty(midiInstrumentDifficultyDropdown.value);
        SetRbFormat(midiRbFormatDropdown.value);

        delayTime = 0;
        delayInputField.text = delayTime.ToString();

        chPackageToggle.isOn = false;
        generateIniToggle.isOn = !editor.currentSong.iniProperties.IsEmpty;

        RefreshSpeedRows();
    }

    static float exportSpeed
    {
        get
        {
            float speed = Globals.gameSettings.gameSpeed;
            return Mathf.Abs(speed - 1) < 0.001f || speed <= 0 ? 1 : speed;
        }
    }

    static string SpeedSuffix(float speed)
    {
        return " (" + SpeedOptions.SpeedText(speed) + " Speed)";
    }

    // Two rows copied from the Clone Hero Package toggle: what speed the export is at, and the name suffix checkbox
    void BuildSpeedRows()
    {
        if (!chPackageToggle)
            return;

        RectTransform template = (RectTransform)chPackageToggle.transform;
        float x = template.anchoredPosition.x;

        Toggle infoRow = Instantiate(chPackageToggle, template.parent);
        infoRow.name = "Export Speed Info";
        infoRow.onValueChanged = new Toggle.ToggleEvent();
        infoRow.interactable = false;
        if (infoRow.targetGraphic)
            infoRow.targetGraphic.gameObject.SetActive(false);     // Just the text
        if (infoRow.graphic)
            infoRow.graphic.gameObject.SetActive(false);
        ((RectTransform)infoRow.transform).anchoredPosition = new Vector2(x, -297);
        m_speedInfo = infoRow.GetComponentInChildren<Text>(true);

        m_speedSuffixToggle = Instantiate(chPackageToggle, template.parent);
        m_speedSuffixToggle.name = "Export Speed Suffix Toggle";
        m_speedSuffixToggle.onValueChanged = new Toggle.ToggleEvent();
        m_speedSuffixToggle.isOn = SpeedOptions.exportSpeedSuffix;
        m_speedSuffixToggle.onValueChanged.AddListener((on) => { SpeedOptions.exportSpeedSuffix = on; RefreshSpeedRows(); });
        ((RectTransform)m_speedSuffixToggle.transform).anchoredPosition = new Vector2(x, -322);
    }

    void RefreshSpeedRows()
    {
        if (!m_speedInfo || !m_speedSuffixToggle)
            return;

        float speed = exportSpeed;
        if (speed == 1)
        {
            m_speedInfo.text = "Export speed: 1x (set with the Speed slider)";
            m_speedSuffixToggle.gameObject.SetActive(false);
            return;
        }

        m_speedInfo.text = "Export speed: " + SpeedOptions.SpeedText(speed) + ", " + (SpeedOptions.pitchedSpeedups ? "pitched" : "same pitch");
        m_speedSuffixToggle.gameObject.SetActive(true);
        Text label = m_speedSuffixToggle.GetComponentInChildren<Text>(true);
        if (label)
            label.text = "Add" + SpeedSuffix(speed) + ":";
    }

    /// <summary>
    /// A copy of the song played at the export speed: BPMs, offset, preview and song.ini times all scaled, and the
    /// name suffixed if that's ticked. The song itself if exporting at 1x.
    /// </summary>
    static Song SongAtExportSpeed(Song song, float speed)
    {
        if (speed == 1)
            return song;

        Song scaled = new Song(song);
        scaled.ScaleSpeed(speed);

        INIParser ini = scaled.iniProperties;
        foreach (string key in s_iniTimeKeysMs)
        {
            string value = RichTextIni.ReadIni(ini, key);
            long ms;
            if (value != null && long.TryParse(value, out ms))
                RichTextIni.WriteIni(ini, key, Math.Round(ms / speed).ToString());
        }

        if (SpeedOptions.exportSpeedSuffix)
        {
            string suffix = SpeedSuffix(speed);
            scaled.name += suffix;
            string iniName = RichTextIni.ReadIni(ini, "name");
            if (!string.IsNullOrEmpty(iniName))
                RichTextIni.WriteIni(ini, "name", iniName + suffix);
        }

        return scaled;
    }

    // Writes the song's audio at the export speed next to the exported chart, with Clone Hero's names, and points the
    // exported song at it. Returns an error to show, or null.
    static string ExportAudioAtSpeed(Song source, Song exported, string folder, float speed)
    {
        string errors = null;
        foreach (Song.AudioInstrument audio in EnumX<Song.AudioInstrument>.Values)
        {
            string audioLocation = source.GetAudioLocation(audio);
            if (string.IsNullOrEmpty(audioLocation) || !File.Exists(audioLocation))
                continue;

            string name = GetCHOggFilename(audio);
            if (string.IsNullOrEmpty(name))
                continue;

            string output = Path.Combine(folder, name);
            if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(audioLocation), StringComparison.OrdinalIgnoreCase))
            {
                errors += "Didn't write " + name + " at " + SpeedOptions.SpeedText(speed) + " because it would replace the original audio. Export into a different folder.\n";
                continue;
            }

            Debug.LogFormat("Writing {0} at {1} speed to {2}", audioLocation, speed, output);
            if (AudioManager.ConvertToOggAtSpeed(audioLocation, output, speed, SpeedOptions.pitchedSpeedups))
                exported.SetAudioLocation(audio, output);
            else
                errors += "Couldn't change the speed of " + Path.GetFileName(audioLocation) + ".\n";
        }

        return errors;
    }

    public void OnChPackageToggle(bool enabled)
    {
        if (enabled)
        {
            generateIniToggle.isOn = enabled;
            fileTypeDropdown.value = 0;
        }

        generateIniToggle.interactable = !enabled;        
        fileTypeDropdown.interactable = !enabled;
        magmaButton.interactable = !enabled;
        exportingInfo.text = chPackageToggle.isOn ? chPackageText : chartInfoText;
    }

    public void ExportSong()
    {
        if (editor.BlockedSaveForDuetGuest())
            return;

        if (chPackageToggle.isOn)
        {
            ExportCHPackage();
        }
        else
        {
            _ExportSong();
        }
    }

    void _ExportSong()
    {
        string defaultFileName;
        if (editor.currentSong.name != string.Empty)
            defaultFileName = new string(editor.currentSong.name.ToCharArray());
        else
            defaultFileName = "Untitled";
        if (exportSpeed != 1 && SpeedOptions.exportSpeedSuffix)
            defaultFileName += SpeedSuffix(exportSpeed);
        if (!exportOptions.forced)
            defaultFileName += "(UNFORCED)";

        string saveLocation;
        bool aquiredFilePath = false;

        // Open up file explorer and get save location
        if (exportOptions.format == ExportOptions.Format.Chart)
        {
            aquiredFilePath = FileExplorer.SaveFilePanel(new ExtensionFilter("Chart files", "chart"), defaultFileName, "chart", out saveLocation);
        }
        else if (exportOptions.format == ExportOptions.Format.Midi)
        {
            aquiredFilePath = FileExplorer.SaveFilePanel(new ExtensionFilter("Midi files", "mid"), defaultFileName, "mid", out saveLocation);
        }
        else
            throw new Exception("Invalid file extension");

        if (!aquiredFilePath)
            return;

        // Everything goes in a folder named after what was typed (the song's name unless changed), with the chart
        // named notes.chart (or notes.mid) as Clone Hero expects
        string folderName = FileExplorer.StripIllegalChars(Path.GetFileNameWithoutExtension(saveLocation)).Trim();
        if (folderName == string.Empty)
            folderName = "Untitled";
        string folder = Path.Combine(Path.GetDirectoryName(saveLocation), folderName);
        string notesFile = Path.Combine(folder, "notes" + (exportOptions.format == ExportOptions.Format.Midi ? FILE_EXT_MIDI : FILE_EXT_CHART));

        if (File.Exists(notesFile))
        {
            NativeWindow window = ChartEditor.Instance.windowHandleManager.nativeWindow;
            NativeMessageBox.Result overwrite = NativeMessageBox.Show("\"" + folderName + "\" already has a " + Path.GetFileName(notesFile) + ". Overwrite it?", "Warning", NativeMessageBox.Type.YesNo, window);
            if (overwrite != NativeMessageBox.Result.Yes)
                return;
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e)
        {
            ChartEditor.Instance.errorManager.QueueErrorMessage("Couldn't make the folder " + folder + ": " + e.Message);
            return;
        }

        StartCoroutine(_ExportSong(notesFile));
    }

    public void ExportCHPackage()
    {
        string saveDirectory;
        if (FileExplorer.OpenFolderPanel(out saveDirectory))
        {
            Song song = editor.currentSong;
            float songLength = editor.currentSongLength;
            string folderName = song.name;
            if (exportSpeed != 1 && SpeedOptions.exportSpeedSuffix)
                folderName += SpeedSuffix(exportSpeed);

            saveDirectory = saveDirectory.Replace('\\', Path.DirectorySeparatorChar);
            saveDirectory = saveDirectory.Replace('/', Path.DirectorySeparatorChar);
            saveDirectory = Path.Combine(saveDirectory, FileExplorer.StripIllegalChars(folderName).Trim());

            // Check if files exist at the current directory and ask user before overwriting.
            if (Directory.Exists(saveDirectory)) {
                NativeWindow window = ChartEditor.Instance.windowHandleManager.nativeWindow;
                NativeMessageBox.Result overwrite = NativeMessageBox.Show("Exported files exist. Overwrite?", "Warning", NativeMessageBox.Type.YesNo, window);

                if (overwrite != NativeMessageBox.Result.Yes) {
                    return;
                }
            }

            StartCoroutine(ExportCHPackage(saveDirectory, song, songLength, exportOptions));
        }
    }

    public IEnumerator _ExportSong(string filepath)
    {
        LoadingTasksManager tasksManager = editor.services.loadingTasksManager;

        float speed = exportSpeed;
        Song originalSong = editor.currentSong;
        Song song = SongAtExportSpeed(originalSong, speed);
        float songLength = editor.currentSongLength / speed;

        exportOptions.tickOffset = TickFunctions.TimeToDis(0, delayTime, exportOptions.targetResolution, 120);

        float timer = Time.realtimeSinceStartup;
        string errorMessageList = string.Empty;

        List<LoadingTask> tasks = new List<LoadingTask>();
        if (speed != 1)
        {
            tasks.Add(new LoadingTask("Changing the audio to " + SpeedOptions.SpeedText(speed) + " speed", () =>
            {
                string audioErrors = ExportAudioAtSpeed(originalSong, song, Path.GetDirectoryName(filepath), speed);
                if (audioErrors != null)
                    errorMessageList += audioErrors;
            }));
        }

        tasks.AddRange(new List<LoadingTask>()
        {
            new LoadingTask("Exporting " + exportOptions.format, () =>
            {
                if (exportOptions.format == ExportOptions.Format.Chart)
                {
                    try
                    {
                        ChartWriter.ErrorReport errorReport;

                        Debug.Log("Exporting CHART file to " + filepath);
                        new ChartWriter(filepath).Write(song, exportOptions, out errorReport);

                        if (errorReport.HasErrorsOrWarnings)
                        {
                            errorMessageList = errorReport.GetFullReport().ToString();
                        }
                    }
                    catch (System.Exception e)
                    {
                        Logger.LogException(e, "Error when exporting chart");
                        errorMessageList += e.Message;
                    }
                }
                else if (exportOptions.format == ExportOptions.Format.Midi)
                {
                    try
                    {
                        Debug.Log("Exporting MIDI file to " + filepath);
                        MidWriter.WriteToFile(filepath, song, exportOptions);
                    }
                    catch (System.Exception e)
                    {
                        Logger.LogException(e, "Error when exporting midi");
                        errorMessageList += e.Message;
                    }
                }
            })
        });

        if (generateIniToggle.isOn)
        {
            tasks.Add(new LoadingTask("Generating Song.ini", () =>
            {
                GenerateSongIni(Path.GetDirectoryName(filepath), song, songLength);
            }));
        }

        tasksManager.KickTasks(tasks);

        while (tasksManager.isRunningTask)
            yield return null;

        Debug.Log("Total exporting time: " + (Time.realtimeSinceStartup - timer));

        if (exportOptions.format == ExportOptions.Format.Midi)
        {
            bool hasErrors;
            SongValidate.ValidationParameters validateParams = new SongValidate.ValidationParameters() { songLength = editor.currentSongLength, checkMidiIssues = true, };
            string validationErrors = SongValidate.GenerateReport(Globals.gameSettings.songValidatorModes, editor.currentSong, validateParams, out hasErrors);

            if (hasErrors)
            {
                errorMessageList += '\n';
                errorMessageList += validationErrors;
            }
        }

        if (errorMessageList != string.Empty)
        {
            Disable();
            ChartEditor.Instance.errorManager.QueueErrorMessage("Encountered the following errors while exporting: " + Globals.LINE_ENDING + errorMessageList);
        }
    }

    public void SetForced(bool forced)
    {
        exportOptions.forced = forced;
    }

    public void SetCopyDiff(bool val)
    {
        exportOptions.copyDownEmptyDifficulty = val;
    }

    public void SetFile(int value)
    {
        switch (value)
        {
            case 1:
                setAsMidFile();
                break;
            case 0:
            default:
                setAsChartFile();
                break;
        }
    }

    void setAsChartFile()
    {
        exportOptions.format = ExportOptions.Format.Chart;
        exportingInfo.text = chPackageToggle.isOn ? chPackageText : chartInfoText;
        midiSettings.gameObject.SetActive(false);
    }

    void setAsMidFile()
    {
        exportOptions.format = ExportOptions.Format.Midi;
        exportingInfo.text = midInfoText;
        midiSettings.gameObject.SetActive(true);
    }

    public void SetTrackEventDifficulty(int value)
    {
        Song.Difficulty diff;

        switch (value)
        {
            case 1:
                diff = Song.Difficulty.Hard;
                break;

            case 2:
                diff = Song.Difficulty.Medium;
                break;

            case 3:
                diff = Song.Difficulty.Easy;
                break;

            case 0:
            default:
                diff = Song.Difficulty.Expert;
                break;
        }

        exportOptions.midiOptions.difficultyToUseGlobalTrackEvents = diff;
        Debug.Log("Set midi track event difficulty");
    }

    public void SetRbFormat(int value)
    {
        ExportOptions.MidiOptions.RBFormat rbFormat;

        switch (value)
        {
            case 1:
                rbFormat = ExportOptions.MidiOptions.RBFormat.RB3;
                break;
            case 0:
            default:
                rbFormat = ExportOptions.MidiOptions.RBFormat.RB2;
                break;
        }

        exportOptions.midiOptions.rbFormat = rbFormat;
    }

    public void SetResolution(string val)
    {
        int res;
        if (int.TryParse(val, out res) && res != 0)
            exportOptions.targetResolution = Mathf.Abs(res);
    }

    public void SetResolutionEnd(string val)
    {
        int res;
        if (!int.TryParse(val, out res))
            res = (int)SongConfig.STANDARD_BEAT_RESOLUTION;

        if (res == 0)
            res = (int)(editor.currentSong.resolution);

        exportOptions.targetResolution = Mathf.Abs(res);
        targetResolution.text = exportOptions.targetResolution.ToString();
    }

    public void SetDelay(string val)
    {
        float delay;
        if (float.TryParse(val, out delay) && delay != 0)
            delayTime = Mathf.Abs(delay);
    }

    public void SetDelayEnd(string val)
    {
        float delay;
        if (!float.TryParse(val, out delay))
            delay = 0;

        delayTime = Mathf.Abs(delay);
        delayInputField.text = delayTime.ToString();
    }

    public void SetRBMagmaExport()
    {
        fileTypeDropdown.value = 1;
        forcedToggle.isOn = false;
        copyDifficultiesToggle.isOn = true;
        targetResolution.text = "480";
        delayInputField.text = "2.5";
    }

    static void GenerateSongIni(string path, Song song, float songLengthSeconds, bool isChPackage = false)
    {
        Metadata metaData = song.metaData;
        INIParser parser = new INIParser();

        try
        {
            parser.Open(Path.Combine(path, "song.ini"));

            // Clone explicit properties
            parser.WriteValue(song.iniProperties);

            SongIniFunctions.AddDefaultIniTags(song, parser, songLengthSeconds);

            if (isChPackage)
            {
                // Write defaults for any missing CH tags
                SongIniFunctions.AddCloneHeroIniTags(song, parser, songLengthSeconds);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError("Error encountered when trying to generate song.ini. " + e.Message);
        }
        finally
        {
            parser.Close();
        }
    }

    static readonly Dictionary<Song.AudioInstrument, string> audioInstrumentToCHNameMap = new Dictionary<Song.AudioInstrument, string>()
    {
        { Song.AudioInstrument.Song, "song" },
        { Song.AudioInstrument.Guitar, "guitar" },
        { Song.AudioInstrument.Bass, "bass" },
        { Song.AudioInstrument.Rhythm, "rhythm" },
		{ Song.AudioInstrument.Keys, "keys" },
		{ Song.AudioInstrument.Drum, "drums" },
		{ Song.AudioInstrument.Drums_2, "drums_2" },
		{ Song.AudioInstrument.Drums_3, "drums_3" },
		{ Song.AudioInstrument.Drums_4, "drums_4" },
		{ Song.AudioInstrument.Vocals, "vocals" },
		{ Song.AudioInstrument.Crowd, "crowd" },
    };

    static string GetCHOggFilename(Song.AudioInstrument audio)
    {
        const string audioFormat = ".ogg";

        string newAudioName = string.Empty;
        if (audioInstrumentToCHNameMap.TryGetValue(audio, out newAudioName))
        {
            newAudioName += audioFormat;
        }
        else
        {
            Debug.LogErrorFormat("Audio instrument {0} not set up in ch name dict. Skipping.", audio.ToString());
        }

        return newAudioName;
    }

    IEnumerator ExportCHPackage(string destFolderPath, Song song, float songLengthSeconds, ExportOptions exportOptions)
    {
        float speed = exportSpeed;
        bool pitched = SpeedOptions.pitchedSpeedups;
        Song newSong = speed != 1 ? SongAtExportSpeed(song, speed) : new Song(song);
        songLengthSeconds /= speed;
        LoadingTasksManager tasksManager = editor.services.loadingTasksManager;

        float timer = Time.realtimeSinceStartup;
        string errorMessageList = string.Empty;

        Directory.CreateDirectory(destFolderPath);

        List<LoadingTask> tasks = new List<LoadingTask>();

        // Determine how many actions ahead of time so that we can display progress of how many songs it has re-encoded, eg "(1/2)"
        List<Action> songEncodeActions = new List<Action>();
        foreach (Song.AudioInstrument audio in EnumX<Song.AudioInstrument>.Values)
        {
            string audioLocation = song.GetAudioLocation(audio);
            if (audioLocation == string.Empty)
                continue;

            if (!File.Exists(audioLocation))
            {
                Debug.LogErrorFormat("Unable to find audio file in location {0}", audioLocation);
                continue;
            }

            string newAudioName = GetCHOggFilename(audio);

            if (string.IsNullOrEmpty(newAudioName))
            {
                Debug.LogErrorFormat("Audio instrument {0} not set up in ch name dict. Skipping.", audio.ToString());
                continue;
            }

            string outputFile = Path.Combine(destFolderPath, newAudioName);

            songEncodeActions.Add(() =>
            {
                if (speed != 1)
                {
                    // A copy of the original would be at the wrong speed, so it's an error rather than a fallback
                    if (string.Equals(Path.GetFullPath(outputFile), Path.GetFullPath(audioLocation), StringComparison.OrdinalIgnoreCase))
                        errorMessageList += "Didn't write " + newAudioName + " because it would replace the original audio. Export into a different folder.\n";
                    else if (!AudioManager.ConvertToOggAtSpeed(audioLocation, outputFile, speed, pitched))
                        errorMessageList += "Couldn't change the speed of " + Path.GetFileName(audioLocation) + ".\n";
                    return;
                }

                bool shouldCopy = false;

                foreach (string extention in COPY_IF_EXTENTION)
                {
                    if (audioLocation.EndsWith(extention))
                    {
                        shouldCopy = true;
                    }
                }

                if (!shouldCopy)
                {
                    Debug.LogFormat("Converting ogg from {0} to {1}", audioLocation, outputFile);
                    shouldCopy = !AudioManager.ConvertToOgg(audioLocation, outputFile);
                }

                if (shouldCopy)
                {
                    // Failed to re-encode, just copy the file as a backup
                    string originalExtention = Path.GetExtension(audioLocation);
                    string destPath = Path.ChangeExtension(outputFile, originalExtention);

                    Debug.LogFormat("Unable to re-encode file, copying {0} to {1}", audioLocation, destPath);
                    File.Copy(audioLocation, destPath, true);
                }
            });         
        }

        for (int i = 0; i < songEncodeActions.Count; ++i)
        {
            tasks.Add(new LoadingTask(string.Format("Preparing audio track ({0}/{1})", i + 1, songEncodeActions.Count), songEncodeActions[i]));
        }

        tasks.Add(new LoadingTask("Exporting chart", () =>
        {
            string chartOutputFile = Path.Combine(destFolderPath, "notes.chart");

            // Set audio location after audio files have already been created as set won't won't if the files don't exist
            foreach (Song.AudioInstrument audio in EnumX<Song.AudioInstrument>.Values)
            {
                if (song.GetAudioLocation(audio) != string.Empty)
                {
                    string audioFilename = GetCHOggFilename(audio);
                    string audioPath = Path.Combine(destFolderPath, audioFilename);
                    newSong.SetAudioLocation(audio, audioPath);
                }
            }

            ChartWriter.ErrorReport errorReport;

            new ChartWriter(chartOutputFile).Write(newSong, exportOptions, out errorReport);
            GenerateSongIni(destFolderPath, newSong, songLengthSeconds, true);

            if (errorReport.HasErrorsOrWarnings)
            {
                errorMessageList += errorReport.GetFullReport().ToString();
            }
        }));

        tasksManager.KickTasks(tasks);

        while (tasksManager.isRunningTask)
            yield return null;

        Debug.Log("Total exporting time: " + (Time.realtimeSinceStartup - timer));

        if (errorMessageList != string.Empty)
        {
            ChartEditor.Instance.errorManager.QueueErrorMessage("Encountered the following errors while exporting: " + Globals.LINE_ENDING + errorMessageList);
        }
    }
}
