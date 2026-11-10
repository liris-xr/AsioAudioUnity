# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [3.0.0] - 2026-11-10

### Changed

- **Breaking:** the ASIO settings are now project settings (*Project Settings > ASIO Audio*, saved in an `AsioAudioSettings` asset included in builds), and the `AsioAudioManager` no longer goes in the scene: it is created automatically when the first source is enabled (`AsioAudioManager.Instance`), and kept across scene loads so the driver stays connected. The *Migrate the Scene's ASIO Audio Manager* button (in *Project Settings > ASIO Audio*, or in the manager's Inspector) moves a scene manager's settings (driver, sample rate, format, master volume, overlay, snapshots) to Project Settings and removes it; unmigrated scenes keep working meanwhile. `AsioAudioSource.ReferencedAsioAudioManager` is now read-only, and the `AsioAudioManager` fields of `AsioOscRemote` and `ReaperController` are removed. The *GameObject > AsioAudioUnity > ASIO Audio Manager* menu item is replaced by *AsioAudioUnity > Project Settings…*.
- **Breaking:** mixer snapshots identify sources by output channel instead of by reference, so a snapshot works in every scene. Snapshots of a scene manager are converted by the migration.
- **Breaking:** new layout: the OSC protocol is a shared library of the package (`Runtime/Osc`, assembly `AsioAudioUnity.Osc`: OSC Manager, OSC remote), and the integrations are two optional plugins, built in: **Spat OSC** (`Modules/SpatOsc`, assembly `AsioAudioUnity.SpatOsc`) and **REAPER OSC** (`Modules/ReaperOsc`, assembly `AsioAudioUnity.ReaperOsc`). The audio core doesn't depend on any of them, and the plugins don't depend on each other. Menus and prefabs moved with them; existing scenes and prefabs keep working. The `ASIO Audio Source` prefab (`Runtime/Prefabs`) is now a plain source, without a Source Position OSC (existing instances stay linked to it). Assembly definitions referencing the old OSC classes need `AsioAudioUnity.SpatOsc` for the position senders.
- **Breaking:** the per-object position senders (`SourcePositionOsc`, `RoomPositionOsc` and their base `ObjectPositionOsc`) are obsolete, replaced by the `SpatController`. Existing components still work, but are hidden from *Add Component*, and their Inspector has a *Replace with Spat Controller* button (a room becomes one of its tracked objects, a source keeps its Spat number). The *Add Source Position OSC to selected / all ASIO Audio Sources* and *GameObject > AsioAudioUnity > ASIO Audio Source (with Source Position OSC)* menu items are removed, as is `SpatOscMenus.AddSourcePositionOsc`.
- The demo scene uses a Spat Controller (tracking its four sources, and the Main Camera as room 1) instead of an OSC Manager with position components.
- The ASIO Mixer window is renamed **ASIO Audio Mixer** (*AsioAudioUnity > ASIO Audio Mixer*, *Window > Audio > ASIO Audio Mixer*; class `AsioAudioMixerWindow`).
- The fade-out of a source (when *Fade Out* is set) also applies when a non-looping file reaches its end, like an item fade-out in REAPER (before, it only applied to Pause and Stop).

### Added

