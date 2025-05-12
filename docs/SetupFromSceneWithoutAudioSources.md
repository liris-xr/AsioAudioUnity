## Setup ASIO Audio Sources From Scene Without Audio Sources

If you want to start from scratch from an empty scene or a scene without any Audio Sources, you can follow this part of the documentation.

### Intended ASIO audio workflow

The intended working operation of this package is based on how Unity Audio Sources work. It tends to reproduce their behaviour with some additional parameters.

The component [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md) is the base component of an ASIO Audio Source. It is concretely a replacement of the Unity Audio Source, with similar properties:
- Property `AudioClip` from Audio Source is replaced with `AudioFilePath`.
- Property `Volume` has the same name and behaviour.
- Property `PlayOnAwake` from Audio Source is replaced with `PlayOnEnable` (triggers `Play` from `OnEnable` instead from `Awake`).
- Property `Loop` has the same name and behaviour.

**For the ASIO Audio Sources to work, we need an [AsioAudioManager](/docs/Audio%20Components/AsioAudioManager.md) component on scene.**

There is multiple reasons that justify the need of this component:
- ASIO Audio Sources can't actually send audio samples data independently from each other.
    - A manager is needed to regroup them together to send a multiplexed audio samples object on the ASIO driver.

- Audio samples from ASIO Audio Sources need to respect a certain audio sample rate and bit depth (bits per sample).
    - A manager is needed to tell ASIO Audio Sources the potential conversion to make them work.

- ASIO drivers support a limited number of input channels, and each ASIO Audio Source has be played on a specific channel.
    - A manager is needed to handle verification of ASIO Audio Sources target channel (validity and no duplicate).


Here is a concrete example of the ASIO audio workflow:

```mermaid
graph LR
    A["AsioAudioManager (Target 48000 Hz, 32 bit)<br>"] --> n13["Audio Samples 1 playing on channel 1<br>"] & n14["Silence on channel 2<br>"] & n15["Audio Samples 3 playing on channnel 3<br>"] & n16["Silence on channel 4 <br>"]
    n1["Audio Samples 1 (converted to 48000 Hz, 32 bit)<br>"] -- Play() --> A
    n2["Audio Samples 2 (converted to 48000 Hz)<br>"] -- Stop() --> A
    n3["Audio Samples 3 (converted to 32 bit)<br>"] -- Play() --> A
    n4["Audio Samples 4<br>"] -- Pause() --> A
    n5["CustomAsioAudioSource 1<br>"] -- Target Channel 1 --> n1
    n6["CustomAsioAudioSource 2<br>"] -- Target Channel 2 --> n2
    n7["CustomAsioAudioSource 3<br>"] -- Target Channel 3 --> n3
    n8["CustomAsioAudioSource 4<br>"] -- Target Channel 4 --> n4
    n9["Audio File 1 (44100 Hz, 16 bit)<br>"] --> n5
    n10["Audio File 2 (44100 Hz, 32 bit)<br>"] --> n6
    n11["Audio File 3 (48000 Hz, 16 bit)<br>"] --> n7
    n12["Audio File 4 (48000 Hz, 32 bit)<br>"] --> n8
    n13 --> B["ASIO Driver<br>"]
    n14 --> B
    n15 --> B
    n16 --> B

    A@{ shape: rounded}
    B@{ shape: diam}
    n1@{ shape: rect}
    n2@{ shape: rect}
    n3@{ shape: rect}
    n4@{ shape: rect}
    n5@{ shape: rounded}
    n6@{ shape: rounded}
    n7@{ shape: rounded}
    n8@{ shape: rounded}
    n9@{ shape: rect}
    n10@{ shape: rect}
    n11@{ shape: rect}
    n12@{ shape: rect}
    n13@{ shape: rect}
    n14@{ shape: rect}
```

### Creating the ASIO Audio Manager

To add an [AsioAudioManager](/docs/Audio%20Components/AsioAudioManager.md) component on scene, you can go multiple ways.

1. Use prefab: A prefab named `ASIO Audio Manager` is present on the `Assets` folder, at `Assets/AsioAudioUnity/Prefabs/ASIO Audio Manager.prefab`, you can simply drag and drop this prefab on scene.
2. Setup Component: You can add an [AsioAudioManager](/docs/Audio%20Components/AsioAudioManager.md) component on any GameObject on scene by selecting it, right click and go to `AsioAudioUnity > ASIO Audio Manager`.

How to know how many channels are available on driver

### Creating a new ASIO Audio Source



1) Use prefab
2) Setup Component 


Set the properties


### Setup with OSC position