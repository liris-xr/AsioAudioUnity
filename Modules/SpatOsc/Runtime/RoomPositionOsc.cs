using System;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>Obsolete: replaced by the <see cref="SpatController"/> (add the room to its Other Objects, as a Room).</summary>
    [Obsolete("Replaced by the Spat Controller, which sends the positions of every ASIO Audio Source and of rooms from a single component. Use its Inspector button to replace this component.")]
    [AddComponentMenu("")] // Hidden from Add Component: existing components keep working
    public class RoomPositionOsc : ObjectPositionOsc
    {
        override public string OscObject => "room";

        [SerializeField] private int _roomIndex;
        override public int Index
        {
            get { return _roomIndex; }
            set { _roomIndex = value; }
        }
    }
}
