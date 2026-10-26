// See LICENSE in project root for license information.

using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Installs a BetterMoonscraper update from this repository's GitHub releases: downloads the release's Windows zip,
/// then once BetterMoonscraper closes, a small PowerShell script unpacks it over this copy (anything else in the
/// folder, such as Custom Resources, is kept) and opens the new version. Settings live in AppData, so they stay.
/// </summary>
public class UpdateInstaller : MonoBehaviour
{
    const string ZipSuffix = "Windows-x64.zip";

    static UpdateInstaller s_instance;

    public static bool isBusy { get { return s_instance != null && s_instance.m_busy; } }
    public static string status { get; private set; }
    public static float progress { get; private set; }

    bool m_busy;
    UnityWebRequest m_request;

    /// <summary>
    /// The download link of the release's Windows zip, or null if it has none
    /// </summary>
    public static string FindZipUrl(Octokit.Release release)
    {
        if (release == null || release.Assets == null)
            return null;

        foreach (Octokit.ReleaseAsset asset in release.Assets)
        {
            if (asset.Name.EndsWith(ZipSuffix, StringComparison.OrdinalIgnoreCase))
                return asset.BrowserDownloadUrl;
        }
        return null;
    }

    public static bool canInstallHere
    {
        get
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>
    /// Downloads the update, then closes BetterMoonscraper (asking to save first, as closing always does) so it can
    /// be put in place. onDone is told whether it got that far, with status saying why not.
    /// </summary>
    public static void DownloadAndInstall(Octokit.Release release, Action<bool> onDone)
    {
        if (isBusy)
            return;

        if (!s_instance)
        {
            GameObject holder = new GameObject("Update Installer");
            DontDestroyOnLoad(holder);
            s_instance = holder.AddComponent<UpdateInstaller>();
        }
        s_instance.StartCoroutine(s_instance.Run(release, onDone));
    }

    IEnumerator Run(Octokit.Release release, Action<bool> onDone)
    {
        m_busy = true;
        progress = 0;
        string url = FindZipUrl(release);
        if (url == null)
        {
            Finish(onDone, false, "This release has no Windows download yet. Try again in a few minutes.");
            yield break;
        }

        string folder = Path.Combine(Application.persistentDataPath, "Update");
        string zip = Path.Combine(folder, "BetterMoonscraper update.zip");
        try
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(zip))
                File.Delete(zip);
        }
        catch (Exception e)
        {
            Finish(onDone, false, "Couldn't make room for the download: " + e.Message);
            yield break;
        }

        status = "Downloading " + release.TagName + "...";
        m_request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
        m_request.downloadHandler = new DownloadHandlerFile(zip) { removeFileOnAbort = true };
        UnityWebRequestAsyncOperation operation = m_request.SendWebRequest();
        while (!operation.isDone)
        {
            progress = m_request.downloadProgress;
            status = "Downloading " + release.TagName + "... " + Mathf.RoundToInt(progress * 100) + "%";
            yield return null;
        }

        bool failed = m_request.isNetworkError || m_request.isHttpError;
        string error = m_request.error;
        m_request.Dispose();
        m_request = null;
        if (failed)
        {
            Finish(onDone, false, "Couldn't download the update: " + error);
            yield break;
        }

        string script;
        try
        {
            script = WriteInstallScript(folder);
            StartInstaller(script, zip);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't start the update: " + e);
            Finish(onDone, false, "Couldn't start the update: " + e.Message);
            yield break;
        }

        Finish(onDone, true, "Downloaded. BetterMoonscraper closes now and opens again as " + release.TagName + ".");
        yield return new WaitForSecondsRealtime(1.5f);
        Application.Quit();     // Asks about unsaved changes first; the update waits for BetterMoonscraper to close
    }

    void Finish(Action<bool> onDone, bool ok, string message)
    {
        status = message;
        m_busy = false;
        if (onDone != null)
            onDone(ok);
    }

    void OnDestroy()
    {
        if (m_request != null)
            m_request.Abort();
    }

    // The folder BetterMoonscraper runs from: the one holding <name>_Data
    static string appFolder
    {
        get { return Directory.GetParent(Application.dataPath).FullName; }
    }

    static string WriteInstallScript(string folder)
    {
        string path = Path.Combine(folder, "install-update.ps1");
        File.WriteAllText(path, InstallScript);
        return path;
    }

    static void StartInstaller(string script, string zip)
    {
        string log = Path.Combine(Path.GetDirectoryName(script), "Update log.txt");
        string arguments = string.Format("-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{0}\" -ProcessId {1} -Zip \"{2}\" -AppFolder \"{3}\" -Log \"{4}\"",
            script, System.Diagnostics.Process.GetCurrentProcess().Id, zip, appFolder, log);

        System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("powershell.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        System.Diagnostics.Process.Start(info);
    }

    // Waits for BetterMoonscraper to close, copies the new files over this copy, and opens it again. Copying over
    // (rather than replacing the folder) keeps Custom Resources and anything else added to it.
    const string InstallScript = @"param([int]$ProcessId, [string]$Zip, [string]$AppFolder, [string]$Log)
$ErrorActionPreference = 'Stop'
Start-Transcript -Path $Log -Force | Out-Null
try {
    Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    $unpacked = Join-Path ([IO.Path]::GetTempPath()) ('BetterMoonscraper update ' + [guid]::NewGuid())
    Expand-Archive -LiteralPath $Zip -DestinationPath $unpacked -Force

    # The zip holds one folder with the exe in it
    $exe = Get-ChildItem -LiteralPath $unpacked -Recurse -Filter '*.exe' | Where-Object { $_.Name -ne 'UnityCrashHandler64.exe' -and $_.Name -ne 'UnityCrashHandler32.exe' } | Select-Object -First 1
    if (-not $exe) { throw 'The update has no program in it' }
    $source = $exe.DirectoryName

    robocopy $source $AppFolder /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw ""Copying the update failed (robocopy $LASTEXITCODE)"" }

    Remove-Item -LiteralPath $unpacked -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $Zip -Force -ErrorAction SilentlyContinue
    Write-Output 'Update installed'
    Start-Process -FilePath (Join-Path $AppFolder $exe.Name) -WorkingDirectory $AppFolder
}
catch {
    Write-Output ('Update failed: ' + $_)
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show('BetterMoonscraper could not install the update: ' + $_ + [Environment]::NewLine + 'Download it from GitHub instead. Details are in ' + $Log, 'BetterMoonscraper update') | Out-Null
}
finally {
    Stop-Transcript | Out-Null
}
";
}
