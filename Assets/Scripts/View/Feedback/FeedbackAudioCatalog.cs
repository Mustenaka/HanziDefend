using System;
using UnityEngine;

namespace HanziDefend.View.Feedback
{
    [CreateAssetMenu(
        menuName = "HanziDefend/Feedback Audio Catalog",
        fileName = "HanziDefendFeedbackAudioCatalog")]
    public sealed class FeedbackAudioCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string ClipId = string.Empty;
            public AudioClip Clip;
        }

        public Entry[] Entries = Array.Empty<Entry>();

        public bool TryGetClip(string clipId, out AudioClip clip)
        {
            if (!string.IsNullOrEmpty(clipId))
            {
                Entry[] entries = Entries ?? Array.Empty<Entry>();
                for (var index = 0; index < entries.Length; index++)
                {
                    Entry entry = entries[index];
                    if (entry != null
                        && string.Equals(entry.ClipId, clipId, StringComparison.Ordinal)
                        && entry.Clip != null)
                    {
                        clip = entry.Clip;
                        return true;
                    }
                }
            }

            clip = null;
            return false;
        }
    }
}
