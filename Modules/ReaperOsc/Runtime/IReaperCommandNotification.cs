using UnityEngine.Playables;

namespace AsioAudioUnity
{
    /// <summary>
    /// A notification (e.g. a Timeline marker) that makes the REAPER Controller send a command: the command of its list named
    /// <see cref="CommandName"/>, or else <see cref="Command"/>. Implemented by the REAPER Command marker (when the Timeline package is installed).
    /// </summary>
    public interface IReaperCommandNotification : INotification
    {
        /// <summary>The name of a command of the REAPER Controller's list, or empty to send <see cref="Command"/>.</summary>
        string CommandName { get; }

        /// <summary>The command to send when <see cref="CommandName"/> is empty (its "When" is ignored).</summary>
        ReaperCommand Command { get; }
    }
}
