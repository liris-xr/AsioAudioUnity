## AsioOscRemote

The `AsioOscRemote` component lets another application control the [AsioAudioManager](/Documentation~/Audio%20Components/AsioAudioManager.md) and its [AsioAudioSource](/Documentation~/Audio%20Components/AsioAudioSource.md) components with OSC messages: REAPER, Spat Revolution, TouchOSC, Max, Pure Data, a lighting desk...

It listens to the messages received by the OSC Manager (the `OSC` component), on its `InPort`.

### Setup

1. Make sure the scene has an OSC Manager (*GameObject > AsioAudioUnity > OSC Manager*), and note its `InPort` (8101 in the demo scene).
2. Add the component to any GameObject: *Add Component > AsioAudioUnity > ASIO OSC Remote* (for example on the ASIO Audio Manager).
3. Send the messages below to the computer running Unity, on the OSC Manager's `InPort`.

`Osc` and `AsioAudioManager` are found in the scene when left empty.

### Messages

Sources are addressed by their **output channel** (`TargetOutputChannel`), which is unique, and matches the ReaRoute input / REAPER track they arrive on.

With the default prefix `/asio`:

| **Address** | **Value** | **Effect** |
|-|-|-|
| `/asio/play` | | Plays (or resumes) all sources, starting on the same sample. |
| `/asio/pause` | | Pauses all sources. |
| `/asio/stop` | | Stops all sources. |
| `/asio/restart` | | Plays all sources from the start, together. |
| `/asio/master/volume` | dB (float) | Sets the master volume, e.g. `-6`. |
| `/asio/snapshot` | name (string) or index (int) | Recalls a mixer snapshot (see [AsioAudioManager](/Documentation~/Audio%20Components/AsioAudioManager.md)). |
| `/asio/source/3/play` | | Plays the source on output channel 3 (also `pause`, `stop`, `restart`). |
| `/asio/source/3/volume` | dB (float) | Sets its volume, e.g. `-12`. `-inf` (as a string) is silence. |
| `/asio/source/3/mute` | 0 or 1 | Mutes or unmutes it. |
| `/asio/source/3/solo` | 0 or 1 | Solos it or not. |
| `/asio/source/3/seek` | seconds (float) | Moves its playback position. |

Numbers can be sent as int, float or string. Messages starting with the prefix that are not understood (unknown address, or no source on that channel) are logged in the console.

### Public Properties

| **Property** | **Description** |
|-|-|
| `Osc` | The OSC Manager receiving the messages (found in the scene if empty). |
| `AsioAudioManager` | The ASIO Audio Manager to control (found in the scene if empty). |
| `AddressPrefix` | The start of the addresses this component answers to (default: `/asio`). |
| `LogMessages` | Logs every message handled. |

### Script Example

Messages can also be listened to from your own scripts, through the OSC Manager's `MessageReceived` event (several components can listen at the same time, unlike `SetAllMessageHandler`):

```cs
using UnityEngine;
using AsioAudioUnity;

public class LogOscMessages : MonoBehaviour
{
    [SerializeField] private OSC _osc;

    private void OnEnable() { _osc.MessageReceived += OnMessage; }
    private void OnDisable() { _osc.MessageReceived -= OnMessage; }

    private void OnMessage(OscMessage message)
    {
        Debug.Log("Received " + message); // Called on the main thread
    }
}
```
