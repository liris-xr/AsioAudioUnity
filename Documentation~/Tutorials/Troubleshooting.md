# Troubleshooting

[Documentation home](/Documentation~/README.md)

Start with **AsioAudioUnity > Setup Check**: it shows the state of each link (settings, driver, connection, sample rate, sources, REAPER, Spat Revolution), live in Play mode, with a fix button where possible, and a **?** button that opens the section below about it. Its **Test Channels** button checks the routing of each source.

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

With *Fall Back To Unity Audio* (on by default), the sources then play through Unity's audio output, all channels mixed together, and the status shows *Unity audio (no ASIO driver)*. If nothing is heard: Unity's audio must be enabled (*Project Settings > Audio > Disable Unity Audio* off), and the scene needs an Audio Listener.

## "Audio file not found"

- Files inside the project are tracked by Unity: moving or renaming them in the Project window updates the sources of the open scenes, and opening a scene recovers paths that no longer exist. If the Inspector still shows the error, click **Use the moved file**, or set the file again.
- Files moved outside Unity (in the Explorer) keep their GUID only if their `.meta` file moved with them. Otherwise, a file with the same name in the scene's folder is used (this is logged in the Console).
- In a build, the files used by the sources of the built scenes are copied next to the player automatically. Files set from code at runtime aren't: copy them yourself, at the same relative path.
- OGG files can't be read: convert them to WAV or FLAC.

## "Channel conflict" / "No such channel"

Each source needs its own output channel, between 1 and the driver's number of outputs. *Project Settings > ASIO Audio > Output Channels* lists the sources and can fix the conflicts.

## A source is heard on the wrong track, or not in Spat Revolution

Click **Test Channels** (Setup Check or ASIO Audio Mixer toolbar): each ASIO Audio Source beeps in turn on its output channels (one per second), and the window says which one.

- **No REAPER track shows a level**: check the track's input (*ReaRoute* number = the source's output channel), that it is armed with input monitoring, and that REAPER's audio device is open. **Set Up REAPER Tracks** sets all of this.
- **Another track shows a level**: the track's ReaRoute input doesn't match the source's output channel. Run **Set Up REAPER Tracks** again.
- **The track shows a level, but not the Spat source**: check that the track's Spat Revolution Send plugin has *Local Audio Path* enabled, and which Spat source it feeds. With **Number Track Names**, the track's name starts with the number of the Spat source it should feed.

## The sources aren't in sync

Sources started one by one with `Play` can start a few milliseconds apart. Use *Play All* (the *Master* strip of the ASIO Audio Mixer, or `AsioAudioManager.Instance.PlayAll()`): all sources start on the same sample.

## Spat Revolution doesn't move the sources

- Spat Revolution needs an OSC input connection (*Preferences > OSC Main*) on the Spat Controller's port (8100 by default). Click **Send Positions Now** on the Spat Controller to test.
- To check that Spat Revolution and Unity can talk at all, use **Listen To Spat** (see [Listening to Spat Revolution](/Documentation~/Reference/SpatController.md#listening-to-spat-revolution)): if Unity receives Spat's messages, the network and OSC are set up.
- **A source moved in Spat Revolution jumps back**: Unity drives the positions, and sends them again at least every *Keep Alive Interval*. Tick **Follow Spat** (with *Listen To Spat*) to move the Unity object instead. If it moves mirrored left/right, tick **Invert Azimuth**.
- Each source is sent as the Spat source numbered like its output channel: check the numbers in the *Sent to Spat* table.

## Unity doesn't follow REAPER

- *Follow REAPER* needs REAPER's OSC control surface in mode *Configure device IP + local port*, with its *Device port* set to the controller's *Listen Port* (9000 by default).
- If the Inspector says the port can't be opened, another application uses it: choose another port, in both.
- Tick **Log Messages** to see what REAPER sends.

## REAPER doesn't follow Unity

- REAPER needs an OSC control surface listening on the REAPER Controller's port (8000 by default): see [7. Control REAPER from Unity](/Documentation~/Tutorials/7-ControlReaper.md).
- *Follow Transport* mirrors *Play All*, *Pause All*, *Stop All* and *Restart All*, not sources played one by one: add *SourcePlay* commands for those.
- Tick **Log Messages** on the REAPER Controller to see what is sent.

## Upgrading to version 4.0

Version 4.0 removes the *Source Position OSC* and *Room Position OSC* components (obsolete since 3.0), replaced by the [Spat Controller](/Documentation~/Reference/SpatController.md). Scenes that still contain them show **missing scripts** (*The referenced script on this Behaviour is missing*), and the Setup Check lists those objects.

- **To convert them automatically**, before upgrading: install version 3.2 (`https://github.com/liris-xr/AsioAudioUnity.git#v3.2.0`), open each scene, click **Replace with Spat Controller** in the components' Inspector, save the scenes, then upgrade to 4.0. Their numbers are kept: a room becomes one of the Spat Controller's *Other Objects*, and a source's number becomes its *Spat #* if it differs from its output channel.
- **Or by hand**, in 4.0: remove the missing scripts (in their Inspector, or with *Select* in the Setup Check), add a Spat Controller (*GameObject > AsioAudioUnity > Spat Controller*), and add the rooms to its *Other Objects*. ASIO Audio Sources are tracked automatically, as the Spat source numbered like their output channel.

Code that used the OSC Manager's `Closing` event must use the component's own `OnDestroy` instead.

## Upgrading from version 2.x

- Scenes contain an `AsioAudioManager` component: open *Project Settings > ASIO Audio* and click **Migrate the Scene's ASIO Audio Manager**. Its settings move to the project settings, and the component is removed.
- Then follow [Upgrading to version 4.0](#upgrading-to-version-40) for the *Source Position OSC* / *Room Position OSC* components.
