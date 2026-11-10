# Troubleshooting

[Documentation home](/Documentation~/README.md)

## The sources show *Playing*, but nothing is heard

**The Console warns that the driver "is connected but isn't asking for audio"**, and the sources' time stays at 0: the driver doesn't process audio.

- With **ReaRoute**, REAPER must be running **with its audio device open**. In REAPER, check *Options > Preferences > Audio > Device*, turn off *Close audio device when stopped and application is inactive* (*Preferences > Audio*), and press Play once in REAPER to make sure its audio runs.
- With another driver, check that no other application holds it.

**The time advances, but nothing is heard**:

- In REAPER, the tracks must be armed, with the right ReaRoute input (output channel 3 arrives on the track with input ReaRoute 3), and their Spat Send plugin must have *Local Audio Path* enabled.
- In workflow B (REAPER plays the result), check the result track: its Spat Revolution Return plugin with *Local Audio Path* enabled, its number of channels, and its route to the audio interface. In Spat Revolution, the *Output* must go to that Return plugin.
- Check the volumes and mute / solo in the ASIO Audio Mixer, and the *Master Volume* in *Project Settings > ASIO Audio*.

## "Could not start the ASIO driver"

- The **sample rate** isn't supported by the driver. ReaRoute only accepts REAPER's sample rate: use the same in *Project Settings > ASIO Audio*.
- Another application uses the driver exclusively. In the Editor, a preview or the *Driver Control Panel* can also hold it: they release it when entering Play mode.

## "No ASIO driver is set" / "driver not found"

Choose a driver in *Project Settings > ASIO Audio*. A driver set on another computer shows *(not installed)*: pick one installed on this machine.

## "Audio file not found"

- The path is stored from the project folder (e.g. `Assets/Sounds/rain.wav`). If you moved or renamed the file, set it again in the Inspector.
- When a scene is opened, sources whose file is missing are relinked automatically to a file with the same name in the scene's folder (this is logged in the Console).
- In a build, the files used by the sources of the built scenes are copied next to the player automatically. Files set from code at runtime aren't: copy them yourself, at the same relative path.
- OGG files can't be read: convert them to WAV or FLAC.

## "Channel conflict" / "No such channel"

Each source needs its own output channel, between 1 and the driver's number of outputs. *Project Settings > ASIO Audio > Output Channels* lists the sources and can fix the conflicts.

## The sources aren't in sync

Sources started one by one with `Play` can start a few milliseconds apart. Use *Play All* (the *Master* strip of the ASIO Audio Mixer, or `AsioAudioManager.Instance.PlayAll()`): all sources start on the same sample.

## Spat Revolution doesn't move the sources

- Spat Revolution needs an OSC input connection (*Preferences > OSC Main*) on the Spat Controller's port (8100 by default). Click **Send Positions Now** on the Spat Controller to test.
- Each source is sent as the Spat source numbered like its output channel: check the numbers in the *Sent to Spat* table.
- If the Spat Controller warns about *Source Position OSC* components, positions are sent twice: remove them with its button.

## REAPER doesn't follow Unity

- REAPER needs an OSC control surface listening on the REAPER Controller's port (8000 by default): see [7. Control REAPER from Unity](/Documentation~/Tutorials/7-ControlReaper.md).
- *Follow Transport* mirrors *Play All*, *Pause All*, *Stop All* and *Restart All*, not sources played one by one: add *SourcePlay* commands for those.
- Tick **Log Messages** on the REAPER Controller to see what is sent.

## Upgrading from version 2.x

- Scenes contain an `AsioAudioManager` component: open *Project Settings > ASIO Audio* and click **Migrate the Scene's ASIO Audio Manager**. Its settings move to the project settings, and the component is removed.
- *Source Position OSC* / *Room Position OSC* components still work, but are obsolete: use **Replace with Spat Controller** in their Inspector.
