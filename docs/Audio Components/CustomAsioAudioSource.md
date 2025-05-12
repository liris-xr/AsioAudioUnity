## CustomAsioAudioSource

The CustomAsioAudioSource component is designed to manage audio playback using the NAudio library in conjunction with an ASIO driver. It provides functionality for loading, configuring, and controlling audio files, while integrating with an [AsioAudioManager](/docs/Audio%20Components/AsioAudioManager.md) for playback coordination.

### Typical Workflow and Features

1. Audio File Setup: The audio file is loaded (and converted if needed) to wave samples on object `SourceWaveProvider`, and configured for playback.
    > **Note:** If the file needs conversion, the component will convert the audio samples AND create a new audio file to a new path, thus changing the `AudioFilePath` to the converted path by adding `_newSampleRate_newBitsPerSample` at the end of the file name. However, the original file path is still stored in `OriginalAudioFilePath`.

    > **Example:** If the original path of the file was `Folder/OriginalSound.wav` encoded in 44100 Hz - 32 bit, and a conversion is needed to 48000 Hz - 32 bit, the audio samples will be converted to desired sample rate, a new audio file will be created, and the new `AudioFilePath` will be `Folder/OriginalSound_48000_32.wav`
2. Playback:
    •	The Play, Pause, Stop, and Restart methods control playback, by sending signal to [AsioAudioManager](/docs/Audio%20Components/AsioAudioManager.md).
    •	The [AsioAudioManager](/docs/Audio%20Components/AsioAudioManager.md) coordinates playback across multiple sources.
3. Integration: The component communicates with the [AsioAudioManager](/docs/Audio%20Components/AsioAudioManager.md) to ensure proper playback synchronization and configuration.

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
| `AudioFilePath` | The path to the audio file to be played, might be converted if needed. |
| `OriginalAudioFilePath` | The original unconverted path of the audio file (Read Only). |
| `TargetOutputChannel` | The output channel for the audio source. |
| `ReferencedAsioAudioManager` | The AsioAudioManager managing this audio source. |
| `SourceWaveProvider` | The provider object containing the wave samples (Read Only). |
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
| `DestroySourceWaveProvider` | Destroy the SourceWaveProvider from the ASIO Audio Source. | 
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