- Spat OSC plugin: the `SpatController` component sends the positions of every ASIO Audio Source and of rooms or other objects to Spat Revolution, from a single component, without an OSC Manager. Positions are sent on movement (rate-limited, with a keep-alive), converted to Spat's axes, relative to an optional origin and scale. Its Inspector lists what is sent, warns about sources that also have a Source Position OSC component, and can send all positions now (also in Edit mode). *GameObject > AsioAudioUnity > Spat Controller*.
- Spat source numbers: each ASIO Audio Source is sent as the Spat source numbered like its output channel by default, or as any other number, or not at all (`SpatController.SourceMappings`, `GetSpatIndex`, `SetSpatIndex`). Edited in the Spat Controller's *Sent to Spat* table (send checkbox, Spat number, reset to the output channel), with a warning when two sources use the same Spat number.
- Fades drawn on the waveform of the ASIO Audio Source Inspector, like REAPER's item fades: the fade-in and fade-out curves with the attenuated part shaded, and handles at the top to drag the fade durations (double-click: no fade). The fade-out is dimmed when looping (it then only applies to Pause and Stop).
- Smooth loop: `AsioAudioSource.LoopCrossfadeDuration` (*Loop Crossfade* in the Inspector) crossfades the end of a looping file with its start (equal-power), so the loop point can't be heard; each loop then continues from the end of the crossfade. Done before resampling, for streamed and preloaded files, and the playback position loops too. Drawn on the waveform, with a handle to drag its duration and a line where each loop continues.
- Fade curves: each fade of an ASIO Audio Source has a curve, `Squared` (the default, as before), `Linear`, `InvertedSquared` or `SCurve` (`AsioAudioSource.FadeInCurve` and `FadeOutCurve`, `AsioFadeCurve`, `AsioFadeCurves.Evaluate`). Chosen next to the fade durations in the Inspector, or with a right-click on a fade in the waveform, which draws each fade with its curve. A fade that changes direction halfway (e.g. a Pause during a fade-in) continues from the same level.
- REAPER OSC plugin (`Modules/ReaperOsc`): the `ReaperController` component controls REAPER over OSC. It follows Unity's transport (`PlayAll`, `PauseAll`, `StopAll`, `RestartAll`, and the sources starting with the scene), and sends commands (play, stop, record, go to a time, marker or region, play a region in loop, run actions and Lua scripts with or without a value, any OSC message) automatically on Unity events (scene start, transport, a source playing / pausing / stopping), from code or UnityEvents (`Trigger("name")`), or with the Inspector's Send button. Comes with `PlayRegion.lua`, installable from the Inspector.
- *Driver Control Panel* in Edit mode (*Project Settings > ASIO Audio*): the driver is loaded for its control panel (during a preview, the preview's driver is used), which also reads its number of outputs, channel names and sample format (shown as *Driver Outputs* and *Driver Format*) without entering Play mode; it is reloaded on each click, so changes made in the panel are read the next time. It is released with *Release Driver*, when leaving the page, entering Play mode, starting a preview or reloading scripts.
- Missing audio files are relinked when a scene is opened in the Editor: a source whose file isn't found is pointed to the file with the same name under the scene's folder (when there is exactly one), and the change is logged. Fixes the demo scene's sounds when the sample is imported for another version, and scenes moved together with their sounds.
- Driver diagnostics: a warning when the ASIO driver is connected but doesn't ask for audio (e.g. ReaRoute while REAPER's audio device is closed), `AsioAudioManager.RenderedFrames`, and an error logged when the driver stops because of an error in the audio callback (it used to go silent without a message).
- `AsioAudioManager.TransportCommandIssued` static event: called after `PlayAll`, `PauseAll`, `StopAll` and `RestartAll`.
- `OscUdpSender`: sends OSC messages to an address and port without an OSC Manager (used by the Spat and REAPER controllers).
- `OSC.Closing` event: called when an OSC Manager closes (the position senders reset their positions on it).
- *GameObject > AsioAudioUnity > ASIO OSC Remote*, *Spat Controller* and *REAPER Controller* menu items.
- Automated tests of the end-of-file fade-out, the Pause fade-out, the fade curves and the smooth loop.

### Fixed

- A *Fade Out* longer than one audio buffer (about 10 ms) was cut short on Pause and Stop: the sound stopped after the first buffer of the fade, and a Stop rewound at that point. The whole fade-out now plays.

## [2.0.0] - 2026-11-09

### Changed

