using UnityEngine;
using System.Collections;

public class SourcePositionOsc : ObjectPositionOsc
{
    override public string OscObject => "source";

    [SerializeField] private int sourceIndex;
    override public int Index => sourceIndex;
}
