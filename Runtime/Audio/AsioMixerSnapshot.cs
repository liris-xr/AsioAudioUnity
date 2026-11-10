using System;
using System.Collections.Generic;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// A saved state of the mix: the master volume, and the volume, mute and solo of each source, identified by its output channel
    /// (unique among sources, and the same in every scene). Snapshots are stored in the project's ASIO Audio Settings.
    /// </summary>
    [Serializable]
    public class AsioMixerSnapshot
    {
        [Serializable]
        public class SourceSettings
        {
            [Tooltip("The output channel of the source these settings apply to.")]
            public int OutputChannel;
            [Range(0f, 1f)] public float Volume = 1f;
            public bool Mute;
            public bool Solo;

            /// <summary>
            /// Only used to migrate snapshots saved on a scene's ASIO Audio Manager (before version 3.0), which referenced sources directly.
            /// </summary>
            [HideInInspector] public AsioAudioSource Source;
        }

        public string Name = "Snapshot";
        [Range(0f, 1f)] public float MasterVolume = 1f;
        public List<SourceSettings> Sources = new List<SourceSettings>();
    }
}
