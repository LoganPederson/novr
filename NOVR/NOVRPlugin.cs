using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NOVR.PatchHelper;
using NOVR.VrCamera;
using NOVR.VrUi;
using NOVR.VrUi.SpecialBehavior;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using System.Windows.Forms;
using Application = UnityEngine.Application;
using Debug = UnityEngine.Debug;

namespace NOVR;

[BepInPlugin(
    "deltawing.novr",
    "NOVR",
    NOVRVersion.Version)] // Set from NovrVersion in NOVR.Build/NOVR.Build.props
public class NOVRPlugin : BaseUnityPlugin
{
    public static ManualLogSource LogSource => _instance?.Logger;
    
    private static NOVRPlugin _instance;
    public static string ModFolderPath { get; private set; }

    public NOVRPlugin()
    {
        
        InputTracking.trackingAcquired += TrackingAcquired;
        _instance = this;
        ModFolderPath = Path.GetDirectoryName(Assembly.GetAssembly(typeof(NOVRPlugin)).Location);

        WarnIfLegacyUuvrFoldersExist();
        
        new ModConfiguration(Config);
        var harm = ApplyHarmonyPatches();
        PatchLoader.Apply(harm);
        Core.Create();
    }

    // Patch each class on its own so a game update that renames one target only disables that
    // patch, instead of Harmony.CreateAndPatchAll aborting and skipping every patch after it.
    private Harmony ApplyHarmonyPatches()
    {
        var harmony = new Harmony("deltawing.novr");
        var applied = 0;
        var failed = 0;
        foreach (var type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
        {
            try
            {
                var patched = harmony.CreateClassProcessor(type).Patch();
                if (patched != null && patched.Count > 0) applied++;
            }
            catch (Exception exception)
            {
                failed++;
                Logger.LogError($"Failed to apply patch {type.FullName}; that feature is disabled. " +
                                $"This usually means the game updated and NOVR needs an update too.\n{exception}");
            }
        }

        if (failed > 0)
            Logger.LogWarning($"Applied {applied} Harmony patch classes, {failed} failed. See errors above.");
        else
            Logger.LogInfo($"Applied {applied} Harmony patch classes.");
        return harmony;
    }

    private void TrackingAcquired(XRNodeState obj)
    {
        NOVRHeadsetData.CalibrateTranslation();
    }
     
    private void Awake()
    {

    }

    private void WarnIfLegacyUuvrFoldersExist()
    {
        var bepInExRootPath = BepInEx.Paths.BepInExRootPath;
        var legacyPluginPath = Path.Combine(bepInExRootPath, "Plugins", "UUVR");
        var legacyPatcherPath = Path.Combine(bepInExRootPath, "Patchers", "UUVR");

        var hasLegacyPluginFolder = Directory.Exists(legacyPluginPath);
        var hasLegacyPatcherFolder = Directory.Exists(legacyPatcherPath);

        if (!hasLegacyPluginFolder && !hasLegacyPatcherFolder)
        {
            return;
        }

        var message =
            "NOVR detected legacy UUVR files in your BepInEx install.\n\n" +
            "This is a hard conflict and will cause NOVR to break or fail to load correctly.\n\n" +
            "Click Delete to remove them now.\n\n" +
            $"{(hasLegacyPluginFolder ? $"- {legacyPluginPath}\n" : string.Empty)}" +
            $"{(hasLegacyPatcherFolder ? $"- {legacyPatcherPath}\n" : string.Empty)}";

        var result = ShowLegacyUuvrConflictDialog(message);

        if (result != DialogResult.Yes)
        {
            return;
        }

        DeleteLegacyUuvrFolders(
            hasLegacyPluginFolder ? legacyPluginPath : null,
            hasLegacyPatcherFolder ? legacyPatcherPath : null);
    }

    private void DeleteLegacyUuvrFolders(params string?[] foldersToDelete)
    {
        var deletedAnyFolders = false;
        foreach (var folder in foldersToDelete.Where(folder => !string.IsNullOrWhiteSpace(folder)))
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                    deletedAnyFolders = true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"NOVR failed to delete legacy UUVR folder '{folder}': {exception}");
            }
        }

        if (deletedAnyFolders)
        {
            MessageBox.Show(
                "Legacy UUVR folder(s) were deleted.\n\nPlease restart the game to finish removing the conflict.",
                "NOVR",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Application.Quit();
        }
    }

    private static DialogResult ShowLegacyUuvrConflictDialog(string message)
    {
        using var dialog = new LegacyUuvrConflictForm(message);
        return dialog.ShowDialog();
    }

    private sealed class LegacyUuvrConflictForm : Form
    {
        public LegacyUuvrConflictForm(string message)
        {
            Text = "NOVR - UUVR Conflict Detected";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(680, 260);
            Padding = new Padding(16);
            Font = SystemFonts.MessageBoxFont;

            var warningLabel = new Label
            {
                Dock = DockStyle.Fill,
                Text = message,
                AutoSize = false
            };

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false
            };

            var deleteAndRestartButton = new Button
            {
                Text = "Delete",
                DialogResult = DialogResult.Yes,
                AutoSize = true,
                MinimumSize = new Size(100, 30)
            };

            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                AutoSize = true,
                MinimumSize = new Size(90, 30)
            };

            buttonPanel.Controls.Add(deleteAndRestartButton);
            buttonPanel.Controls.Add(cancelButton);

            AcceptButton = deleteAndRestartButton;
            CancelButton = cancelButton;

            Controls.Add(warningLabel);
            Controls.Add(buttonPanel);
        }
    }
    
    
}
