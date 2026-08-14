using System;
using System.Collections.Generic;

namespace HanziDefend.Data
{
    public enum FeedbackCue
    {
        Unknown,
        Deploy,
        Merge,
        Spawn,
        Hit,
        Victory,
        Defeat,
        Settlement
    }

    [Serializable]
    public sealed class FeedbackSynthesisDef
    {
        public float FrequencyHz { get; set; }

        public float DurationSeconds { get; set; }

        public float Amplitude { get; set; }

        public float AttackSeconds { get; set; }

        public float ReleaseSeconds { get; set; }
    }

    [Serializable]
    public sealed class FeedbackCueDef
    {
        public FeedbackCue Cue { get; set; }

        public string ClipId { get; set; } = string.Empty;

        public float Volume { get; set; }

        public float Pitch { get; set; }

        public FeedbackSynthesisDef Synthesis { get; set; } = new FeedbackSynthesisDef();
    }

    [Serializable]
    public sealed class FeedbackAudioDef
    {
        public int PoolSize { get; set; }

        public int SampleRateHz { get; set; }

        public float MasterVolume { get; set; }

        public FeedbackCueDef[] Cues { get; set; } = Array.Empty<FeedbackCueDef>();
    }

    [Serializable]
    public sealed class FeedbackHitStopDef
    {
        public bool Enabled { get; set; }

        public float DurationSeconds { get; set; }

        public float CooldownSeconds { get; set; }
    }

    [Serializable]
    public sealed class FeedbackShakeDef
    {
        public bool Enabled { get; set; }

        public float DurationSeconds { get; set; }

        public float CooldownSeconds { get; set; }

        public float Amplitude { get; set; }

        public float FrequencyHz { get; set; }

        public float VerticalFrequencyMultiplier { get; set; }

        public float PhaseStepRadians { get; set; }
    }

    /// <summary>Presentation-only tuning loaded independently from feedback.json.</summary>
    [Serializable]
    public sealed class FeedbackConfig
    {
        public const string FileName = "feedback.json";

        public int SchemaVersion { get; set; }

        public FeedbackAudioDef Audio { get; set; } = new FeedbackAudioDef();

        public FeedbackHitStopDef HitStop { get; set; } = new FeedbackHitStopDef();

        public FeedbackShakeDef Shake { get; set; } = new FeedbackShakeDef();

        public static FeedbackConfig Load(IConfigSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            try
            {
                FeedbackConfig config = JsonCodec.Deserialize<FeedbackConfig>(source.ReadText(FileName));
                config.Validate();
                return config;
            }
            catch (ConfigLoadException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ConfigLoadException(
                    $"Failed to load '{FileName}': {exception.Message}",
                    exception);
            }
        }

        public static FeedbackConfig Disabled()
        {
            return new FeedbackConfig();
        }

        public FeedbackCueDef FindCue(FeedbackCue cue)
        {
            FeedbackCueDef[] cues = Audio?.Cues ?? Array.Empty<FeedbackCueDef>();
            for (var index = 0; index < cues.Length; index++)
            {
                FeedbackCueDef value = cues[index];
                if (value != null && value.Cue == cue)
                {
                    return value;
                }
            }

            return null;
        }

