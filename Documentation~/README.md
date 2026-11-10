# Asio Audio Unity documentation

Asio Audio Unity plays Unity's sounds through an **ASIO driver**, each sound on its own output channel, and sends the sounds' **positions over OSC**. With REAPER (ReaRoute) and Spat Revolution, this spatialises a Unity scene on any speaker layout. The spatialised result is played either by Spat Revolution, or by REAPER (Spat Revolution sends it back): both workflows are explained in [How it works](/Documentation~/Tutorials/1-Installation.md#how-it-works).

## Tutorials

Step by step, from installation to a spatialised scene. Follow them in order the first time.

1. [Installation](/Documentation~/Tutorials/1-Installation.md): requirements, installing the package, the demo scene, how it works
2. [Set up REAPER](/Documentation~/Tutorials/2-SetupReaper.md): receive Unity's channels with ReaRoute, and send them to Spat Revolution
3. [Set up Spat Revolution](/Documentation~/Tutorials/3-SetupSpatRevolution.md): audio output, OSC input, and the source / room chain
4. [First play in Unity](/Documentation~/Tutorials/4-FirstPlay.md): choose the driver, play the demo scene, and check each link
5. [ASIO sources in your scene](/Documentation~/Tutorials/5-AddSources.md): create or convert sources, set them up, play them from scripts
6. [Send positions to Spat Revolution](/Documentation~/Tutorials/6-SendPositionsToSpat.md): the Spat Controller
7. [Control REAPER from Unity](/Documentation~/Tutorials/7-ControlReaper.md): follow Unity's transport, play regions, run actions
8. [Control Unity over OSC](/Documentation~/Tutorials/8-ControlUnityOverOsc.md): play, mix and recall snapshots from another application

Something doesn't work? See [Troubleshooting](/Documentation~/Tutorials/Troubleshooting.md).

## Reference

Every component, window and setting in detail.

**Audio**

- [ASIO Audio Settings](/Documentation~/Reference/AsioAudioSettings.md): *Project Settings > ASIO Audio*: driver, sample rate, master volume, snapshots, editor pause, overlay
- [AsioAudioSource](/Documentation~/Reference/AsioAudioSource.md): the component that plays a file on an output channel: Inspector, waveform, fades, smooth loop, API
- [AsioAudioManager](/Documentation~/Reference/AsioAudioManager.md): plays all the sources (created automatically): transport in sync, snapshots, API
- [ASIO Audio Mixer and in-game overlay](/Documentation~/Reference/AsioAudioMixer.md): control all the sources side by side

**Spat OSC plugin** (`Modules/SpatOsc`)

- [SpatController](/Documentation~/Reference/SpatController.md): sends the positions of the sources and rooms to Spat Revolution
- [Legacy position components](/Documentation~/Reference/LegacyPositionComponents.md): *SourcePositionOsc* and *RoomPositionOsc*, obsolete

**REAPER OSC plugin** (`Modules/ReaperOsc`)

- [ReaperController](/Documentation~/Reference/ReaperController.md): follows Unity's transport, and sends commands to REAPER

**OSC library** (`Runtime/Osc`)

- [AsioOscRemote and OSC Manager](/Documentation~/Reference/AsioOscRemote.md): receive OSC messages, and control the sources with them

## Package layout

The audio core (`Runtime`, `Editor`) doesn't depend on OSC. The two plugins are built in (nothing to import), each in its own assemblies: **Spat OSC** (`AsioAudioUnity.SpatOsc`) and **REAPER OSC** (`AsioAudioUnity.ReaperOsc`). They don't depend on each other, and both use the shared OSC library (`AsioAudioUnity.Osc`). Everything is in the `AsioAudioUnity` namespace.
