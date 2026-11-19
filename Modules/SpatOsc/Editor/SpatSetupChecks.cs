using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>The Spat OSC plugin's checks in the Setup Check window (AsioAudioUnity > Setup Check).</summary>
    [InitializeOnLoad]
    internal static class SpatSetupChecks
    {
        static SpatSetupChecks()
        {
            AsioSetupChecks.Register("Spat Revolution", 30, Checks);
            AsioSetupChecks.RegisterSceneObject("Spat Controller", 30,
                () => Object.FindObjectsByType<SpatController>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0,
                () =>
                {
                    GameObject go = GlobalAsioAudioEditor.CreateGameObjectInScene("Spat Controller", new MenuCommand(null));
                    Undo.AddComponent<SpatController>(go);
                    return go;
                });
        }

        private static IEnumerable<AsioSetupCheck> Checks()
        {
            SpatController[] controllers = Object.FindObjectsByType<SpatController>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            if (controllers.Length == 0)
            {
                yield return new AsioSetupCheck("No Spat Controller (optional)", AsioCheckStatus.Info,
                    "Add one to send the positions of the sources and rooms to Spat Revolution.", "Create",
                    () => EditorApplication.ExecuteMenuItem("GameObject/AsioAudioUnity/Spat Controller")).Help("Tutorials/6-SendPositionsToSpat.md");
            }

            foreach (SpatController controller in controllers)
            {
                System.Action select = () => Selection.activeObject = controller.gameObject;
                controller.RefreshSources();
                List<SpatController.TrackedPosition> tracked = controller.GetTrackedPositions();
                int sources = tracked.Count(item => item.Type == SpatObjectType.Source), rooms = tracked.Count(item => item.Type == SpatObjectType.Room);
                yield return new AsioSetupCheck("Spat Controller \"" + controller.name + "\": sends to " + controller.SpatAddress + ":" + controller.SpatPort,
                    controller.isActiveAndEnabled ? AsioCheckStatus.Ok : AsioCheckStatus.Warning,
                    (controller.isActiveAndEnabled ? "" : "It is disabled. ") + sources + " source(s), " + rooms + " room(s). Spat Revolution needs an OSC input connection on port "
                    + controller.SpatPort + ". It doesn't answer: click Send Now, and watch its Room tab.", "Send Now", () => controller.SendAllPositions())
                    .Help("Tutorials/Troubleshooting.md#spat-revolution-doesnt-move-the-sources");

                if (controller.ListenToSpat)
                {
                    const string listenHelp = "Reference/SpatController.md#listening-to-spat-revolution";
                    if (!controller.IsListening)
                        yield return new AsioSetupCheck("Listen To Spat: can't listen on port " + controller.ListenPort, AsioCheckStatus.Error,
                            controller.ListenError ?? (controller.isActiveAndEnabled ? "The port couldn't be opened: choose another one, in Unity and in Spat Revolution." : "The Spat Controller is disabled."),
                            "Select", select).Help(listenHelp);
                    else if (!controller.HasHeardFromSpat)
                        yield return new AsioSetupCheck("Listen To Spat: no message from Spat Revolution yet", AsioCheckStatus.Warning,
                            "In Spat Revolution: Preferences > OSC Main: add an OSC output connection to this computer, port " + controller.ListenPort
                            + ". Then move a source in its Room tab.", "Select", select).Help(listenHelp);
                    else
                        yield return new AsioSetupCheck("Listen To Spat: Spat Revolution answers", AsioCheckStatus.Ok,
                            controller.ReceivedMessageCount + " message(s), " + controller.ReceivedAddresses.Count + " address(es) received (listed in the Spat Controller)."
                            + (controller.FollowSpat ? " Follow Spat: " + controller.AppliedMoveCount + " move(s) applied." : ""));
                    if (controller.IsListening && controller.AzimuthLooksInverted && !controller.InvertAzimuth)
                        yield return new AsioSetupCheck("Spat Revolution's azimuth looks inverted", AsioCheckStatus.Warning,
                            "Its echoes only match Unity's positions with the azimuth's sign inverted: positions moved in Spat would be mirrored in Unity.", "Invert Azimuth", () =>
                            {
                                Undo.RecordObject(controller, "Invert Azimuth");
                                controller.InvertAzimuth = true;
                                controller.ClearReceivedMessages();
                                EditorUtility.SetDirty(controller);
                            }).Help(listenHelp);
                }

                var duplicates = tracked.GroupBy(item => (item.Type, item.Index)).Where(group => group.Count() > 1).ToList();
                if (duplicates.Count > 0)
                {
                    yield return new AsioSetupCheck("Several objects use the same Spat number", AsioCheckStatus.Warning,
                        string.Join("; ", duplicates.Select(group => group.Key.Type + " " + group.Key.Index + ": " + string.Join(", ", group.Select(item => item.Name)))), "Select", select).Help("Reference/SpatController.md");
                }
            }

            // Version 4.0 removed the Source / Room Position OSC components: scenes that still had them show missing scripts.
            GameObject[] missing = FindObjectsWithMissingScripts();
            if (missing.Length > 0)
            {
                yield return new AsioSetupCheck(missing.Length + " object(s) with a missing script", AsioCheckStatus.Warning,
                    "Possibly Source / Room Position OSC components, removed in version 4.0 (the Spat Controller replaces them): "
                    + string.Join(", ", missing.Take(8).Select(go => "\"" + go.name + "\"")) + (missing.Length > 8 ? "…" : "") + ".", "Select",
                    () => Selection.objects = missing.Select(go => (Object)go).ToArray()).Help("Tutorials/Troubleshooting.md#upgrading-to-version-40");
            }
        }

        private static GameObject[] FindObjectsWithMissingScripts()
        {
            List<GameObject> found = new List<GameObject>();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject rootObject in scene.GetRootGameObjects())
                {
                    foreach (Transform transform in rootObject.GetComponentsInChildren<Transform>(true))
                    {
                        if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0) found.Add(transform.gameObject);
                    }
                }
            }
            return found.ToArray();
        }
    }
}
