# Legacy position components (obsolete)

[Documentation home](/Documentation~/README.md)

> **Obsolete since version 3.0.** Replaced by the [Spat Controller](/Documentation~/Reference/SpatController.md), which tracks every ASIO Audio Source automatically, and rooms, from one component, without an OSC Manager.

Before version 3.0, positions were sent to Spat Revolution by a component on each object, through the scene's [OSC Manager](/Documentation~/Reference/AsioOscRemote.md#osc-manager):

- **SourcePositionOsc** sends `/source/[Index]/xyz`,
- **RoomPositionOsc** sends `/room/[Index]/xyz`,
- both inherit from **ObjectPositionOsc**, and convert Unity's axes (Y up) to Spat's (Z up).

Existing components still work, but they are hidden from *Add Component* and their menu items are removed.

## Replacing them

Their Inspector shows a warning with a **Replace with Spat Controller** button. It uses the scene's Spat Controller (or creates one) and removes the component:

- a **RoomPositionOsc** becomes one of the Spat Controller's *Other Objects*, as a *Room* with the same number,
- a **SourcePositionOsc** on an ASIO Audio Source is tracked automatically: its number is kept as the source's *Spat #* if it differs from the output channel,
- a **SourcePositionOsc** on another object becomes one of the *Other Objects*, as a *Source*.

Then remove the OSC Manager if nothing else uses it (the [ASIO OSC Remote](/Documentation~/Reference/AsioOscRemote.md) still needs one).

Don't use both for the same object: its position would be sent twice. The Spat Controller warns about it, and can remove the old components.

## Properties

| **Property** | **Description** |
|-|-|
| `Index` | The source or room number in Spat Revolution. |
| `Osc` | The OSC Manager used to send messages (found in the scene if empty). |
| `MaxSendRate` | Maximum number of messages per second (default: 60; 0: no limit). |
| `KeepAliveInterval` | The position is re-sent every this many seconds even if the object doesn't move (default: 1; 0: only on movement). |
| `MovementThreshold` | Smallest movement that sends the position (default: 0.001). |
