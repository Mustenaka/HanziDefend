using System;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.View.Feedback
{
    public interface IFeedbackAudioClipSource
    {
        bool TryGetClip(string clipId, out AudioClip clip);
    }

    public sealed class FeedbackAudioCatalogSource : IFeedbackAudioClipSource
    {
        private readonly FeedbackAudioCatalog catalog;

        public FeedbackAudioCatalogSource(FeedbackAudioCatalog audioCatalog)
        {
            catalog = audioCatalog;
        }

        public bool TryGetClip(string clipId, out AudioClip clip)
        {
            if (catalog != null)
            {
                return catalog.TryGetClip(clipId, out clip);
            }

            clip = null;
            return false;
        }
    }

    public sealed class EmptyFeedbackAudioClipSource : IFeedbackAudioClipSource
    {
        public static EmptyFeedbackAudioClipSource Instance { get; } = new EmptyFeedbackAudioClipSource();

        private EmptyFeedbackAudioClipSource()
        {
        }

        public bool TryGetClip(string clipId, out AudioClip clip)
        {
            clip = null;
            return false;
        }
    }

    /// <summary>Single Resources boundary for all E2 runtime assets.</summary>
    public static class FeedbackRuntimeAssets
    {
        public const string ConfigResourceName = "HanziDefendGameConfig";
        public const string AudioCatalogResourceName = "HanziDefendFeedbackAudioCatalog";

        public static FeedbackConfig LoadConfigOrDisabled()
        {
            GameConfigTextBundle bundle = Resources.Load<GameConfigTextBundle>(ConfigResourceName);
            if (bundle == null || bundle.Feedback == null)
            {
                return FeedbackConfig.Disabled();
            }

            return FeedbackConfig.Load(bundle.CreateSource());
        }

        public static IFeedbackAudioClipSource LoadClipSourceOrEmpty()
        {
            FeedbackAudioCatalog catalog =
                Resources.Load<FeedbackAudioCatalog>(AudioCatalogResourceName);
            if (catalog == null)
            {
                return EmptyFeedbackAudioClipSource.Instance;
            }
            return new FeedbackAudioCatalogSource(catalog);
        }
    }

    [DisallowMultipleComponent]
    public sealed class AudioSourcePool : MonoBehaviour
    {
        private AudioSource[] sources = Array.Empty<AudioSource>();
        private float[] cueVolumes = Array.Empty<float>();
        private int nextSourceIndex;
        private float masterVolume;

        public int Capacity => sources.Length;

        public int CreatedSourceCount => sources.Length;

        public float MasterVolume => masterVolume;

        public int ActiveSourceCount
        {
            get
            {
                var count = 0;
                for (var index = 0; index < sources.Length; index++)
                {
                    if (sources[index] != null && sources[index].isPlaying)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public void Initialize(int capacity, float initialMasterVolume)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            if (sources.Length != 0)
            {
                throw new InvalidOperationException("AudioSourcePool has already been initialized.");
            }

            sources = new AudioSource[capacity];
            cueVolumes = new float[capacity];
            for (var index = 0; index < sources.Length; index++)
            {
                AudioSource source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                sources[index] = source;
            }

            SetMasterVolume(initialMasterVolume);
        }

        public bool TryPlay(AudioClip clip, float cueVolume, float pitch)
        {
            if (clip == null || sources.Length == 0)
            {
                return false;
            }

            int selectedIndex = FindAvailableSource();
            AudioSource source = sources[selectedIndex];
            if (source.isPlaying)
            {
                source.Stop();
            }

            cueVolumes[selectedIndex] = Mathf.Clamp01(cueVolume);
            source.clip = clip;
            source.volume = cueVolumes[selectedIndex] * masterVolume;
            source.pitch = pitch;
            source.Play();
            nextSourceIndex = (selectedIndex + 1) % sources.Length;
            return true;
        }

        public void SetMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            for (var index = 0; index < sources.Length; index++)
            {
                if (sources[index] != null)
                {
                    sources[index].volume = cueVolumes[index] * masterVolume;
                }
            }
        }

        public float GetSourceVolume(int index)
        {
            if (index < 0 || index >= sources.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return sources[index].volume;
        }

        private int FindAvailableSource()
        {
            for (var offset = 0; offset < sources.Length; offset++)
            {
                int index = (nextSourceIndex + offset) % sources.Length;
                if (!sources[index].isPlaying)
                {
                    return index;
                }
            }

            return nextSourceIndex;
        }
    }
}
