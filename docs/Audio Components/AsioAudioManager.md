## AsioAudioManager

The AsioAudioManager compenent is designed to manage audio playback using the NAudio library and an ASIO driver. It acts as a central controller for multiple [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md) components, handling ASIO driver initialization, audio source validation, and playback coordination.

### Typical Workflow and Features

1.	Driver Initialization: The Asio Audio Manager onnects to a specified ASIO driver (AsioDriverName) and manages its lifecycle. If a driver has been found, it will track the number of input channels (AsioDriverInputChannelCount) and ensures compatibility with audio sources.
2.	Audio Source Coordination:
    •	Audio Source Management: Validates, adds, and removes CustomAsioAudioSource components.
    •	Global Multiplexing: Combines audio sources into a single MultiplexingWaveProvider for playback through the ASIO driver.
3.	GUI Integration:
    •	Debug GUI: Displays ASIO driver and audio source information in the Unity Game window.
    •	Interactive Controls: Allows runtime configuration of driver name, sample rate, and bit depth.

#### Public Properties
| **Property** | **Description** |
|-|-|
| `AsioDriverName` | The name of the ASIO driver to connect to. |
| `TargetSampleRate` | The target sample rate for audio playback (default: 48000 Hz). |
| `TargetBitsPerSample` | The target bit depth for audio playback (16 or 32 bits). |
| `AsioDriverInputChannelCount` | The number of input channels supported by the ASIO driver (Read Only). |
| `CustomAsioAudioSources` | A list of all `CustomAsioAudioSource` components managed by this manager (Read Only). |
| `DisplayInfoOnGameWindow` | Whether to display ASIO driver and audio source information in the Unity Game window. |

#### Public Methods

| **Method** | **Description** |
|-|-|
| `ConnectMixAndPlay` | Connects to the ASIO driver, sets up the global multiplexing provider, and starts playback for all audio sources. Optionally reinitializes audio samples from file paths. |
| `RequestAddAsioAudioSource` | Validates and adds a `CustomAsioAudioSource` to the manager. Returns `true` if the source is successfully added, `false` otherwise. |
| `RequestRemoveAsioAudioSource` | Removes a `CustomAsioAudioSource` from the manager. Returns `true` if the source is successfully removed, `false` otherwise. |

#### Unity Events

| **Event** | **Description** |
|-|-|
| `OnAsioDriverNameChanged` | Triggered when the ASIO driver name is updated. |
| `OnTargetSampleRateChanged` | Triggered when the target sample rate is updated. |
| `OnTargetBitsPerSampleChanged` | Triggered when the target bit depth is updated. |
