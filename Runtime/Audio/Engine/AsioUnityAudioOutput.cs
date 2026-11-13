using System;
using UnityEngine;

namespace AsioAudioUnity
{
    /// <summary>
    /// Plays the ASIO Audio Manager's mix through Unity's own audio output, when no ASIO driver can be used (fallback):
    /// all output channels are mixed together, on every speaker of Unity's output. Lives on a hidden child of the manager,
    /// with a silent looping AudioSource whose audio filter is replaced by the mix.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class AsioUnityAudioOutput : MonoBehaviour
    {
        private volatile AsioMixer _mixer;
        private float[] _mixBuffer = new float[0];

        /// <summary>Creates the output under the given parent, playing the mixer's channels through Unity's audio.</summary>
        public static AsioUnityAudioOutput Create(Transform parent, AsioMixer mixer)
        {
            GameObject outputObject = new GameObject("ASIO Audio (Unity audio fallback)") { hideFlags = HideFlags.HideAndDontSave };
            outputObject.transform.SetParent(parent, false);

            // A silent looping clip keeps the AudioSource running, so Unity calls OnAudioFilterRead on every audio buffer.
            AudioSource audioSource = outputObject.AddComponent<AudioSource>();
            audioSource.clip = AudioClip.Create("Silence", AudioSettings.outputSampleRate, 1, AudioSettings.outputSampleRate, false);
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.bypassListenerEffects = true;
            audioSource.bypassReverbZones = true;

            AsioUnityAudioOutput output = outputObject.AddComponent<AsioUnityAudioOutput>();
            output._mixer = mixer;
            audioSource.Play();
            return output;
        }

        /// <summary>Stops the output and destroys it.</summary>
        public void Close()
        {
            _mixer = null;
            if (Application.isPlaying) Destroy(gameObject);
            else DestroyImmediate(gameObject);
        }

        // [Audio thread] Replaces the silent clip's samples with the mix: every output channel of the mixer, summed, on every speaker.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            AsioMixer mixer = _mixer;
            if (mixer == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            int mixChannels = mixer.WaveFormat.Channels;
            int frames = data.Length / channels;
            int needed = frames * mixChannels;
            if (_mixBuffer.Length < needed) _mixBuffer = new float[needed];
            mixer.Read(_mixBuffer, 0, needed);

            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0f;
                int mixFrame = frame * mixChannels;
                for (int c = 0; c < mixChannels; c++) sum += _mixBuffer[mixFrame + c];
                int dataFrame = frame * channels;
                for (int c = 0; c < channels; c++) data[dataFrame + c] = sum;
            }
        }
    }
}
