## CustomAsioAudioSource

The CustomAsioAudioSource component is designed to manage audio playback using the NAudio library in conjunction with an ASIO driver. It provides functionality for loading, configuring, and controlling audio files, while integrating with an [AsioAudioManager](/docs/Audio%20Components/AsioAudioManager.md) for playback coordination.

### Typical Workflow and Features

1. Audio File Setup: The audio file is loaded, converted if needed, and configured for playback.
2. Playback:
•	The Play, Pause, Stop, and Restart methods control playback.
•	The [AsioAudioManager](/docs/Audio%20Components/AsioAudioManager.md) coordinates playback across multiple sources.
3. Integration: The component communicates with the AsioAudioManager to ensure proper playback synchronization and configuration.

#### Example

```cs
using UnityEngine;
using AsioAudioUnity;

public class PlayAsioSourceAndLoop : MonoBehaviour
{
    private void Start()
    {
        CustomAsioAudioSource audioSource = gameObject.AddComponent<CustomAsioAudioSource>();
        audioSource.AudioFilePath = "path/to/audio/file.wav";
        audioSource.Volume = 0.8f;
        audioSource.Loop = true;
        audioSource.Play();
    }
}
```

#### Public Properties

| **Property** | **Description** |
|-|-|
| `AudioFilePath` | The path to the audio file to be played. |
| `OriginalAudioFilePath` | The original unconverted path of the audio file (Read Only). |
| `TargetOutputChannel` | The output channel for the audio source. |
| `ReferencedAsioAudioManager` | The AsioAudioManager managing this audio source. |
| `Volume` | The playback volume (range: 0.0 to 1.0). |
| `PlayOnEnable` | Whether playback starts automatically when the component is enabled. |
| `Loop` | Whether the audio should loop when it reaches the end. |
| `AudioStatus` | The current playback status (`Stopped`, `Paused`, or `Playing`) (Read Only). |
| `CurrentTimestamp` | The current playback position in seconds (Read Only). |

#### Public Methods

| **Method** | **Description** |
|-|-|
| `Play` | Starts playback of the audio file. |
| `Pause` | Pauses playback of the audio file. |
| `Stop` | Stops playback and resets the playback position. |
| `Restart` | Restarts playback from the beginning. |
| `SetSourceWaveProviderFromFileName` | Configures the SourceWaveProvider from the specified audio file path, with optional conversion and offset settings. |
| `ConvertSamplesAndCreateNewAudioFile` | Converts audio samples to a target sample rate and bit depth, and writes them to a new file. |

#### Unity Events

| **Event** | **Description** |
|-|-|
| `OnAudioFilePathChanged` | Triggered when the audio file path is updated. |
| `OnTargetOutputChannelChanged` | Triggered when the target output channel is updated. |
| `OnVolumeChanged` | Triggered when the playback volume is updated. |
| `OnPlay` | Triggered when playback starts. |
| `OnPause` | Triggered when playback is paused. |
| `OnStop` | Triggered when playback stops. |

> **Note:** The ```Restart``` method doesn't trigger any events.
