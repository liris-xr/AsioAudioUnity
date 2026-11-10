# 8. Control Unity over OSC

[Documentation home](/Documentation~/README.md) · Previous: [7. Control REAPER from Unity](/Documentation~/Tutorials/7-ControlReaper.md)

The **ASIO OSC Remote** lets another application control Unity's sounds with OSC messages: play, stop, volumes, mute, solo, snapshots. For example from TouchOSC on a tablet, from REAPER, Max, Pure Data or a lighting desk.

## Receive OSC in Unity

1. *GameObject > AsioAudioUnity > OSC Manager*. It receives the OSC messages: set its **In Port** (8101, for example) to the port the other application sends to.
2. *GameObject > AsioAudioUnity > ASIO OSC Remote*.

## Send messages

From the other application, send messages to the IP address of the computer running Unity, on the OSC Manager's port. Sources are addressed by their **output channel**:

| **Message** | **Effect** |
|-|-|
| `/asio/play` | Plays all sources, together. |
| `/asio/stop` | Stops all sources. |
| `/asio/master/volume -6` | Sets the master volume to -6 dB. |
| `/asio/snapshot "Intro"` | Recalls the snapshot named *Intro*. |
| `/asio/source/3/play` | Plays the source on output channel 3. |
| `/asio/source/3/volume -12` | Sets its volume to -12 dB. |
| `/asio/source/3/mute 1` | Mutes it. |

Tick **Log Messages** on the remote to see what it receives in the Console. The full list of messages is in [AsioOscRemote](/Documentation~/Reference/AsioOscRemote.md).
