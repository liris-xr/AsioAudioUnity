## Setup OSC Environment

### Introduction to OSC

(To implement)

### Creating the OSC Manager

**Before adding any OSC position component (that will send realtime position through OSC protocol), please verify that an `OSC` component is already in scene.**

To add an `OSC` component on scene, you can go multiple ways:

1. Use prefab: A prefab named `OSC Manager` is present on the `Assets` folder, at `Assets/AsioAudioUnity/Prefabs/OSC Manager.prefab`, you can simply drag and drop this prefab on scene.
2. Setup GameObject: You can add a GameObject with an `OSC` component on scene by going to *GameObject > AsioAudioUnity > OSC Manager*.

Once the `OSC` component is put in scene, you can set the `InPort`, `OutIP` and `OutPort` properties.

### Setup OSC Position

If you don't have any [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md) component on scene, you can either refer to [Setup ASIO Audio Sources In Scene](/docs/SetupAsioAudioSourcesInScene.md) section and follow next instructions, or you can go to *GameObject > AsioAudioUnity > Custom ASIO Audio Source (with OSC Position)*.
> For the [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md) component properties, go to the [CustomAsioAudioSource Properties Setup](/docs/Audio%20Components/CustomAsioAudioSource.md#properties-setup) section.

If there is already a [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md) component on scene and you want to attach an OSC position component, you can go multiple ways:

1. From the Inspector tab: Select the GameObject that contains the [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md), go to the Inspector tab, click on Add Component, and add either a [RoomPositionOsc](/docs/OSC%20Components/RoomPositionOsc.md) (if you want to track a room in Spat Revolution), or a [SourcePositionOsc](/docs/OSC%20Components/SourcePositionOsc.md) (if you want to track an audio source). You will have to manually set the properties (`Osc` and `RoomIndex` or `SourceIndex`).
2. From Unity menu: 
    - For each [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md) component: Select the GameObject that contains the [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md), and go to *AsioAudioUnity > Add Source Position OSC to selected Custom ASIO Audio Source(s)* to add a [SourcePositionOsc](/docs/OSC%20Components/SourcePositionOsc.md). The properties should already be set.
    - For all [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md) components: Go to *AsioAudioUnity > Add Source Position OSC to all Custom ASIO Audio Sources* to add a [SourcePositionOsc](/docs/OSC%20Components/SourcePositionOsc.md) to all GameObjects that contains a [CustomAsioAudioSource](/docs/Audio%20Components/CustomAsioAudioSource.md) component. The properties should already be set.