- **Breaking:** `CustomAsioAudioSource` is renamed `AsioAudioSource` (component, prefab, menus and documentation), and `AsioAudioManager.CustomAsioAudioSources` is renamed `AsioAudioSources`. Existing scenes and prefabs keep working, and Unity's API updater offers to update scripts that use the old names.
- **Breaking:** the OSC classes (`OSC`, `OscMessage`, `OscMessageHandler`, `UDPPacketIO`, `ObjectPositionOsc`, `SourcePositionOsc`, `RoomPositionOsc`) are now in the `AsioAudioUnity` namespace, like the rest of the package. Existing scenes keep working; scripts using them need `using AsioAudioUnity;`.
- **Breaking:** the package's runtime assembly only builds for Windows players (and the Editor), the only platform ASIO exists on.
- `.ogg` files are no longer offered in the file browser and drag and drop: NAudio can't decode them.
- `PlayAll`, `PauseAll` and `StopAll` change the state of all sources on the same sample (sample-accurate sync).
- Play, Pause and Stop fades use a smoother (squared) curve.
- The manager logs a line when it connects (driver, outputs, sample rate, driver format).
- Volumes are shown and edited in decibels (Inspector, ASIO Mixer and overlay), with a mixing-desk fader law: half travel is -20 dB instead of -6 dB, so the useful range gets most of the fader. `Volume` itself is unchanged (linear, 0 to 1).
- New ASIO Audio Source inspector layout, in cards: audio file (drop zone, Browse, Show in Explorer), playback (status, waveform, Play / Pause / Stop / Restart, Loop, Play On Enable), mix & output (dB fader with level meter, Mute / Solo, output channel grid showing the channels used by other sources), advanced settings and events.
- Numbers in the Inspector and the overlay always use a decimal point, whatever the system language.
- New real-time audio engine: the ASIO driver stays connected and continuously plays a mix of all sources. Play, Pause, Stop, Seek, volume, loop and output channel changes are instant and no longer interrupt the other sources.
- Audio files are mixed down to mono and resampled on the fly to the target sample rate. Converted copies (`name_48000_32.wav`) are no longer written to disk.
- Volume changes are applied live and smoothly while playing. Play, Pause, Stop and Mute fade in or out over 5 ms, so they don't click.
- Loops are now seamless.
- `CurrentTimestamp` follows the position actually played by the driver.
- `OnStop` is now also triggered when a non-looping file reaches its end.
- Connection errors are logged (with the list of available drivers) instead of thrown as exceptions.
- `AsioDriverName`, `TargetSampleRate` and `TargetBitsPerSample` can be set from code, and reconnect the driver when changed at runtime.
- Relative audio file paths are resolved from the project folder in the Editor, and from the player's folder in builds.
- Play, Pause and Stop buttons in the Inspector are only enabled in Play mode.

### Added

