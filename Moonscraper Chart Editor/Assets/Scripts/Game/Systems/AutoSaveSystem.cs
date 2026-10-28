// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using UnityEngine;
using System.Threading.Tasks;
using MoonscraperEngine;
using MoonscraperChartEditor.Song;
using MoonscraperChartEditor.Song.IO;

public class AutoSaveSystem : SystemManagerState.System
{
    const float AUTOSAVE_RUN_INTERVAL = 60; // Once a minute
    float autosaveTimer = 0;
    Song autosaveSong = null;
    Task currentAutosaveTask;

    // Besides the backup copy above, the open chart file itself can be saved every few minutes. Set in Settings > Editor.
    const string PrefChartMinutes = "Autosave.ChartMinutes";
    public static readonly int[] ChartMinuteChoices = { 0, 1, 2, 5, 10 };
    float chartAutosaveTimer = 0;
    bool chartAutosaving = false;     // Waiting for the save to finish, to say so

    // Off for someone new. Anyone who already used a build with chart autosave on (it used to start at 2 minutes) keeps it.
    static readonly string[] s_signsOfEarlierUse =
    {
        "Duet.Username", "Duet.Address", "Duet.UseRooms", "Display.GemSizePercent", "Speed.NonSpacedSlowdown",
        "Export.SpeedSuffix", "Customize.CloneHeroFolder", "Customize.ColorProfile.Source", "Customize.Highway.Source",
    };

    public static int chartAutosaveMinutes
    {
        get
        {
            if (!PlayerPrefs.HasKey(PrefChartMinutes))
            {
                bool usedBefore = System.Array.Exists(s_signsOfEarlierUse, PlayerPrefs.HasKey);
                PlayerPrefs.SetInt(PrefChartMinutes, usedBefore ? 2 : 0);
                PlayerPrefs.Save();
            }
            return Mathf.Max(0, PlayerPrefs.GetInt(PrefChartMinutes, 0));
        }
        set { PlayerPrefs.SetInt(PrefChartMinutes, Mathf.Max(0, value)); PlayerPrefs.Save(); }
    }

    public override void SystemUpdate()
    {
        if (DuetPanel.isGuest)
            return;     // The chart is the Duet session host's, so it isn't kept here

        UpdateChartAutosave();

        if (currentAutosaveTask == null || currentAutosaveTask.Status != TaskStatus.RanToCompletion)
        {
            autosaveTimer += Time.deltaTime;

            if (autosaveTimer > AUTOSAVE_RUN_INTERVAL)
            {
                currentAutosaveTask = Autosave();
            }
        }
        else
        {
            autosaveTimer = 0;
        }
    }

    void UpdateChartAutosave()
    {
        ChartEditor editor = ChartEditor.Instance;
        if (chartAutosaving && !editor.isSaving)
        {
            chartAutosaving = false;
            editor.services.notificationBar.PushNotification("CHART AUTOSAVED", 2, true);
        }

        int minutes = chartAutosaveMinutes;
        if (minutes <= 0)
        {
            chartAutosaveTimer = 0;
            return;
        }

        chartAutosaveTimer += Time.deltaTime;
        if (chartAutosaveTimer < minutes * 60)
            return;

        // Waits for a quiet moment: not mid-drag or mid-save. Untitled charts and .mid files are left for the user to save.
        string file = editor.currentChartFile;
        if (string.IsNullOrEmpty(file) || editor.isSaving || editor.songEditGestureTracker.hasPendingEdit)
            return;

        chartAutosaveTimer = 0;
        string extension = System.IO.Path.GetExtension(file).ToLowerInvariant();
        if (!ChartEditor.isDirty || (extension != ".chart" && extension != MsceIOHelper.FileExtention))
            return;

        Debug.Log("Autosaving the chart to " + file);
        chartAutosaving = true;
        editor.AutoSaveChart();
    }

    async Task Autosave()
    {
        ChartEditor editor = ChartEditor.Instance;

        autosaveSong = new Song(editor.currentSong);

        await Task.Run(() =>
        {
            autosaveTimer = 0;
            Debug.Log("Autosaving...");

            try
            {
                ChartWriter.ErrorReport errorReport;
                new ChartWriter(Globals.autosaveLocation).Write(autosaveSong, editor.currentSong.defaultExportOptions, out errorReport);

                if (errorReport.HasErrors && errorReport.hasNonErrorFileTypeRelatedErrors)
                {
                    string saveErrorMessage = errorReport.GetFullReport();
                    string errorMessage = "Autosave completed with the following errors: " + System.Environment.NewLine + saveErrorMessage;
                    ChartEditor.Instance.errorManager.QueueErrorMessage(errorMessage);
                    Debug.LogError(errorMessage);
                }
            }
            catch (System.Exception e)
            {
                ChartEditor.Instance.errorManager.QueueErrorMessage("Autosave failed!" + System.Environment.NewLine + e.Message);
                Logger.LogException(e, "Autosave failed!");
            }

            Debug.Log("Autosave complete!");
            autosaveTimer = 0;
        });
    }
}
