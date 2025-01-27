using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class ObjectPositionOsc : MonoBehaviour
{
    [SerializeField] public OSC _osc;
    public OSC Osc 
    { 
        get { return _osc; }
        set { _osc = value; }
    }

    public abstract string OscObject { get; }

    public abstract int Index { get; }

    public void SendPositionOsc()
    {
        OscMessage message = new OscMessage();
        message.address = "/" + OscObject + "/" + Index + "/xyz";
        message.values.Add(transform.position.x);
        message.values.Add(transform.position.z);
        message.values.Add(transform.position.y);
        Osc.Send(message);
    }

    public void SendResetPositionOsc()
    {
        OscMessage message = new OscMessage();
        message.address = "/" + OscObject + "/" + Index + "/xyz";
        message.values.Add(0);
        message.values.Add(0);
        message.values.Add(0);
        Osc.Send(message);
    }

    void Update()
    {
        SendPositionOsc();
    }
}
