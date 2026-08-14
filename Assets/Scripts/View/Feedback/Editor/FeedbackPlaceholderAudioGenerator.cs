using System;
using System.IO;
using System.Linq;
using HanziDefend.Data;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.View.Feedback.Editor
{
    public static class FeedbackPlaceholderAudioGenerator
    {
        public const string AudioDirectory = "Assets/Audio/Placeholder";
        public const string CatalogAssetPath =
            "Assets/Resources/HanziDefendFeedbackAudioCatalog.asset";

        private const short ChannelCount = 1;
        private const short BitsPerSample = 16;

        [MenuItem("HanziDefend/Feedback/Regenerate Placeholder Audio")]
        public static void RegenerateFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Feedback placeholder audio cannot be regenerated in Play Mode.");
                return;
            }

            FeedbackConfig config = FeedbackConfig.Load(new JsonConfigSource());
            EnsureAssetFolder(AudioDirectory);
            EnsureAssetFolder("Assets/Resources");

            foreach (FeedbackCueDef cue in config.Audio.Cues)
            {
                string assetPath = $"{AudioDirectory}/{cue.ClipId}.wav";
                byte[] bytes = BuildWave(cue.Synthesis, config.Audio.SampleRateHz);
                WriteIfChanged(assetPath, bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            }

            // This catalog is generated output. Recreate it so a previous interrupted
            // import cannot leave a missing-script shell that still looks loadable.
            AssetDatabase.DeleteAsset(CatalogAssetPath);
            FeedbackAudioCatalog catalog =
                ScriptableObject.CreateInstance<FeedbackAudioCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogAssetPath);

            catalog.Entries = config.Audio.Cues
                .Select(cue => new FeedbackAudioCatalog.Entry
                {
                    ClipId = cue.ClipId,
                    Clip = AssetDatabase.LoadAssetAtPath<AudioClip>(
                        $"{AudioDirectory}/{cue.ClipId}.wav")
                })
                .ToArray();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            Debug.Log(
                $"Generated {catalog.Entries.Length} feedback placeholder clips in '{AudioDirectory}'.");
        }

        internal static byte[] BuildWave(FeedbackSynthesisDef synthesis, int sampleRateHz)
        {
            if (synthesis == null)
            {
                throw new ArgumentNullException(nameof(synthesis));
            }
            if (sampleRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            }

            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(synthesis.DurationSeconds * sampleRateHz));
            int bytesPerSample = BitsPerSample / 8;
            int dataByteCount = checked(sampleCount * ChannelCount * bytesPerSample);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                WriteAscii(writer, "RIFF");
                writer.Write(36 + dataByteCount);
                WriteAscii(writer, "WAVE");
                WriteAscii(writer, "fmt ");
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(ChannelCount);
                writer.Write(sampleRateHz);
                writer.Write(sampleRateHz * ChannelCount * bytesPerSample);
                writer.Write((short)(ChannelCount * bytesPerSample));
                writer.Write(BitsPerSample);
                WriteAscii(writer, "data");
                writer.Write(dataByteCount);

                for (var index = 0; index < sampleCount; index++)
                {
                    float time = index / (float)sampleRateHz;
                    float envelope = CalculateEnvelope(time, synthesis);
                    float sample = Mathf.Sin(time * synthesis.FrequencyHz * Mathf.PI * 2f)
                                   * synthesis.Amplitude
                                   * envelope;
                    writer.Write((short)Mathf.RoundToInt(sample * short.MaxValue));
                }
            }

            return stream.ToArray();
        }

        private static float CalculateEnvelope(float time, FeedbackSynthesisDef synthesis)
        {
            float attack = synthesis.AttackSeconds <= 0f
                ? 1f
                : Mathf.Clamp01(time / synthesis.AttackSeconds);
            float remaining = synthesis.DurationSeconds - time;
            float release = synthesis.ReleaseSeconds <= 0f
                ? 1f
                : Mathf.Clamp01(remaining / synthesis.ReleaseSeconds);
            return Mathf.Min(attack, release);
        }

        private static void EnsureAssetFolder(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            int slash = normalized.LastIndexOf('/');
            if (slash <= 0)
            {
                throw new InvalidOperationException($"Invalid asset folder '{assetPath}'.");
            }
            string parent = normalized.Substring(0, slash);
            string name = normalized.Substring(slash + 1);
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void WriteIfChanged(string assetPath, byte[] bytes)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (File.Exists(fullPath) && File.ReadAllBytes(fullPath).SequenceEqual(bytes))
            {
                return;
            }
            File.WriteAllBytes(fullPath, bytes);
        }

        private static void WriteAscii(BinaryWriter writer, string value)
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(value));
        }
    }
}
