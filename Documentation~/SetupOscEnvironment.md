## Setup OSC Environment

### Introduction to OSC

OSC stands for Open Sound Control and is a protocol designed for show control and has the benefit to be flexible and accurate. This standard communicates to show devices through UDP/IP protocol by sending commands that look like this:
`"/menu/submenu/command", (bool float or string)`

Each environment uses its own controls, but the format is always the same.
For example, Spat Revolution has controls for sources and room to set properties such as position. The commands will look like this:
`"/source/(k)/xyz", [x,y,z]`
`"/room/(k)/xyz", [x,y,z]`
Where k is the index of the source or room and x,y,z to coordinates to set. 

Sending positions to Spat Revolution is the **Spat OSC plugin** (`Modules/SpatOsc`, assembly `AsioAudioUnity.SpatOsc`), with the [Spat Controller](/Documentation~/Spat%20OSC/SpatController.md). It uses the shared OSC library of the package (`Runtime/Osc`, assembly `AsioAudioUnity.Osc`), which also has the OSC Manager and the [AsioOscRemote](/Documentation~/OSC%20Components/AsioOscRemote.md) to control Unity from other applications. The [REAPER OSC plugin](/Documentation~/REAPER%20OSC/ReaperController.md) uses the same library to control REAPER.

### Sending positions with the Spat Controller

The [Spat Controller](/Documentation~/Spat%20OSC/SpatController.md) sends the positions of all the ASIO Audio Sources (and rooms) to Spat Revolution from a single component, without an OSC Manager:

1. *GameObject > AsioAudioUnity > Spat Controller* (or drag the `Spat Controller` prefab, in `Modules/SpatOsc/Runtime/Prefabs`).
2. Check Spat Revolution's **Address** and **Port** (127.0.0.1 and 8100 with the setup above).
3. Keep **Track All ASIO Sources** ticked: each source is sent as the Spat source numbered like its output channel (the REAPER track / Spat input it arrives on), or with another number set in the *Sent to Spat* table.
4. Add rooms in **Other Objects** (type *Room*), and click **Send Positions Now** to check that Spat receives them.

The demo scene has a Spat Controller tracking its four sources, and the Main Camera as room 1.

### Receiving OSC: the OSC Manager

The OSC Manager (`OSC` component) receives OSC messages, for the [ASIO OSC Remote](/Documentation~/OSC%20Components/AsioOscRemote.md) (to control playback, volumes and snapshots from TouchOSC, REAPER, etc.). It isn't needed to send positions or to control REAPER.

To add one: *GameObject > AsioAudioUnity > OSC Manager*, or drag the `OSC Manager` prefab (`Runtime/Osc/Prefabs`). Set its `InPort` to the port the messages are sent to.

### Obsolete: per-object position components

Before version 3.0, positions were sent by a [SourcePositionOsc](/Documentation~/Spat%20OSC/SourcePositionOsc.md) or [RoomPositionOsc](/Documentation~/Spat%20OSC/RoomPositionOsc.md) component on each object, through an OSC Manager. They are obsolete: they still work in existing scenes, but can no longer be added from the menus. Their Inspector has a **Replace with Spat Controller** button, which moves them to the scene's Spat Controller (created if needed): a room becomes one of its *Other Objects*, and an ASIO Audio Source keeps its Spat number. Then remove the OSC Manager if nothing else uses it.