        public void Validate()
        {
            Require(SchemaVersion == 1, "schemaVersion must be 1.");
            Require(Audio != null, "audio is required.");
            Require(Audio.PoolSize > 0, "audio.poolSize must be positive.");
            Require(Audio.SampleRateHz > 0, "audio.sampleRateHz must be positive.");
            RequireUnitInterval(Audio.MasterVolume, "audio.masterVolume");
            Require(Audio.Cues != null, "audio.cues must be an array.");

            var cues = new HashSet<FeedbackCue>();
            var clipIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (FeedbackCueDef cue in Audio.Cues)
            {
                Require(cue != null, "audio.cues contains a null entry.");
                Require(cue.Cue != FeedbackCue.Unknown, "audio.cues contains an unknown cue.");
                Require(cues.Add(cue.Cue), $"audio.cues repeats cue '{cue.Cue}'.");
                Require(!string.IsNullOrWhiteSpace(cue.ClipId), $"audio cue '{cue.Cue}' requires clipId.");
                Require(cue.ClipId.Trim().Equals(cue.ClipId, StringComparison.Ordinal),
                    $"audio cue '{cue.Cue}' clipId cannot have surrounding whitespace.");
                Require(clipIds.Add(cue.ClipId), $"audio.cues repeats clipId '{cue.ClipId}'.");
                RequireUnitInterval(cue.Volume, $"audio cue '{cue.Cue}' volume");
                RequireFinitePositive(cue.Pitch, $"audio cue '{cue.Cue}' pitch");
                ValidateSynthesis(cue);
            }

            foreach (FeedbackCue cue in Enum.GetValues(typeof(FeedbackCue)))
            {
                if (cue != FeedbackCue.Unknown)
                {
                    Require(cues.Contains(cue), $"audio.cues is missing '{cue}'.");
                }
            }

            Require(HitStop != null, "hitStop is required.");
            RequireFiniteNonNegative(HitStop.DurationSeconds, "hitStop.durationSeconds");
            RequireFiniteNonNegative(HitStop.CooldownSeconds, "hitStop.cooldownSeconds");
            if (HitStop.Enabled)
            {
                Require(HitStop.DurationSeconds > 0f,
                    "hitStop.durationSeconds must be positive when enabled.");
            }

            Require(Shake != null, "shake is required.");
            RequireFiniteNonNegative(Shake.DurationSeconds, "shake.durationSeconds");
            RequireFiniteNonNegative(Shake.CooldownSeconds, "shake.cooldownSeconds");
            RequireFiniteNonNegative(Shake.Amplitude, "shake.amplitude");
            RequireFiniteNonNegative(Shake.FrequencyHz, "shake.frequencyHz");
            RequireFiniteNonNegative(Shake.VerticalFrequencyMultiplier,
                "shake.verticalFrequencyMultiplier");
            RequireFinite(Shake.PhaseStepRadians, "shake.phaseStepRadians");
            if (Shake.Enabled)
            {
                Require(Shake.DurationSeconds > 0f, "shake.durationSeconds must be positive when enabled.");
                Require(Shake.Amplitude > 0f, "shake.amplitude must be positive when enabled.");
                Require(Shake.FrequencyHz > 0f, "shake.frequencyHz must be positive when enabled.");
                Require(Shake.VerticalFrequencyMultiplier > 0f,
                    "shake.verticalFrequencyMultiplier must be positive when enabled.");
            }
        }

        private static void ValidateSynthesis(FeedbackCueDef cue)
        {
            Require(cue.Synthesis != null, $"audio cue '{cue.Cue}' synthesis is required.");
            FeedbackSynthesisDef synthesis = cue.Synthesis;
            RequireFinitePositive(synthesis.FrequencyHz,
                $"audio cue '{cue.Cue}' synthesis.frequencyHz");
            RequireFinitePositive(synthesis.DurationSeconds,
                $"audio cue '{cue.Cue}' synthesis.durationSeconds");
            RequireUnitInterval(synthesis.Amplitude,
                $"audio cue '{cue.Cue}' synthesis.amplitude");
            Require(synthesis.Amplitude > 0f,
                $"audio cue '{cue.Cue}' synthesis.amplitude must be positive.");
            RequireFiniteNonNegative(synthesis.AttackSeconds,
                $"audio cue '{cue.Cue}' synthesis.attackSeconds");
            RequireFiniteNonNegative(synthesis.ReleaseSeconds,
                $"audio cue '{cue.Cue}' synthesis.releaseSeconds");
            Require(synthesis.AttackSeconds + synthesis.ReleaseSeconds <= synthesis.DurationSeconds,
                $"audio cue '{cue.Cue}' synthesis envelope exceeds duration.");
        }

        private static void RequireUnitInterval(float value, string context)
        {
            RequireFinite(value, context);
            Require(value >= 0f && value <= 1f, $"{context} must be in [0,1].");
        }

        private static void RequireFinitePositive(float value, string context)
        {
            RequireFinite(value, context);
            Require(value > 0f, $"{context} must be positive.");
        }

        private static void RequireFiniteNonNegative(float value, string context)
        {
            RequireFinite(value, context);
            Require(value >= 0f, $"{context} cannot be negative.");
        }

        private static void RequireFinite(float value, string context)
        {
            Require(!float.IsNaN(value) && !float.IsInfinity(value), $"{context} must be finite.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ConfigLoadException($"Invalid feedback configuration: {message}");
            }
        }
    }
}
