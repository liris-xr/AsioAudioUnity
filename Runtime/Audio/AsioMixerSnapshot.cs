using System;
using System.Collections.Generic;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// A saved state of the mix: the master volume, and the volume, mute and solo of each source.
    /// Snapshots are stored on the ASIO Audio Manager (see AsioAudioManager.SaveSnapshot and RecallSnapshot).
    /// </summary>
    [Serializable]
    public class AsioMixerSnapshot
    {
        [Serializable]
        public class SourceSettings
        {
            public AsioAudioSource Source;
            [Range(0f, 1f)] public float Volume = 1f;
            public bool Mute;
            public bool Solo;
        }

        public string Name = "Snapshot";
        [Range(0f, 1f)] public float MasterVolume = 1f;
        public List<SourceSettings> Sources = new List<SourceSettings>();
    }
}
