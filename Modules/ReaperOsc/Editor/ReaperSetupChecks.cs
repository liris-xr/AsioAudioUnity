using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>The REAPER OSC plugin's checks in the Setup Check window (AsioAudioUnity > Setup Check).</summary>
    [InitializeOnLoad]
    internal static class ReaperSetupChecks
    {
        static ReaperSetupChecks()
        {
            AsioSetupChecks.Register("REAPER", 20, Checks);
            AsioSetupChecks.RegisterSceneObject("REAPER Controller", 20,
                () => Object.FindObjectsByType<ReaperController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0,
                () =>
                {
                    GameObject go = GlobalAsioAudioEditor.CreateGameObjectInScene("REAPER Controller", new MenuCommand(null));
                    Undo.AddComponent<ReaperController>(go);
                    return go;
                });
        }

        private const string TwoWaySync = "Reference/ReaperController.md#two-way-sync";
        private const string ScriptsHelp = "Reference/ReaperController.md#lua-scripts";

        private static IEnumerable<AsioSetupCheck> Checks()
        {
            ReaperController[] controllers = Object.FindObjectsByType<ReaperController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (controllers.Length == 0)
            {
                yield return new AsioSetupCheck("No REAPER Controller (optional)", AsioCheckStatus.Info,
                    "Add one to make REAPER follow Unity's transport, or to send REAPER commands.", "Create",
                    () => EditorApplication.ExecuteMenuItem("GameObject/AsioAudioUnity/REAPER Controller")).Help("Tutorials/7-ControlReaper.md");
                yield break;
            }

            foreach (ReaperController controller in controllers)
            {
                System.Action select = () => Selection.activeObject = controller.gameObject;
                yield return new AsioSetupCheck("REAPER Controller \"" + controller.name + "\": sends to " + controller.ReaperAddress + ":" + controller.ReaperPort,
                    controller.isActiveAndEnabled ? AsioCheckStatus.Ok : AsioCheckStatus.Warning,
                    (controller.isActiveAndEnabled ? "" : "It is disabled. ") + "REAPER needs an OSC control surface listening on port " + controller.ReaperPort
                    + " (Preferences > Control/OSC/web). Follow Transport: " + (controller.FollowTransport ? "on." : "off."), "Select", select)
                    .Help("Tutorials/7-ControlReaper.md#let-reaper-receive-osc");

                List<string> outdated = ReaperScriptInstaller.OutdatedScripts();
                if (outdated.Count > 0)
                {
                    yield return new AsioSetupCheck("Scripts installed in REAPER are outdated", AsioCheckStatus.Warning,
                        "REAPER runs its installed copies, which differ from the package's: " + string.Join(", ", outdated) + ". Updating keeps the command IDs.", "Update", () =>
                        {
                            try { ReaperScriptInstaller.Install(); }
                            catch (System.Exception e) { Debug.LogError("AsioAudioUnity: could not update the scripts installed in REAPER: " + e.Message); }
                        }).Help(ScriptsHelp);
                }

                bool registered = System.IO.File.Exists(ReaperController.RegisteredScriptIdsPath);
                if (registered && controller.ScriptIdsDifferFromReaper())
                {
                    yield return new AsioSetupCheck("Script IDs differ from REAPER's", AsioCheckStatus.Warning,
                        "RegisterScripts.lua gave other command IDs than the REAPER Controller's (or ones it doesn't have yet).", "Fill from REAPER", () =>
                        {
                            Undo.RecordObject(controller, "Fill Script IDs from REAPER");
                            controller.FillScriptIdsFromReaper();
                            EditorUtility.SetDirty(controller);
                        }).Help(ScriptsHelp);
                }

                if (controller.Commands.Any(command => command != null && command.Type == ReaperCommandType.PlayRegion) && string.IsNullOrWhiteSpace(controller.PlayRegionScriptId))
                {
                    yield return new AsioSetupCheck("Play Region commands without PlayRegion.lua", AsioCheckStatus.Warning,
                        "Without the script's command ID, they go to the region without looping it. Install the scripts and run RegisterScripts.lua once in REAPER (Lua Scripts card).", "Select", select)
                        .Help("Tutorials/7-ControlReaper.md#play-regions-in-loop");
                }

                if (string.IsNullOrWhiteSpace(controller.SetupTracksScriptId))
                {
                    yield return new AsioSetupCheck("REAPER tracks can be created from Unity", AsioCheckStatus.Info,
                        "Install the scripts and run RegisterScripts.lua once in REAPER (Lua Scripts card) to create one REAPER track per source with Set Up REAPER Tracks.", "Select", select)
                        .Help("Tutorials/7-ControlReaper.md#create-reapers-tracks-from-unity");
                }

                if (controller.FollowReaper)
                {
                    if (!EditorApplication.isPlaying)
                        yield return new AsioSetupCheck("Follow REAPER: listening on port " + controller.ListenPort, AsioCheckStatus.Unknown, "Checked in Play mode.", "Select", select).Help(TwoWaySync);
                    else if (!controller.IsListening)
                        yield return new AsioSetupCheck("Follow REAPER: can't listen on port " + controller.ListenPort, AsioCheckStatus.Error,
                            controller.ListenError ?? "The port couldn't be opened: choose another one, in Unity and in REAPER.", "Select", select).Help(TwoWaySync);
                    else if (!controller.HasHeardFromReaper)
                        yield return new AsioSetupCheck("Follow REAPER: no message from REAPER yet", AsioCheckStatus.Warning,
                            "In REAPER's OSC control surface: Mode \"Configure device IP + local port\", Device IP: this computer, Device port: " + controller.ListenPort
                            + ". REAPER sends its state when its transport changes: press Play in REAPER to test.", "Select", select).Help(TwoWaySync);
                    else
                        yield return new AsioSetupCheck("Follow REAPER: REAPER answers", AsioCheckStatus.Ok, null);
                }
            }
        }
    }
}
