using UnityEngine;
using System.Collections;

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
