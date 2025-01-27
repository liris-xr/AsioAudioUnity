using UnityEngine;
using System.Collections;

public class RoomPositionOsc : ObjectPositionOsc 
{
    override public string OscObject => "room";

    [SerializeField] private int roomIndex;
    override public int Index => roomIndex;
}
