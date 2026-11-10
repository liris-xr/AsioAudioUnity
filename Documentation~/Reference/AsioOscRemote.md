# AsioOscRemote and OSC Manager

[Documentation home](/Documentation~/README.md) · Tutorial: [8. Control Unity over OSC](/Documentation~/Tutorials/8-ControlUnityOverOsc.md)

> Part of the shared OSC library (`Runtime/Osc`, assembly `AsioAudioUnity.Osc`). To control REAPER *from* Unity, see the [REAPER Controller](/Documentation~/Reference/ReaperController.md); to send positions to Spat Revolution, the [Spat Controller](/Documentation~/Reference/SpatController.md). Neither needs an OSC Manager.

The **ASIO OSC Remote** lets another application control the ASIO Audio Sources and the [ASIO Audio Manager](/Documentation~/Reference/AsioAudioManager.md) with OSC messages: TouchOSC, REAPER, Spat Revolution, Max, Pure Data, a lighting desk… It handles the messages received by the scene's **OSC Manager**.

## OSC Manager

The OSC Manager (`OSC` component) receives OSC messages on its **In Port**, and can send messages to its **Out IP** / **Out Port**. Menu: *GameObject > AsioAudioUnity > OSC Manager*, or the `OSC Manager` prefab (`Runtime/Osc/Prefabs`).

Scripts can listen to the received messages with its `MessageReceived` event (called on the main thread; several components can listen at the same time):

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
        Debug.Log("Received " + message);
    }
}
```

## ASIO OSC Remote

Menu: *GameObject > AsioAudioUnity > ASIO OSC Remote*, or *Add Component > AsioAudioUnity > ASIO OSC Remote* on any GameObject. Its **Osc** field is found in the scene when left empty.

### Messages

Sources are addressed by their **output channel**, which is unique and matches the ReaRoute input / REAPER track they arrive on. With the default prefix `/asio`:

| **Address** | **Value** | **Effect** |
|-|-|-|
| `/asio/play` | | Plays (or resumes) all sources, on the same sample. |
| `/asio/pause` | | Pauses all sources. |
| `/asio/stop` | | Stops all sources. |
| `/asio/restart` | | Plays all sources from the start, together. |
| `/asio/master/volume` | dB (float) | Sets the master volume, e.g. `-6`. |
| `/asio/snapshot` | name (string) or index (int) | Recalls a mixer snapshot. |
| `/asio/source/3/play` | | Plays the source on output channel 3 (also `pause`, `stop`, `restart`). |
| `/asio/source/3/volume` | dB (float) | Sets its volume, e.g. `-12`. `-inf` (as a string) is silence. |
| `/asio/source/3/mute` | 0 or 1 | Mutes or unmutes it. |
| `/asio/source/3/solo` | 0 or 1 | Solos it or not. |
| `/asio/source/3/seek` | seconds (float) | Moves its playback position. |

Numbers can be sent as int, float or string. Messages starting with the prefix that aren't understood (unknown address, or no source on that channel) are logged in the Console.

### Properties

| **Property** | **Description** |
|-|-|
| `Osc` | The OSC Manager receiving the messages (found in the scene if empty). |
| `AddressPrefix` | The start of the addresses this component answers to (default: `/asio`). |
| `LogMessages` | Logs every message handled. |
