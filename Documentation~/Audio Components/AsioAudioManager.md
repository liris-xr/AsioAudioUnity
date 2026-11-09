## AsioAudioManager

The AsioAudioManager compenent is designed to manage audio playback using the NAudio library and an ASIO driver. It acts as a central controller for multiple [AsioAudioSource](/Documentation~/Audio%20Components/AsioAudioSource.md) components, handling ASIO driver initialization, audio source validation, and playback coordination.

### Typical Workflow and Features

1.	Driver Connection: The Asio Audio Manager connects to the ASIO driver set in `AsioDriverName` when it is enabled, and stays connected until it is disabled, using all the driver's output channels (`AsioDriverOutputChannelCount`). Connection errors (driver not found, unsupported sample rate...) are logged in the console, along with the list of available drivers. Changing `AsioDriverName`, `TargetSampleRate` or `TargetBitsPerSample` at runtime (from code, the Inspector or the Game window overlay) reconnects the driver; sources keep their state and position.
2.	Audio Source Coordination:
    •	Audio Source Management: Validates, adds, and removes [AsioAudioSource](/Documentation~/Audio%20Components/AsioAudioSource.md) components.
    •	Real-time Mixing: Continuously mixes all registered sources (each one on its own output channel) into the stream sent to the ASIO driver. Sources can be played, paused, stopped, or have their volume, loop or channel changed without interrupting the driver.
3.	Inspector: The driver is picked from the list of ASIO drivers installed on the machine, and the sample rate and format from dropdowns. In Play mode, the Inspector shows the connection status, with buttons to reconnect and to open the driver's own control panel. The *Output Channels* section lists every source of the scene with its channel, status and file, highlights channel conflicts (with a *Fix* button that assigns free channels), and opens the ASIO Mixer window.
4.	Synchronised transport: `PlayAll`, `PauseAll`, `StopAll` and `RestartAll` change the state of all sources **on the same sample**, so files that belong together (e.g. the stems of a multichannel mix) stay perfectly in sync. They are also on the *Master* strip of the ASIO Mixer, and available over OSC (see [AsioOscRemote](/Documentation~/OSC%20Components/AsioOscRemote.md)).
5.	Snapshots: the *Snapshots* section of the Inspector (and the *Snapshots* menu of the ASIO Mixer) saves the current mix (master volume, and the volume, mute and solo of every source) and recalls it later. Snapshots are stored in the scene, with the manager, and can be recalled from code (`RecallSnapshot`) or over OSC.
6.	In-game Overlay: A draggable window in the Game view (and in builds), shown or hidden with a key (`OverlayToggleKey`, F9 by default). It shows the connection status, lets you change the driver, sample rate and format (*Settings*), and lists every source with play / pause / stop, mute / solo, volume, a level meter and a position bar (click it to seek). `OverlayScale` makes it bigger on high-resolution screens.

#### Public Properties
| **Property** | **Description** |
|-|-|
| `AsioDriverName` | The name of the ASIO driver to connect to. Changing it at runtime reconnects. |
| `TargetSampleRate` | The sample rate the ASIO driver runs at (default: 48000 Hz). Audio files are resampled to it on the fly. Changing it at runtime reconnects. |
| `TargetBitsPerSample` | The sample format the mix is handed to NAudio in (16-bit integer or 32-bit float). NAudio then converts it to the driver's own format (`AsioDriverSampleFormat`), so a 24-bit driver gets full 24-bit samples from 32-bit float. 16-bit only works with 16-bit and 32-bit integer drivers; with other drivers, 32-bit float is used instead (with a warning). Changing it at runtime reconnects. |
| `AsioDriverSampleFormat` | The sample format the ASIO driver actually works with, e.g. `24-bit integer`, set by the driver and its control panel (Read Only, empty until connected). The Inspector shows it as *Driver Format*, remembered from the last connection so it is visible outside Play mode. |
| `MasterVolume` | Volume applied to all sources after their own volume, like the master fader of a mixing desk (linear gain, 0 to 1; default: 1). Applied live and smoothly. Shown in dB in the Inspector, as the *Master* strip of the ASIO Mixer, and in the overlay. |
| `MasterVolumeDecibels` | The master volume in decibels (0 dB: unchanged; negative infinity: silence). |
| `MasterMeterLevel` | The level sent to the ASIO driver (peak of all channels, 0 to 1, after the master volume), with a meter-like falloff (Read Only). |
| `IsConnected` | Whether the ASIO driver is connected and playing (Read Only). |
| `AsioDriverOutputChannelCount` | The number of output channels of the ASIO driver, i.e. the highest valid `TargetOutputChannel` (Read Only). |
| `AsioDriverInputChannelCount` | The number of input channels of the ASIO driver (Read Only). |
| `AsioDriverOutputChannelNames` | The driver's names for its output channels (e.g. `ReaRoute 3`), index 0 being channel 1 (Read Only, empty until connected). `GetOutputChannelName(channel)` returns the name of one channel. The Inspector and the ASIO Mixer show them, remembered from the last connection. |
| `Snapshots` | The saved mixer snapshots (`AsioMixerSnapshot`: `Name`, `MasterVolume`, and the `Volume` / `Mute` / `Solo` of each source). |
| `AsioAudioSources` | A list of all [AsioAudioSource](/Documentation~/Audio%20Components/AsioAudioSource.md) components managed by this manager (Read Only). |
| `DisplayInfoOnGameWindow` | Whether the in-game overlay is shown. |
| `OverlayToggleKey` | The key that shows or hides the in-game overlay at runtime (default: F9; `None` for no key). |
| `OverlayScale` | The size of the in-game overlay (0.5 to 3, e.g. 2 on a 4K screen). |

#### Public Methods

| **Method** | **Description** |
|-|-|
| `Reconnect` | Disconnects from the ASIO driver and connects again with the current settings. Sources keep their state and position. |
| `PlayAll` | Plays (or resumes) all the sources managed by this manager, starting on the same sample. Also available on the *Master* strip of the ASIO Mixer. |
| `PauseAll` | Pauses all the playing sources, on the same sample (so they stay in sync when resumed). |
| `StopAll` | Stops all the sources (they go back to the start of their file). |
| `RestartAll` | Plays all the sources from the start of their file, together. |
| `SaveSnapshot(name)` | Saves the current mix as a snapshot (a snapshot with the same name is replaced). |
| `RecallSnapshot(name)` / `RecallSnapshot(index)` | Applies a saved snapshot (volume changes are smoothed while playing). Returns `false` if it doesn't exist. |
| `DeleteSnapshot(name)` | Deletes a snapshot. |
| `RequestAddAsioAudioSource` | Validates and adds a [AsioAudioSource](/Documentation~/Audio%20Components/AsioAudioSource.md) to the manager. Returns `true` if the source is successfully added, `false` otherwise. |
| `RequestRemoveAsioAudioSource` | Removes a [AsioAudioSource](/Documentation~/Audio%20Components/AsioAudioSource.md) from the manager. Returns `true` if the source is successfully removed, `false` otherwise. |

#### Unity Events

| **Event** | **Description** |
|-|-|
| `OnAsioDriverNameChanged` | Triggered when the ASIO driver name is updated. |
| `OnTargetSampleRateChanged` | Triggered when the target sample rate is updated. |
| `OnTargetBitsPerSampleChanged` | Triggered when the target bit depth is updated. |
