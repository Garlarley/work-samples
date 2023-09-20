namespace MySamples.Audio
{
    /// <summary>
    /// Manages background music using a stack-based system. 
    /// The top sheet on the stack plays, while underlying sheets fade out.
    /// </summary>
    public class MusicManager : Singleton<MusicManager>
    {
        /// <summary>
        /// Represents a runtime instance of a playing MusicSheet.
        /// Handles the sequencing of clips and specific fade logic for a single source.
        /// </summary>
        protected class ActiveMusicTrack
        {
            public MusicSheet Sheet { get; private set; }
            public AudioSource Source { get; private set; }
            public bool IsFinished { get; private set; }

            private int _currentClipIndex;
            private int _currentLoopCount;
            private float _timer;
            private float _fadeTimer;
            private bool _isPlaying;

            public ActiveMusicTrack(MusicSheet sheet, AudioSource source)
            {
                Sheet = sheet;
                Source = source;
                
                // Apply mixer group if present
                if (sheet.outputGroup != null) source.outputAudioMixerGroup = sheet.outputGroup;
                
                _currentClipIndex = 0;
                _currentLoopCount = 0;
                _timer = 0;
                _fadeTimer = 0;
                IsFinished = false;
                
                // Initialize first clip
                if (Sheet.clips.Length > 0)
                {
                    Source.clip = Sheet.clips[0].clip;
                    Source.volume = 0;
                }
            }

            /// <summary>
            /// Updates the state of the track: volume fading, clip sequencing, and delays.
            /// </summary>
            public void Tick(float deltaTime, bool isTopTrack, float masterVolume)
            {
                if (IsFinished) return;

                _timer += deltaTime;

                if (_timer < Sheet.startDelay) return;

                if (!_isPlaying)
                {
                    Source.Play();
                    _isPlaying = true;
                }

                HandleFading(deltaTime, isTopTrack, masterVolume);
                HandleSequencing();
            }

            /// <summary>
            /// Stops the audio source immediately.
            /// </summary>
            public void Stop()
            {
                if (Source != null) Source.Stop();
                IsFinished = true;
            }

            private void HandleFading(float deltaTime, bool isTopTrack, float masterVolume)
            {
                float targetTime = isTopTrack ? Sheet.fadeInTime : Sheet.fadeOutTime;
                
                if (targetTime <= 0)
                {
                    _fadeTimer = isTopTrack ? 1 : 0;
                }
                else
                {
                    float step = deltaTime / targetTime;
                    _fadeTimer += isTopTrack ? step : -step;
                }

                _fadeTimer = Mathf.Clamp01(_fadeTimer);

                float curveVal = Sheet.volumeCurve.Evaluate(_fadeTimer);
                Source.volume = curveVal * Sheet.targetVolume * masterVolume;

                if (!isTopTrack && _fadeTimer <= 0)
                {
                    Stop();
                }
            }

            private void HandleSequencing()
            {
                if (Source.isPlaying) return;
                
                var currentClipData = Sheet.clips[_currentClipIndex];

                // Handle Looping logic (-1 is infinite, or check count)
                if (currentClipData.loopCount == -1 || _currentLoopCount < currentClipData.loopCount)
                {
                    _currentLoopCount++;
                    Source.Play();
                }
                else
                {
                    _currentClipIndex++;
                    
                    if (_currentClipIndex >= Sheet.clips.Length)
                    {
                        Stop();
                    }
                    else
                    {
                        // Play next clip in sequence
                        _currentLoopCount = 0;
                        Source.clip = Sheet.clips[_currentClipIndex].clip;
                        Source.Play();
                    }
                }
            }
        }

        private struct VolumeOverride
        {
            public int sourceId;
            public float volume;
        }

        private List<AudioSource> _sourcePool = new List<AudioSource>();
        private List<ActiveMusicTrack> _musicStack = new List<ActiveMusicTrack>();
        private List<VolumeOverride> _volumeOverrides = new List<VolumeOverride>();

        public static System.Func<float> OnRequestMasterVolume; 

        /// <summary>
        /// Gets the calculated volume including all overrides and global settings.
        /// </summary>
        public float CurrentControlledVolume
        {
            get
            {
                float baseVol = (_volumeOverrides.Count > 0) ? _volumeOverrides[_volumeOverrides.Count - 1].volume : 1f;
                float settingsVol = OnRequestMasterVolume != null ? OnRequestMasterVolume.Invoke() : 1f;
                return baseVol * settingsVol;
            }
        }

        /// <summary>
        /// Pushes a music sheet onto the stack. This will become the playing music, 
        /// causing the previous music to fade out.
        /// </summary>
        public static void Play(MusicSheet sheet)
        {
            if (Instance) Instance.PushSheet(sheet);
        }

        /// <summary>
        /// Removes a specific sheet from the stack, causing it to fade out if it was active.
        /// </summary>
        public static void Stop(MusicSheet sheet)
        {
            if (Instance) Instance.RemoveSheet(sheet);
        }

        /// <summary>
        /// Sets a temporary volume override.
        /// </summary>
        public static void SetVolumeOverride(GameObject source, float volume)
        {
            if (Instance == null) return;
            
            // Remove existing override from this source to prevent stacking
            RemoveVolumeOverride(source);
            
            Instance._volumeOverrides.Add(new VolumeOverride 
            { 
                sourceId = source.GetInstanceID(), 
                volume = volume 
            });
        }

        /// <summary>
        /// Removes a volume override associated with a specific object.
        /// </summary>
        public static void RemoveVolumeOverride(GameObject source)
        {
            if (Instance == null) return;
            int id = source.GetInstanceID();
            for (int i = Instance._volumeOverrides.Count - 1; i >= 0; i--)
            {
                if (Instance._volumeOverrides[i].sourceId == id)
                {
                    Instance._volumeOverrides.RemoveAt(i);
                }
            }
        }

        private void PushSheet(MusicSheet sheet)
        {
            if (sheet == null || sheet.clips.Length == 0) return;

            // Check if this sheet is already at the top of the stack
            if (_musicStack.Count > 0 && _musicStack[_musicStack.Count - 1].Sheet == sheet)
            {
                return; 
            }

            // Check if this sheet exists lower in the stack, remove it so we can move it to top
            for (int i = _musicStack.Count - 1; i >= 0; i--)
            {
                if (_musicStack[i].Sheet == sheet)
                {
                    _musicStack[i].Stop(); 
                    _musicStack.RemoveAt(i);
                }
            }

            var source = GetAvailableSource();
            var newTrack = new ActiveMusicTrack(sheet, source);
            _musicStack.Add(newTrack);
        }

        private void RemoveSheet(MusicSheet sheet)
        {            
            for (int i = _musicStack.Count - 1; i >= 0; i--)
            {
                if (_musicStack[i].Sheet == sheet)
                {
                    // If we remove it abruptly, it cuts. 
                    _musicStack[i].Stop();
                    _musicStack.RemoveAt(i);
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            float masterVol = CurrentControlledVolume;

            for (int i = _musicStack.Count - 1; i >= 0; i--)
            {
                var track = _musicStack[i];
                
                bool isTop = (i == _musicStack.Count - 1);

                track.Tick(dt, isTop, masterVol);

                if (track.IsFinished)
                {
                    _musicStack.RemoveAt(i);
                }
            }
        }

        private AudioSource GetAvailableSource()
        {
            foreach (var src in _sourcePool)
            {
                if (!src.isPlaying) return src;
            }

            // Create new if none available
            GameObject go = new GameObject($"MusicSource_{_sourcePool.Count}");
            go.transform.SetParent(transform);
            var newSource = go.AddComponent<AudioSource>();
            newSource.playOnAwake = false;
            newSource.loop = false;
            
            _sourcePool.Add(newSource);
            return newSource;
        }
    }
}