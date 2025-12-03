## RoomPositionOsc

The `RoomPositionOsc` component is a specialized OSC (Open Sound Control) object designed to represent and synchronize the position of a "room" entity over OSC messages. It inherits from `ObjectPositionOsc`.

### Typical Workflow and Features

1. **OSC Object Identification:**  
   The component overrides the `OscObject` property to return `"room"`, ensuring that all OSC messages sent or received by this component are associated with a room entity.

2. **Room Indexing:**  
   The `Index` property is overridden to use a private serialized field `_roomIndex`. This allows each `RoomPositionOsc` instance to be uniquely identified and addressed via its index in OSC communications.

3. **Integration:**  
   Designed to be used in conjunction with other OSC components, such as `ObjectPositionOsc`, to provide a flexible and extensible system for spatial audio or interactive environments.

#### Properties Setup

To use `RoomPositionOsc`, attach it to a GameObject representing a room in your Unity scene. Set the `_roomIndex` field in the Inspector to uniquely identify the room for OSC communication.

> **Note:** The `OscObject` property is fixed to `"room"` and cannot be changed.

#### Script Example

```cs
using UnityEngine;
using AsioAudioUnity;

public class SetupRoomOsc : MonoBehaviour
{
    private void Awake()
    {
        RoomPositionOsc roomOsc = gameObject.AddComponent<RoomPositionOsc>();
        // Set the room index (e.g., 1 for the first room)
        roomOsc.Index = 1;
        // The OscObject property will automatically be "room"
    }
}
```

#### Public Properties

| **Property** | **Description** |
|-|-|
| `OscObject` | Returns the OSC object identifier, always `"room"` (Read Only). |
| `Index` | The unique index of the room for OSC addressing. |

