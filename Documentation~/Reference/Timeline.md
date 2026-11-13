# Timeline tracks

[Documentation home](/Documentation~/README.md)

> Requires Unity's **Timeline** package (installed by default in most projects). The tracks are in two optional assemblies, compiled only when Timeline is installed: `AsioAudioUnity.Timeline` (`Modules/Timeline`) and `AsioAudioUnity.ReaperOsc.Timeline` (`Modules/ReaperOsc/Timeline`).

With these tracks, the cues of a show can be laid out on a Timeline: when each ASIO Audio Source plays, and when REAPER is sent a command.

## ASIO Audio track

1. In the Timeline window, **+** > **AsioAudioUnity > ASIO Audio Track** (or *Asio Audio Track*), and bind it to an **ASIO Audio Source**.
2. Right-click the track > **Add ASIO Audio Clip**, and place and trim the clip.

During a clip, the source plays: it starts at the clip's start, from the clip's **Clip In** time in its file (to start further in the file), and stops at the clip's end.

- Clips of several tracks that start on the same frame start **on the same sample** (through `AsioAudioManager.PlayInSync`).
- Starts and stops happen on the Timeline's frames; during a clip, the audio runs on the ASIO driver's clock. When the Timeline jumps (scrubbing in Play mode, a jump in time), the source follows.
- Pausing the Timeline pauses the source, and resuming resumes it. Stopping the Timeline stops it.
- The source's own settings still apply: volume, fades, loop (a clip longer than the file continues if the source loops).
- It works in Play mode and in builds: in Edit mode, scrubbing the Timeline doesn't play the sources.

## REAPER Command markers

1. **+** > **AsioAudioUnity > REAPER Command Track**, and bind it to the [REAPER Controller](/Documentation~/Reference/ReaperController.md).
2. Right-click the track's marker area > **Add REAPER Command**, and place the marker.
3. In the marker's Inspector, either enter the **Command Name** of a command of the REAPER Controller's list, or leave it empty and set up the marker's own **Command** (play a region, go to a marker, run an action…; its *When* is ignored).

When the Timeline reaches the marker, the command is sent to REAPER. **Retroactive**: also send it when the Timeline starts after the marker (e.g. when jumping past it).

## From code

| **Type** | **Description** |
|-|-|
| `AsioAudioTrack`, `AsioAudioClip` | The ASIO Audio track and its clips (`ClipCaps.ClipIn`). |
| `ReaperCommandTrack`, `ReaperCommandMarker` | The REAPER Command track and its markers (`CommandName`, `Command`). |
| `IReaperCommandNotification` | Implemented by the markers; the REAPER Controller sends the command of any notification implementing it. |
