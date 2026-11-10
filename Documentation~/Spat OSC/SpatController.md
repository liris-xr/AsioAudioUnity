## SpatController

> Part of the **Spat OSC plugin** (`Modules/SpatOsc`, assembly `AsioAudioUnity.SpatOsc`), which uses the shared OSC library (`Runtime/Osc`).

The `SpatController` component sends the positions of the sound sources (and rooms) to **Spat Revolution** over OSC, from a single place:

- **every [AsioAudioSource](/Documentation~/Audio%20Components/AsioAudioSource.md) is tracked automatically**: its number in Spat Revolution is its **output channel**, i.e. the ReaRoute input / REAPER track it arrives on, so Unity, REAPER and Spat stay numbered the same way,
- **other objects** can be added: rooms, or sources that aren't ASIO Audio Sources,
- positions are sent when objects move (at most *Max Send Rate* times per second), re-sent every *Keep Alive Interval* seconds, and converted from Unity's axes (Y up) to Spat's (Z up), relative to an optional **Origin**.

It sends directly to Spat Revolution's address and port: **no OSC Manager, and no component on each source**, are needed.

### Setup

#### In Spat Revolution

*Preferences > OSC Main*: enable OSC, and add an OSC connection `input | Spat Revolution - Plugins` on the port of the Spat Controller (8100 by default), with the IP address of the computer running Unity (127.0.0.1 if it is the same computer). See [Setup OSC Environment](/Documentation~/SetupOscEnvironment.md).

#### In Unity

1. *GameObject > AsioAudioUnity > Spat Controller* (or add the component to any GameObject).
2. Check the **Address** and **Port** of Spat Revolution.
3. Keep **Track All ASIO Sources** ticked, and add rooms in **Other Objects** (type *Room*, with their number in Spat).
4. Optionally set an **Origin** (e.g. the listener or the room's center) and a **Position Scale**.

The **Sent to Spat** table lists what is tracked, with the position sent, and **Send Positions Now** sends them all (also in Edit mode, to check the setup).

#### Spat source numbers

By default, each ASIO Audio Source is sent as the Spat source numbered like its **output channel**. In the *Sent to Spat* table, each source can be given **any other Spat number** (*Spat #*), or **not be sent** (*Send* unticked); the reset button goes back to the output channel. Two sources with the same Spat number show a warning. From code: `SetSpatIndex(source, number)`, `GetSpatIndex(source)`, and the `SourceMappings` list.

> If sources still have an obsolete [SourcePositionOsc](/Documentation~/Spat%20OSC/SourcePositionOsc.md) component (the per-object workflow from before version 3.0), their positions are sent twice: the Inspector warns about it and can remove those components.

### Messages

| **Address** | **Values** |
|-|-|
| `/source/[number]/xyz` | x, y, z (Spat's axes: Unity's x, z, y) |
| `/room/[number]/xyz` | x, y, z |

The addresses can be changed in the **OSC Addresses** foldout (`{0}` is the number).

### Public Properties

| **Property** | **Description** |
|-|-|
| `SpatAddress`, `SpatPort` | Where Spat Revolution listens (default: 127.0.0.1:8100). |
| `TrackAllSources` | Sends the position of every ASIO Audio Source, as the Spat source numbered like its output channel (default: on). |
| `SourceMappings` | ASIO Audio Sources sent with another Spat number (`SpatSourceMapping`: `Source`, `SpatIndex`, `Send`), or not sent. Sources without an entry use their output channel. |
| `TrackedObjects` | Other objects (`SpatTrackedObject`: `Target`, `Type` Source or Room, `Index`). An entry with the same type and number as an ASIO source replaces it. |
| `Origin`, `PositionScale` | Positions are sent relative to the origin (its position, rotation and scale), and multiplied by the scale. |
| `MaxSendRate` | Maximum number of messages per second, per object (default: 60; 0 for no limit). |
| `KeepAliveInterval` | Positions are re-sent every this many seconds even if nothing moves (default: 1; 0 to only send on movement). |
| `MovementThreshold` | Smallest movement that sends a new position (default: 0.001). |
| `ResetPositionsOnQuit` | Sends (0, 0, 0) for every tracked object when the application quits or Play mode ends (default: off). |
| `Addresses` | The OSC addresses of Spat Revolution. |
| `LogMessages` | Logs every message sent. |

### Public Methods

| **Method** | **Description** |
|-|-|
| `SendAllPositions()` | Sends the position of everything tracked now. |
| `GetSpatIndex(source)`, `SetSpatIndex(source, number)`, `GetMapping(source)` | The Spat number of an ASIO Audio Source (its mapping, or its output channel), and how to change it. |
| `SendPosition(type, number, spatPosition)` | Sends a position, in Spat's coordinates. |
| `ToSpatPosition(worldPosition)` | Converts a Unity position to Spat's coordinates (origin, scale, axes). |
| `GetTrackedPositions()` | What is tracked now. |
| `Send(address, values...)` | Sends any OSC message to Spat Revolution. |

### Script Example

```cs
using UnityEngine;
using AsioAudioUnity;

public class AddRoom : MonoBehaviour
{
    [SerializeField] private SpatController _spat;
    [SerializeField] private Transform _room;

    private void Start()
    {
        _spat.TrackedObjects.Add(new SpatTrackedObject { Target = _room, Type = SpatObjectType.Room, Index = 1 });
    }
}
```
