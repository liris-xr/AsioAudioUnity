using System;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>What a REAPER command does.</summary>
    public enum ReaperCommandType
    {
        /// <summary>Starts REAPER's playback.</summary>
        Play,
        /// <summary>Pauses REAPER's playback.</summary>
        Pause,
        /// <summary>Stops REAPER's playback.</summary>
        Stop,
        /// <summary>Starts recording (armed tracks).</summary>
        Record,
        /// <summary>Moves the edit cursor to the start of the project.</summary>
        GoToStart,
        /// <summary>Moves the edit cursor to a time, in seconds (Number).</summary>
        GoToTime,
        /// <summary>Moves the edit cursor to a marker, by its number (Number).</summary>
        GoToMarker,
        /// <summary>Moves the edit cursor to the start of a region, by its number (Number).</summary>
        GoToRegion,
        /// <summary>
        /// Plays a region (Number) in loop, with the PlayRegion.lua script (its command ID is set on the REAPER Controller), which receives the region number.
        /// Without the script, goes to the region and plays from there (no loop).
        /// </summary>
        PlayRegion,
        /// <summary>Runs a REAPER action or script, by its command ID (Text): a number ("40042") or a named ID ("_RS7d3c..." for scripts, "_SWS_..." for extensions).</summary>
        RunAction,
        /// <summary>
        /// Runs a REAPER action or script (Text) with a value (Number), sent as its OSC argument: a Lua script reads it with reaper.get_action_context() (":s=value").
        /// </summary>
        RunActionWithValue,
        /// <summary>Sends any OSC message: the address is Text, the value is Number.</summary>
        CustomMessage
    }

    /// <summary>When a REAPER command is sent automatically.</summary>
    public enum ReaperCommandTrigger
    {
        /// <summary>Only when called (ReaperController.Trigger, a UnityEvent, the Inspector's Send button).</summary>
        Manual,
        /// <summary>When the scene starts (the REAPER Controller's Start).</summary>
        SceneStart,
        /// <summary>When AsioAudioManager.PlayAll is called (ASIO Audio Mixer's Master strip, overlay, OSC remote...).</summary>
        PlayAll,
        /// <summary>When AsioAudioManager.PauseAll is called.</summary>
        PauseAll,
        /// <summary>When AsioAudioManager.StopAll is called.</summary>
        StopAll,
        /// <summary>When AsioAudioManager.RestartAll is called.</summary>
        RestartAll,
        /// <summary>When a given source (Source) starts playing.</summary>
        SourcePlay,
        /// <summary>When a given source (Source) is paused.</summary>
        SourcePause,
        /// <summary>When a given source (Source) stops (Stop, or the end of a non-looping file).</summary>
        SourceStop
    }

    /// <summary>A command sent to REAPER over OSC, by name, from code or a UnityEvent, or automatically on a Unity event (Trigger).</summary>
    [Serializable]
    public class ReaperCommand
    {
        [Tooltip("Name used to send the command from code or a UnityEvent (ReaperController.Trigger(\"name\")).")]
        public string Name = "Command";

        [Tooltip("When the command is sent automatically.")]
        public ReaperCommandTrigger Trigger = ReaperCommandTrigger.Manual;

        [Tooltip("The source whose events trigger the command (for the Source triggers).")]
        public AsioAudioSource Source;

        [Tooltip("What the command does in REAPER.")]
        public ReaperCommandType Type = ReaperCommandType.Play;

        [Tooltip("Time in seconds (Go To Time), marker or region number (Go To Marker / Region, Play Region), or value (Custom Message).")]
        public float Number;

        [Tooltip("Action command ID (Run Action), e.g. 40042 or _RS7d3c..., or OSC address (Custom Message).")]
        public string Text = "";

        public bool UsesSource => Trigger == ReaperCommandTrigger.SourcePlay || Trigger == ReaperCommandTrigger.SourcePause || Trigger == ReaperCommandTrigger.SourceStop;
    }

    /// <summary>
    /// The OSC addresses REAPER answers to. The defaults are those of REAPER's "Default" OSC pattern configuration (Default.ReaperOSC);
    /// change them if your REAPER control surface uses another pattern configuration.
    /// </summary>
    [Serializable]
    public class ReaperOscAddresses
    {
        public string Play = "/play";
        public string Pause = "/pause";
        public string Stop = "/stop";
        public string Record = "/record";
        [Tooltip("Sets the edit cursor position, in seconds (float).")]
        public string Time = "/time";
        [Tooltip("Goes to a marker, by its number (int).")]
        public string Marker = "/marker";
        [Tooltip("Goes to a region, by its number (int).")]
        public string Region = "/region";
        [Tooltip("Runs an action by its numeric command ID (int).")]
        public string Action = "/action";
        [Tooltip("Runs an action or script by its named command ID (string), e.g. _RS7d3c...")]
        public string NamedAction = "/action/str";
        [Tooltip("Runs an action or script with a value (Play Region, Run Action With Value): {0} is replaced by the command ID, the value is sent as a string. A Lua script reads it in reaper.get_action_context() (\":s=value\").")]
        public string ActionWithValue = "/action/{0}";
    }
}
