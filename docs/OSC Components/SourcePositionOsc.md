## SourcePositionOsc

The `SourcePositionOsc` component is a specialized OSC (Open Sound Control) object designed to represent and synchronize the position of a "source" entity over OSC messages. It inherits from `ObjectPositionOsc`.

### Typical Workflow and Features

1. **OSC Object Identification:**  
   The component overrides the `OscObject` property to return `"source"`, ensuring that all OSC messages sent or received by this component are associated with a source entity.

2. **Room Indexing:**  
   The `Index` property is overridden to use a private serialized field `_sourceIndex`. This allows each `SourcePositionOsc` instance to be uniquely identified and addressed via its index in OSC communications.

3. **Integration:**  
   Designed to be used in conjunction with other OSC components, such as `ObjectPositionOsc`, to provide a flexible and extensible system for spatial audio or interactive environments.

#### Properties Setup

To use `SourcePositionOsc`, attach it to a GameObject representing a source in your Unity scene. Set the `_sourceIndex` field in the Inspector to uniquely identify the source for OSC communication.

> **Note:** The `OscObject` property is fixed to `"source"` and cannot be changed.

#### Script Example

```cs
using UnityEngine;
using AsioAudioUnity;

public class SetupSourceOsc : MonoBehaviour
{
    private void Awake()
    {
        SourcePositionOsc sourceOsc = gameObject.AddComponent<SourcePositionOsc>();
        // Set the source index (e.g., 1 for the first source)
        sourceOsc.Index = 1;
        // The OscObject property will automatically be "source"
    }
}
```

#### Public Properties

| **Property** | **Description** |
|-|-|
| `OscObject` | Returns the OSC object identifier, always `"source"` (Read Only). |
| `Index` | The unique index of the source for OSC addressing. |
