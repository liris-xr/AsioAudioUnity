using System;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>Obsolete: replaced by the <see cref="SpatController"/>, which tracks every ASIO Audio Source automatically.</summary>
    [Obsolete("Replaced by the Spat Controller, which sends the positions of every ASIO Audio Source and of rooms from a single component. Use its Inspector button to replace this component. Will be removed in version 4.0.")]
    [AddComponentMenu("")] // Hidden from Add Component: existing components keep working
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
}
