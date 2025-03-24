using UnityEngine;
using System.Collections;

public class SourcePositionOsc : ObjectPositionOsc
{
    override public string OscObject => "source";

    [SerializeField] private int _sourceIndex;
    override public int Index
    {
        get { return _sourceIndex; }
        set { _sourceIndex = value; }
    }
}
