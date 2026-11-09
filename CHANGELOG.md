# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
