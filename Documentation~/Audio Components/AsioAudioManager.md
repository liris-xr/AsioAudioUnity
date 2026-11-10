## ASIO Audio Settings and AsioAudioManager

Since version 3.0, there is **no manager to add to the scene**: the ASIO settings are project settings, and the manager that plays the sources is created automatically.

### Project Settings > ASIO Audio

Open *Edit > Project Settings > ASIO Audio* (or *AsioAudioUnity > Project Settings…*). The first time, click **Create ASIO Audio Settings**: an asset is created at `Assets/Settings/ASIO Audio Settings.asset`, used by every scene, and included in builds (it is added to the Player Settings' *Preloaded Assets*).

| **Section** | **Settings** |
|-|-|
| Status | In Play mode: connection status, number of outputs and sources, *Reconnect*, and *Driver Control Panel* (the driver's own settings: buffer size, clock...). In Edit mode: *Driver Control Panel* too: the driver is loaded for it, which also reads its number of outputs (*Driver Outputs*), channel names and sample format (*Driver Format*), reloaded on each click, and released with *Release Driver*, when leaving the page, entering Play mode or starting a preview. While it is loaded, other applications may not be able to use an exclusive driver. |
| ASIO Driver | **Driver** (from the ASIO drivers installed on the machine), **Sample Rate** (must be supported by the driver; audio files are resampled to it), **Sample Format** (16-bit integer or 32-bit float), and the **Driver Format** the driver actually works with (e.g. 24-bit integer, remembered from the last connection). Changing them in Play mode reconnects the driver. |
| Master | **Master Volume**, in dB, applied to all sources after their own volume, with the level sent to the driver in Play mode. |
| Snapshots | Saved mixes: the master volume, and the volume, mute and solo of every source, **identified by output channel** (so a snapshot works in every scene). *Save Current Mix*, *Recall*, *Update*, delete, and rename. |
| In-game Overlay | **Show**, **Toggle Key** (F9 by default), **Scale**. |
| Output Channels | The sources of the open scene with their channel, status and file, channel conflicts (with *Fix*), and *Open Mixer*. |

> Changes made at runtime (from the overlay, the ASIO Audio Mixer, code or OSC) are made to the settings asset, so in the Editor they are kept after Play mode, like any asset edited in Play mode.

### The ASIO Audio Manager

When the first [AsioAudioSource](/Documentation~/Audio%20Components/AsioAudioSource.md) is enabled (in Play mode or in a build), the **ASIO Audio Manager** is created (`AsioAudioManager.Instance`), connects to the driver of the settings, and continuously mixes all sources, each on its own output channel. It is kept across scene loads, so the driver stays connected.

In Play mode, it appears in the *DontDestroyOnLoad* scene of the Hierarchy: its Inspector shows the connection status, with *Reconnect*, *Open Mixer* and *Settings*.

#### Migrating a scene from before version 3.0

Scenes made before version 3.0 contain an `AsioAudioManager` component. Open *Project Settings > ASIO Audio* and click **Migrate the Scene's ASIO Audio Manager** (or use the button in the component's Inspector): its settings (driver, sample rate, format, master volume, overlay, snapshots) are copied to the ASIO Audio Settings, and the component is removed from the scene. Save the scene afterwards.

Until a scene is migrated, it keeps working: if the project has no settings asset yet, the scene's manager uses its own settings (with a warning).

#### Public Properties

| **Property** | **Description** |
|-|-|
| `AsioAudioManager.Instance` | The manager (static). In Play mode or in a build, it is created if needed. Null in Edit mode. |
| `AsioAudioManager.Existing` | The manager if it exists, without creating it (static). |
| `Settings` | The ASIO Audio Settings in use (`AsioAudioSettings.Instance`). |
| `AsioDriverName`, `TargetSampleRate`, `TargetBitsPerSample` | The driver, sample rate and sample format (from the settings). Setting them reconnects the driver. |
| `MasterVolume`, `MasterVolumeDecibels` | The master volume, linear (0 to 1) or in dB (from the settings). Applied live and smoothly. |
| `MasterMeterLevel` | The level sent to the driver (peak of all channels, after the master volume), with a meter-like falloff (Read Only). |
| `IsConnected` | Whether the ASIO driver is connected and playing (Read Only). |
| `AsioDriverOutputChannelCount`, `AsioDriverInputChannelCount` | The driver's number of outputs (the highest valid `TargetOutputChannel`) and inputs (Read Only). |
| `AsioDriverSampleFormat` | The sample format the driver works with, e.g. `24-bit integer` (Read Only, empty until connected). |
| `AsioDriverOutputChannelNames`, `GetOutputChannelName(channel)` | The driver's names for its output channels, e.g. `ReaRoute 3` (Read Only). |
| `AsioAudioSources` | The sources playing through the manager (Read Only). |
| `Snapshots` | The saved snapshots (from the settings). |
| `DisplayInfoOnGameWindow`, `OverlayToggleKey`, `OverlayScale` | The in-game overlay settings (from the settings). |

#### Public Methods

| **Method** | **Description** |
|-|-|
| `PlayAll`, `PauseAll`, `StopAll`, `RestartAll` | Transport of all sources, **on the same sample** (stems stay in sync). Also on the *Master* strip of the ASIO Audio Mixer, and over OSC. |
| `SaveSnapshot(name)` | Saves the mix of the loaded scenes as a snapshot (a snapshot with the same name is replaced). |
| `RecallSnapshot(name)` / `RecallSnapshot(index)` | Applies a snapshot (volume changes are smoothed while playing). Returns `false` if it doesn't exist. |
| `DeleteSnapshot(name)` | Deletes a snapshot. |
| `Reconnect` | Disconnects from the driver and connects again. Sources keep their state and position. |
| `RequestAddAsioAudioSource`, `RequestRemoveAsioAudioSource` | Used by the sources to register (you don't need to call them). |

#### Events

| **Event** | **Description** |
|-|-|
| `AsioAudioManager.TransportCommandIssued` | Static: called after `PlayAll`, `PauseAll`, `StopAll` and `RestartAll` (used by the REAPER Controller to follow Unity). |
| `OnAsioDriverNameChanged`, `OnTargetSampleRateChanged`, `OnTargetBitsPerSampleChanged` | Called when these settings are changed through the manager. |

### Script Example

```cs
using UnityEngine;
using AsioAudioUnity;

public class ShowControl : MonoBehaviour
{
    public void StartShow()
    {
        AsioAudioManager manager = AsioAudioManager.Instance;
        manager.RecallSnapshot("Intro");
        manager.MasterVolumeDecibels = -6f;
        manager.RestartAll();   // all sources from the start, in sync
    }
}
```
