using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UpdatesMenu : MonoBehaviour
{
    [SerializeField]
    Button downloadLatestButton;
    [SerializeField]
    Text textInfo;
    [SerializeField]
    Toggle checkUpdateToggle;

    Octokit.Release m_release;

    public void Populate(Octokit.Release latestRelease)
    {
        string currentVersion = Application.version;
        m_release = latestRelease;

        bool hasNewerVersion = latestRelease != null;
        downloadLatestButton.gameObject.SetActive(hasNewerVersion);

        if (hasNewerVersion)
        {
            bool canInstall = UpdateInstaller.canInstallHere && UpdateInstaller.FindZipUrl(latestRelease) != null;
            textInfo.text = string.Format("A new version of BetterMoonscraper is available.\n\nCurrent version- v{0}\nLatest version- {1}{2}",
                currentVersion, latestRelease.TagName,
                canInstall ? "\n\nInstall it now: it downloads, BetterMoonscraper closes (asking to save first) and opens again updated. Your settings stay." : string.Empty);
            SetButtonText(canInstall ? "Install update" : "Download");

            downloadLatestButton.interactable = true;
            downloadLatestButton.onClick.RemoveAllListeners();
            downloadLatestButton.onClick.AddListener(() =>
            {
                if (!canInstall)
                {
                    Application.OpenURL(latestRelease.HtmlUrl);
                    return;
                }

                downloadLatestButton.interactable = false;
                UpdateInstaller.DownloadAndInstall(latestRelease, (ok) =>
                {
                    if (!ok && this)
                        downloadLatestButton.interactable = true;
                });
            });
        }
        else
        {
            textInfo.text = string.Format("BetterMoonscraper is up to date. The current version is v{0}", currentVersion);
        }
    }

    void Update()
    {
        // Download progress and anything that went wrong
        if (m_release != null && (UpdateInstaller.isBusy || !string.IsNullOrEmpty(UpdateInstaller.status)) && textInfo.text != UpdateInstaller.status)
            textInfo.text = UpdateInstaller.status;
    }

    void SetButtonText(string label)
    {
        Text text = downloadLatestButton.GetComponentInChildren<Text>(true);
        if (text)
            text.text = label;
    }

    private void OnEnable()
    {
        checkUpdateToggle.isOn = Globals.gameSettings.automaticallyCheckForUpdates;
    }

    public void OnAutoCheckUpdatesToggles(bool updatesEnabled)
    {
        Globals.gameSettings.automaticallyCheckForUpdates.value = updatesEnabled;
    }
}
