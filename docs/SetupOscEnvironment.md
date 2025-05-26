## Setup OSC Environment

### Introduction to OSC

OSC stands for Open Sound Control and is a protocol designed for show control and has the benefit to be flexible and accurate. This standard communicates to show devices through UDP/IP protocol by sending commands that look like this:
`"/menu/submenu/command", (bool float or string)`

Each environment uses its own controls, but the format is always the same.
For example, Spat Revolution has controls for sources and room to set properties such as position. The commands will look like this:
`"/source/(k)/xyz", [x,y,z]`
`"/room/(k)/xyz", [x,y,z]`
Where k is the index of the source or room and x,y,z to coordinates to set. 

For more information on OSC components, see [RoomPositionOsc](/docs/OSC%20Components/RoomPositionOsc.md) and [SourcePositionOsc](/docs/OSC%20Components/SourcePositionOsc.md).

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