- `AsioAudioManager.RestartAll`: plays all sources from the start, together (also on the *Master* strip of the ASIO Mixer).
- `AsioAudioSource.FadeInDuration` and `FadeOutDuration`: fade durations of Play and of Pause / Stop, in seconds (Inspector: *Fade In* / *Fade Out*).
- `AsioOscRemote` component: control all sources, each source (by output channel), the master volume and snapshots with OSC messages (`/asio/play`, `/asio/source/3/volume -6`, `/asio/snapshot "Intro"`...).
- `OSC.MessageReceived` event: several components can listen to all incoming OSC messages.
- Mixer snapshots: save and recall the master volume and the volume / mute / solo of every source (`SaveSnapshot`, `RecallSnapshot`, `DeleteSnapshot`, `Snapshots`), from the manager's Inspector, the *Snapshots* menu of the ASIO Mixer, code or OSC.
- The driver's channel names (e.g. *ReaRoute 3*) in the output channel grid, the channel popups and `AsioAudioManager.AsioDriverOutputChannelNames` / `GetOutputChannelName`.
- *Preview* in the ASIO Audio Source inspector: listen to a source in Edit mode, without entering Play mode.
- Automated tests (*Window > General > Test Runner*, EditMode): mixer output level, no build-up between buffers, volumes, synchronised start and restart, decibel conversions.
- `link.xml`, so NAudio is kept whole when Unity strips code (IL2CPP, or a managed stripping level above Minimal).
- New in-game overlay: a compact, draggable window shown or hidden with a key (F9 by default), with the driver status and settings, and play / pause / stop, mute / solo, volume, level meter and seekable position for every source. Its size can be scaled for high-resolution screens (`OverlayToggleKey`, `OverlayScale`).
- OSC position senders (`SourcePositionOsc`, `RoomPositionOsc`) only send when the object moves, at most `MaxSendRate` times per second, with a periodic resend (`KeepAliveInterval`), instead of every frame.
- ASIO Mixer window (*AsioAudioUnity > ASIO Mixer*): one strip per source with output channel, level meter, volume fader, mute / solo / loop, transport controls and a seekable progress bar.
- New ASIO Audio Manager inspector: installed-driver dropdown, sample rate and format dropdowns, connection status, Reconnect and driver control panel buttons, and an overview of all sources' output channels with conflict detection and auto-fix.
- `AsioAudioSource.VolumeDecibels`: the volume in decibels, and `AsioVolume`: conversions between linear volume, decibels and fader positions.
- Master volume on the ASIO Audio Manager (`MasterVolume`, `MasterVolumeDecibels`), applied to all sources, with the level sent to the driver (`MasterMeterLevel`). It is shown in the manager's Inspector, as a *Master* strip in the ASIO Mixer and in the overlay.
- `AsioAudioManager.PlayAll`, `PauseAll` and `StopAll`, also available as Play / Pause / Stop buttons on the *Master* strip of the ASIO Mixer (first strip, with the number of sources playing).
- Waveform view in the ASIO Audio Source inspector: time ruler and amplitude scale (linear or dB), peak and RMS levels, playhead with seeking, zoom and scroll, follow playhead, amplitude zoom, and levels under the mouse.
- `AsioAudioSource.Mute`, `AsioAudioSource.Solo`, `AsioAudioSource.MeterLevel`.
- `AsioAudioSource.PreloadIntoMemory`: decode the whole file into memory instead of streaming it from disk while playing.
- `AsioAudioSource.Seek`, `AsioAudioSource.ResolvedAudioFilePath`.
- `AsioAudioManager.IsConnected`, `AsioAudioManager.Reconnect`, `AsioAudioManager.AsioDriverOutputChannelCount`.
- `AsioAudioManager.AsioDriverSampleFormat`: the driver's own sample format (e.g. 24-bit integer), also shown in the overlay, and in the Inspector (remembered from the last connection, so it is visible outside Play mode).
- Editor assembly definition (`AsioAudioUnity.Editor`).

### Removed

- `AsioAudioSource.SourceWaveProvider`, `SetSourceWaveProviderFromFileName`, `ConvertSamplesAndCreateNewAudioFile`, `DestroySourceWaveProvider`, `OriginalAudioFilePath`, `InternalStopwatch`.
- `AsioAudioManager.ConnectMixAndPlay`, `GlobalMultiplexingWaveProvider`.
- The converted `_48000_32.wav` copies of the demo sounds (no longer needed).

### Fixed

- OSC: the receiver thread was stopped with `Thread.Abort`, which is not supported on recent .NET runtimes. It now stops cleanly, and the sockets are no longer reopened while shutting down (which could leave the port in use for the next Play session).
- OSC: if the listening port is already in use, sending still works (and the warning is logged once, not for every message).
- OSC: the editor play-mode callback was never unregistered.
- 16-bit output failed to connect to drivers working in 24-bit integer or 32-bit float: 32-bit float is now sent instead, with a warning.

- Listeners set in the Inspector on `OnPlay`, `OnStop` and `OnPause` were erased when entering Play mode.
- The number of sources was checked against the driver's input channels instead of its output channels.
- Setting the sample rate from the Game window overlay had no effect.
- Audio files were never closed (leaked file handles).
- The post-build step closed the scenes open in the Editor (losing unsaved changes), and ignored inactive sources. It also read the scene list from the Build Settings instead of the scenes actually built (build profiles, `BuildPipeline.BuildPlayer` from a script), and failed when that list contained a deleted scene: audio files were then not copied.
- Dragging an audio file over the source's drop area (without dropping it) assigned it.
- Editor menu actions could not be undone.

## [1.0.0]

### Changed

- Complete rework of the package to become a GIT package (not a .unitypackage release).
- Functionalities stay the same as version 0.2.5.
