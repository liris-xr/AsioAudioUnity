## ReaperController

> Part of the **REAPER OSC plugin** (`Modules/ReaperOsc`, assembly `AsioAudioUnity.ReaperOsc`), which uses the shared OSC library (`Runtime/Osc`).

The `ReaperController` component controls REAPER from Unity with OSC messages:

- it **follows Unity's transport**: when the scene starts with sources playing (*Play On Enable*), REAPER plays from the start of the project; when the [AsioAudioManager](/Documentation~/Audio%20Components/AsioAudioManager.md)'s `PlayAll` is called, REAPER plays; `PauseAll` pauses it, `StopAll` stops it, and `RestartAll` plays it from the start of the project,
- it sends **commands**: play, stop, go to a marker or a region, play a region in loop, run any REAPER action or Lua script (with a value if needed), or any OSC message,
- commands are sent **automatically on Unity events** (scene start, `PlayAll`..., a given source playing, pausing or stopping), **from your scripts or UnityEvents** (`Trigger("name")`), or with the Inspector's **Send** button (also in Edit mode, to test).

It sends directly to REAPER's address and port: no OSC Manager is needed, and it works alongside the [Spat Controller](/Documentation~/Spat%20OSC/SpatController.md) (each sends to its own application and port).

### Setup

#### In REAPER

1. *Options > Preferences > Control/OSC/web > Add*, and choose **OSC (Open Sound Control)**.
2. Mode: *Local port* (or *Configure device IP + local port*). **Local listen port: 8000** (the default of the REAPER Controller). Pattern config: **Default**.
3. Click OK. REAPER now listens to OSC on port 8000.

To play regions in loop, also load the PlayRegion.lua script (see [Lua scripts](#lua-scripts)).

#### In Unity

1. *GameObject > AsioAudioUnity > REAPER Controller* (or add the component to any GameObject).
2. Check the **Address** (127.0.0.1 if REAPER runs on the same computer) and the **Port** (REAPER's local listen port).
3. Keep **Follow Transport** ticked to mirror `PlayAll` / `PauseAll` / `StopAll` / `RestartAll` in REAPER.
4. Add commands if needed (see below), and use **Send** to test them.

### Commands

Each command has:

| **Field** | **Description** |
|-|-|
| Name | Used to send the command from code or a UnityEvent: `Trigger("name")`. |
| When | `Manual` (only when called), `SceneStart`, `PlayAll`, `PauseAll`, `StopAll`, `RestartAll`, or `SourcePlay` / `SourcePause` / `SourceStop` (with a **Source**: an [AsioAudioSource](/Documentation~/Audio%20Components/AsioAudioSource.md)). |
| Do | What REAPER does (see below). |

| **Do** | **Settings** | **OSC message sent** |
|-|-|-|
| Play / Pause / Stop / Record | | `/play`, `/pause`, `/stop`, `/record` |
| Go To Start | | `/time 0` |
| Go To Time | Time (s) | `/time [seconds]` |
| Go To Marker | Marker Number | `/marker [number]` |
| Go To Region | Region Number | `/region [number]` |
| Play Region | Region Number | `/action/[PlayRegion.lua ID] "[number]"`: plays the region in loop (without the script: `/region [number]` then `/play`) |
| Run Action | Command ID | `/action [id]` for a numeric ID (e.g. `40042`), `/action/str "[id]"` for a named ID (e.g. `_RS7d3c...`, `_SWS_...`) |
| Run Action With Value | Command ID, Value | `/action/[id] "[value]"`: the script reads the value with `reaper.get_action_context()` |
| Custom Message | OSC Address, Value | `[address] [value]`, e.g. `/track/1/mute 1` |

A command ID is found in REAPER's action list (*Actions > Show action list*): right-click an action > *Copy selected action command ID*.

The addresses are those of REAPER's *Default* OSC pattern configuration. If your control surface uses another pattern file, change them in the **OSC Addresses** foldout.

### Lua scripts

The REAPER OSC plugin comes with **PlayRegion.lua** (`Modules/ReaperOsc/Lua Scripts`), used by *Play Region* commands: it receives the region number from the OSC message, sets the time selection to the region, turns repeat on and plays it.

1. In the REAPER Controller's Inspector, click **Install into REAPER**: the scripts are copied to `%APPDATA%\REAPER\Scripts\AsioAudioUnity` (or **Show Scripts** to copy them yourself, e.g. for a portable REAPER).
2. In REAPER: *Actions > Show action list > New action > Load ReaScript...*, and select `PlayRegion.lua`.
3. Right-click it in the action list > *Copy selected action command ID*, and paste it in **Play Region Script ID**.

Your own scripts work the same way, with *Run Action* (no value) or *Run Action With Value* commands. A script reads the value sent with it from its action context:

```lua
local _, _, _, _, _, _, _, context = reaper.get_action_context()
local value = context:match(":s=([^%]]+)")  -- the value sent by Unity, as a string
```

### Script Example

```cs
using UnityEngine;
using AsioAudioUnity;

public class ReaperCues : MonoBehaviour
{
    [SerializeField] private ReaperController _reaper;

    public void OnVisitorEntersRoom()
    {
        _reaper.PlayRegion(2);          // plays region 2 in loop (with PlayRegion.lua)
    }

    public void OnShowEnds()
    {
        _reaper.Trigger("Outro");       // a command named "Outro" in the Inspector
        _reaper.RunAction(40042);       // Transport: Go to start of project
    }
}
```

`Trigger(string)` can also be called directly from a UnityEvent in the Inspector (e.g. a source's `OnPlay`).

### Public Properties

| **Property** | **Description** |
|-|-|
| `ReaperAddress` | IP address of the computer running REAPER (default: 127.0.0.1). |
| `ReaperPort` | The local listen port of REAPER's OSC control surface (default: 8000). |
| `FollowTransport` | Mirrors `PlayAll` / `PauseAll` / `StopAll` / `RestartAll` in REAPER, and plays REAPER from the start of the project when the scene starts with sources playing (default: on). Sources played one by one (`AsioAudioSource.Play`) are not mirrored: use *SourcePlay* commands for that. |
| `StopReaperOnQuit` | Stops REAPER when the application quits or Play mode ends (default: on). Commands triggered by the sources stopping while quitting are not sent. |
| `PlayRegionScriptId` | The command ID of PlayRegion.lua in REAPER (e.g. `_RS7d3c...`). |
| `Commands` | The list of commands (`ReaperCommand`). |
| `Addresses` | The OSC addresses REAPER answers to (`ReaperOscAddresses`). |
| `LogMessages` | Logs every message sent. |

### Public Methods

| **Method** | **Description** |
|-|-|
| `Play`, `Pause`, `Stop`, `Record` | REAPER's transport. |
| `GoToStart`, `GoToTime(seconds)` | Moves the edit cursor (and playback, if playing). |
| `GoToMarker(number)`, `GoToRegion(number)` | Goes to a marker, or to the start of a region. |
| `PlayRegion(number)` | Plays a region in loop (with PlayRegion.lua). |
| `RunAction(id)` | Runs an action or script, by numeric or named command ID. |
| `RunActionWithValue(id, value)` | Runs an action or script with a value. |
| `Trigger(name)` / `Trigger(index)` | Sends a command of the list. |
| `Execute(command)` | Sends a `ReaperCommand`. |
| `Send(address, values...)` | Sends any OSC message to REAPER (int, float or string values). |
