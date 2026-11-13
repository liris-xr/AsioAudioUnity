#if ASIO_TIMELINE // Only when the Timeline package is installed
using System;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace AsioAudioUnity
{
    /// <summary>
    /// A Timeline track of REAPER commands, bound to a REAPER Controller: its markers send their command to REAPER when the Timeline reaches them.
    /// </summary>
    [Serializable]
    [DisplayName("AsioAudioUnity/REAPER Command Track")]
    [TrackColor(0.85f, 0.45f, 0.2f)]
    [TrackBindingType(typeof(ReaperController))]
    public class ReaperCommandTrack : MarkerTrack
    {
    }

    /// <summary>
    /// A Timeline marker that sends a REAPER command when the Timeline reaches it: a command of the REAPER Controller's list, by name,
    /// or its own command. Put it on a REAPER Command track bound to the REAPER Controller.
    /// </summary>
    [Serializable]
    [DisplayName("AsioAudioUnity/REAPER Command")]
    public class ReaperCommandMarker : Marker, IReaperCommandNotification, INotificationOptionProvider
    {
        [Tooltip("The name of a command of the REAPER Controller's list. Empty: the command below is sent.")]
        [SerializeField] private string _commandName = "";

        [Tooltip("The command sent when no command name is set (its \"When\" is ignored).")]
        [SerializeField] private ReaperCommand _command = new ReaperCommand();

        [Tooltip("Also send the command when the Timeline starts after the marker (e.g. when jumping past it).")]
        [SerializeField] private bool _retroactive = false;

        public string CommandName { get => _commandName; set => _commandName = value; }
        public ReaperCommand Command { get => _command; set => _command = value; }

        public PropertyName id => new PropertyName("ReaperCommandMarker");

        public NotificationFlags flags => NotificationFlags.TriggerOnce | (_retroactive ? NotificationFlags.Retroactive : 0);
    }
}
#endif
