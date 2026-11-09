## AsioAudioManager

The AsioAudioManager compenent is designed to manage audio playback using the NAudio library and an ASIO driver. It acts as a central controller for multiple [CustomAsioAudioSource](/Documentation~/Audio%20Components/CustomAsioAudioSource.md) components, handling ASIO driver initialization, audio source validation, and playback coordination.

### Typical Workflow and Features

1.	Driver Connection: The Asio Audio Manager connects to the ASIO driver set in `AsioDriverName` when it is enabled, and stays connected until it is disabled, using all the driver's output channels (`AsioDriverOutputChannelCount`). Connection errors (driver not found, unsupported sample rate...) are logged in the console, along with the list of available drivers. Changing `AsioDriverName`, `TargetSampleRate` or `TargetBitsPerSample` at runtime (from code, the Inspector or the Game window overlay) reconnects the driver; sources keep their state and position.
2.	Audio Source Coordination:
    •	Audio Source Management: Validates, adds, and removes [CustomAsioAudioSource](/Documentation~/Audio%20Components/CustomAsioAudioSource.md) components.
    •	Real-time Mixing: Continuously mixes all registered sources (each one on its own output channel) into the stream sent to the ASIO driver. Sources can be played, paused, stopped, or have their volume, loop or channel changed without interrupting the driver.
3.	Inspector: The driver is picked from the list of ASIO drivers installed on the machine, and the sample rate and format from dropdowns. In Play mode, the Inspector shows the connection status, with buttons to reconnect and to open the driver's own control panel. The *Output Channels* section lists every source of the scene with its channel, status and file, highlights channel conflicts (with a *Fix* button that assigns free channels), and opens the ASIO Mixer window.
4.	GUI Integration:
    •	Debug GUI: Displays ASIO driver and audio source information in the Unity Game window.
    •	Interactive Controls: Allows runtime configuration of driver name, sample rate, and bit depth.

#### Public Properties
| **Property** | **Description** |
|-|-|
| `AsioDriverName` | The name of the ASIO driver to connect to. Changing it at runtime reconnects. |
| `TargetSampleRate` | The sample rate the ASIO driver runs at (default: 48000 Hz). Audio files are resampled to it on the fly. Changing it at runtime reconnects. |
| `TargetBitsPerSample` | The sample format sent to the ASIO driver (16-bit integer or 32-bit float). Changing it at runtime reconnects. |
| `IsConnected` | Whether the ASIO driver is connected and playing (Read Only). |
| `AsioDriverOutputChannelCount` | The number of output channels of the ASIO driver, i.e. the highest valid `TargetOutputChannel` (Read Only). |
| `AsioDriverInputChannelCount` | The number of input channels of the ASIO driver (Read Only). |
| `CustomAsioAudioSources` | A list of all [CustomAsioAudioSource](/Documentation~/Audio%20Components/CustomAsioAudioSource.md) components managed by this manager (Read Only). |
| `DisplayInfoOnGameWindow` | Whether to display ASIO driver and audio source information in the Unity Game window. |

#### Public Methods

| **Method** | **Description** |
|-|-|
| `Reconnect` | Disconnects from the ASIO driver and connects again with the current settings. Sources keep their state and position. |
| `RequestAddAsioAudioSource` | Validates and adds a [CustomAsioAudioSource](/Documentation~/Audio%20Components/CustomAsioAudioSource.md) to the manager. Returns `true` if the source is successfully added, `false` otherwise. |
| `RequestRemoveAsioAudioSource` | Removes a [CustomAsioAudioSource](/Documentation~/Audio%20Components/CustomAsioAudioSource.md) from the manager. Returns `true` if the source is successfully removed, `false` otherwise. |

#### Unity Events

| **Event** | **Description** |
|-|-|
| `OnAsioDriverNameChanged` | Triggered when the ASIO driver name is updated. |
| `OnTargetSampleRateChanged` | Triggered when the target sample rate is updated. |
| `OnTargetBitsPerSampleChanged` | Triggered when the target bit depth is updated. |
