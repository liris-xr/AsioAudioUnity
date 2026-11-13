#if ASIO_TIMELINE // Only when the Timeline package is installed
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AsioAudioUnity
{
    /// <summary>
    /// A Timeline track that plays an ASIO Audio Source: each clip plays the source from the clip's start (and its "clip in" offset in the file)
    /// to its end. Clips of several tracks starting on the same frame start on the same sample. Pausing the Timeline pauses the source.
    /// Only in Play mode and in builds (the ASIO driver isn't used in Edit mode).
    /// </summary>
    [Serializable]
    [TrackColor(0.35f, 0.6f, 0.95f)]
    [TrackClipType(typeof(AsioAudioClip))]
    [TrackBindingType(typeof(AsioAudioSource))]
    public class AsioAudioTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<AsioAudioMixerBehaviour> mixer = ScriptPlayable<AsioAudioMixerBehaviour>.Create(graph, inputCount);
            AsioAudioMixerBehaviour behaviour = mixer.GetBehaviour();
            behaviour.Clips = GetClips().ToArray();
            behaviour.Director = go != null ? go.GetComponent<PlayableDirector>() : null;
            return mixer;
        }

        protected override void OnCreateClip(TimelineClip clip)
        {
            clip.displayName = "ASIO Audio";
        }
    }

    /// <summary>A clip of an <see cref="AsioAudioTrack"/>: the source plays during the clip, from the clip's "clip in" time in its file.</summary>
    [Serializable]
    public class AsioAudioClip : PlayableAsset, ITimelineClipAsset
    {
        public ClipCaps clipCaps => ClipCaps.ClipIn;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Playable.Create(graph);
    }

    /// <summary>Plays, seeks, pauses and stops the bound ASIO Audio Source as the Timeline enters, plays and leaves its clips.</summary>
    public class AsioAudioMixerBehaviour : PlayableBehaviour
    {
        // A jump of the Timeline's time larger than this (scrubbing, a jump to a marker...) moves the source's position.
        private const double JumpThreshold = 0.5;

        public TimelineClip[] Clips = new TimelineClip[0];
        public PlayableDirector Director;

        private AsioAudioSource _source;
        private int _activeClip = -1;
        private double _lastTime = -1;
        private bool _pausedByTimeline;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (!Application.isPlaying) return;
            AsioAudioSource source = playerData as AsioAudioSource;
            if (source == null) return;
            _source = source;

            double time = Director != null ? Director.time : playable.GetGraph().GetRootPlayable(0).GetTime();
            int active = -1;
            for (int i = 0; i < Clips.Length; i++)
            {
                if (time >= Clips[i].start && time < Clips[i].end) active = i;
            }

            if (active != _activeClip)
            {
                if (active < 0) source.Stop();
                else StartClip(source, active, time);
                _activeClip = active;
            }
            else if (active >= 0 && _lastTime >= 0 && Math.Abs(time - _lastTime) > JumpThreshold)
            {
                // The Timeline jumped within the clip: follow it
                source.Seek(PositionInFile(active, time));
            }
            _lastTime = time;
        }

        private void StartClip(AsioAudioSource source, int clip, double time)
        {
            source.Seek(PositionInFile(clip, time));
            AsioAudioManager manager = AsioAudioManager.Instance;
            if (manager != null) manager.PlayInSync(source);
            else source.Play();
            _pausedByTimeline = false;
        }

        private double PositionInFile(int clip, double time) => Clips[clip].clipIn + (time - Clips[clip].start);

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (!Application.isPlaying || _source == null || _activeClip < 0) return;

            if (Director != null && Director.state == PlayState.Paused)
            {
                // The Timeline is paused: pause the source, to resume it with the Timeline
                if (_source.AudioStatus == AsioAudioStatus.Playing) { _source.Pause(); _pausedByTimeline = true; }
            }
            else
            {
                // The Timeline stopped
                _source.Stop();
                _activeClip = -1;
                _lastTime = -1;
            }
        }

        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            if (!Application.isPlaying || _source == null || !_pausedByTimeline) return;
            _pausedByTimeline = false;
            AsioAudioManager manager = AsioAudioManager.Instance;
            if (manager != null) manager.PlayInSync(_source);
            else _source.Play();
        }

        public override void OnGraphStop(Playable playable)
        {
            if (Application.isPlaying && _source != null && _activeClip >= 0) _source.Stop();
            _activeClip = -1;
            _lastTime = -1;
            _pausedByTimeline = false;
        }
    }
}
#endif
