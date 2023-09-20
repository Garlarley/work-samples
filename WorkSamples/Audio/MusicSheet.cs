namespace MySamples.Audio
{
    [System.Serializable]
    public struct MusicClip
    {
        [Tooltip("The audio clip to play.")]
        public AudioClip clip;
        
        [Tooltip("0: Play once, -1: Loop infinitely, N: Loop N times.")]
        public int loopCount;
    }

    /// <summary>
    /// Configuration asset defining a sequence of music clips and transition settings.
    /// This object is stateless and only contains configuration data.
    /// </summary>
    [CreateAssetMenu(fileName = "MusicSheet", menuName = "MD/Music/MusicSheet")]
    public class MusicSheet : ScriptableObject
    {
        [Header("Audio Configuration")]
        [Tooltip("Optional Audio Mixer Group for routing.")]
        public AudioMixerGroup outputGroup;

        [Tooltip("The sequence of clips to play for this music theme.")]
        public MusicClip[] clips;

        [Header("Transition Settings")]
        [Tooltip("Time in seconds to fade this music in.")]
        [Min(0f)]
        public float fadeInTime = 2f;

        [Tooltip("Time in seconds to fade this music out.")]
        [Min(0f)]
        public float fadeOutTime = 3f;

        [Tooltip("Delay in seconds before the audio actually begins playing after being queued.")]
        [Min(0f)]
        public float startDelay = 0f;

        [Tooltip("Curve used for fading volume. Default is linear.")]
        public AnimationCurve volumeCurve = AnimationCurve.Linear(0, 0, 1, 1);
        
        [Header("Volume Adjustments")]
        [Range(0f, 1f)]
        public float targetVolume = 1f;
    }
}