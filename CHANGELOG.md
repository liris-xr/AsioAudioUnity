# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

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

- ASIO Mixer window (*AsioAudioUnity > ASIO Mixer*): one strip per source with output channel, level meter, volume fader, mute / solo / loop, transport controls and a seekable progress bar.
- New ASIO Audio Manager inspector: installed-driver dropdown, sample rate and format dropdowns, connection status, Reconnect and driver control panel buttons, and an overview of all sources' output channels with conflict detection and auto-fix.
- New Custom ASIO Audio Source inspector: file browser, file length and format, status and seekable progress bar, output channel dropdown showing used channels, level meter, mute / solo.
- `CustomAsioAudioSource.Mute`, `CustomAsioAudioSource.Solo`, `CustomAsioAudioSource.MeterLevel`.
- `CustomAsioAudioSource.PreloadIntoMemory`: decode the whole file into memory instead of streaming it from disk while playing.
- `CustomAsioAudioSource.Seek`, `CustomAsioAudioSource.ResolvedAudioFilePath`.
- `AsioAudioManager.IsConnected`, `AsioAudioManager.Reconnect`, `AsioAudioManager.AsioDriverOutputChannelCount`.
- Editor assembly definition (`AsioAudioUnity.Editor`).

### Removed

- `CustomAsioAudioSource.SourceWaveProvider`, `SetSourceWaveProviderFromFileName`, `ConvertSamplesAndCreateNewAudioFile`, `DestroySourceWaveProvider`, `OriginalAudioFilePath`, `InternalStopwatch`.
- `AsioAudioManager.ConnectMixAndPlay`, `GlobalMultiplexingWaveProvider`.
- The converted `_48000_32.wav` copies of the demo sounds (no longer needed).

### Fixed

- Listeners set in the Inspector on `OnPlay`, `OnStop` and `OnPause` were erased when entering Play mode.
- The number of sources was checked against the driver's input channels instead of its output channels.
- Setting the sample rate from the Game window overlay had no effect.
- Audio files were never closed (leaked file handles).
- The post-build step closed the scenes open in the Editor (losing unsaved changes), and ignored inactive sources.
- Dragging an audio file over the source's drop area (without dropping it) assigned it.
- Editor menu actions could not be undone.

## [1.0.0]

### Changed

- Complete rework of the package to become a GIT package (not a .unitypackage release).
- Functionalities stay the same as version 0.2.5.
