#nullable enable

namespace Pose.Core.Voice
{
    /// <summary>
    /// What the OS has said about the microphone. Three states, not a bool,
    /// because "not asked yet" and "refused" need opposite handling: the first
    /// prompts on first use, the second must not re-prompt (Android and iOS both
    /// stop showing the dialog after a refusal) and instead points at settings.
    /// </summary>
    public enum MicPermissionState
    {
        /// <summary>Not asked yet. Voice stays available; the prompt comes at first use.</summary>
        Unknown = 0,

        /// <summary>The player allowed microphone access.</summary>
        Granted = 1,

        /// <summary>The player refused. Only they can undo this, in system settings.</summary>
        Denied = 2,
    }
}
