using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HanziDefend.Data;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor.Balance
{
    /// <summary>
    /// The five minor stages' wave specs, and the command that turns them into
    /// <c>Assets/GameData/waves.json</c>.
    ///
    /// <para>This table <b>is</b> the tuning surface for enemy volume. Nothing in waves.json is
    /// hand-authored: change a DPS reading or a pressure coefficient here, re-run
    /// <c>HanziDefend/Balance/Regenerate Waves</c>, and the whole five-stage schedule follows.
    /// The derivation and the measurements behind <see cref="MeasuredAllyDps"/> are recorded in
    /// Docs/Plan/DECISIONS.md under WO-F1.</para>
    /// </summary>
    public static class WaveSetSpecTable
    {
        /// <summary>WO-F1 §A.2, unchanged: act one probes, act two presses, act three decides.</summary>
        public const float FirstActPressure = 0.55f;
        public const float SecondActPressure = 0.85f;
        public const float ThirdActPressure = 1.05f;

        /// <summary>
        /// Act lengths. WO-F1 §A.1's first-version numbers were 32 / 38 / >=42, which put the last
        /// scheduled spawn at 126s once the two 6s pauses are counted — and the acceptance band for
        /// a whole battle is 90-150s. That left roughly twenty seconds for the entire castle fight,
        /// so measured runs sat at 153-155s with timeouts. Trimmed acts one and two; act three keeps
        /// its floor of 42s because it is the act that has to decide the battle.
        /// </summary>
        public const float FirstActSeconds = 26f;
        public const float SecondActSeconds = 32f;
        public const float ThirdActSeconds = 42f;

        /// <summary>The readable silence between two acts — the "one wave just ended" beat.</summary>
        public const float InterActGapSeconds = 6f;

        /// <summary>
        /// Longest silence allowed inside an act. Comfortably under
        /// <see cref="InterActGapSeconds"/> so the two never read as the same thing.
        /// </summary>
        public const float MaximumSpawnGapSeconds = 3f;

        /// <summary>
        /// Effective ally DPS per stage, measured by <see cref="BalanceDpsProbe"/> against the
        /// stage's reference lineup and weighted by the armour mix that stage actually fields.
        /// These are readings, not estimates — see DECISIONS.md for the run they came from.
        /// </summary>
        private static readonly float[] MeasuredAllyDps = { 250f, 380f, 335f, 390f, 400f };

        /// <summary>
        /// Share of the unarmored slot given to <c>e_lang</c>, the only unit that ignores the front
        /// line and runs at the camp. Rising with the stage is what makes camp hit points bite late.
        /// </summary>
        private static readonly float[] RushShare = { 0.15f, 0.20f, 0.25f, 0.30f, 0.35f };

        /// <summary>
        /// Per-stage difficulty ramp. See <see cref="WaveSetSpec.StageDifficultyScalar"/> for why
        /// the DPS-derived budget needs one at all; the values here are the ones measured to land
        /// stage one and stage five inside their own win-rate bands.
        /// </summary>
        private static float[] stageDifficultyScalar = { 1.25f, 1.30f, 1.35f, 1.40f, 1.45f };

        public static IReadOnlyList<float> StageDifficultyScalars => stageDifficultyScalar;

        private static readonly string[] WaveSetIds =
        {
            "main_20", "main_20_stage_2", "main_20_stage_3", "main_20_stage_4", "main_20_stage_5"
        };

        public static IReadOnlyList<WaveSetSpec> All => BuildSpecs(MeasuredAllyDps);

        /// <summary>
        /// The spec set for an arbitrary DPS curve. Deriving the enemy budget from measured DPS
        /// makes the two mutually dependent — a heavier schedule kills the board sooner, which
        /// lowers the DPS it lands — so the tuning loop feeds a fresh reading back in and re-derives
        /// until the number stops moving. This overload is that loop's entry point.
        /// </summary>
        public static IReadOnlyList<WaveSetSpec> BuildSpecs(IReadOnlyList<float> allyDpsPerStage)
        {
            if (allyDpsPerStage == null)
            {
                throw new ArgumentNullException(nameof(allyDpsPerStage));
            }

            var specs = new WaveSetSpec[WaveSetIds.Length];
            for (int index = 0; index < specs.Length; index++)
            {
                specs[index] = Create(index + 1, allyDpsPerStage);
            }

            return Array.AsReadOnly(specs);
        }

        public static WaveSetSpec Create(int stageIndex)
        {
            return Create(stageIndex, MeasuredAllyDps);
        }

        public static WaveSetSpec Create(int stageIndex, IReadOnlyList<float> allyDpsPerStage)
        {
            int slot = Mathf.Clamp(stageIndex - 1, 0, WaveSetIds.Length - 1);
            return new WaveSetSpec(
                WaveSetIds[slot],
                stageIndex,
                allyDpsPerStage[Mathf.Clamp(slot, 0, allyDpsPerStage.Count - 1)],
                RushShare[slot],
                stageDifficultyScalar[slot],
                new[]
                {
                    // M1-04 §6 armour mix, mapped one act per phase. Squads grow from 2-4 through
                    // 4-6 to the act-three stream, exactly as WO-F1 §A.1 describes.
                    new WaveActSpec(
                        WaveActs.First, FirstActSeconds, 0f, FirstActPressure,
                        0.70f, 0.30f, 0.00f, 4, 0.55f, MaximumSpawnGapSeconds),
                    new WaveActSpec(
                        WaveActs.Second, SecondActSeconds, InterActGapSeconds, SecondActPressure,
                        0.45f, 0.40f, 0.15f, 6, 0.45f, MaximumSpawnGapSeconds),
                    new WaveActSpec(
                        WaveActs.Third, ThirdActSeconds, InterActGapSeconds, ThirdActPressure,
                        0.35f, 0.35f, 0.30f, 6, 0.40f, MaximumSpawnGapSeconds)
                });
        }

        [MenuItem("HanziDefend/Balance/Regenerate Waves")]
        public static void RegenerateFromMenu()
        {
            string path = Path.Combine(
                Directory.GetCurrentDirectory(),
                "Assets",
                "GameData",
                "waves.json");
            string trace = Regenerate(path);
            AssetDatabase.Refresh();
            Debug.Log("Regenerated waves.json from WaveSetSpecTable:\n" + trace);
        }

        /// <summary>
        /// Rebuilds waves.json in place and returns the derivation trace. The config is loaded
        /// before the rewrite, so a generator bug cannot leave a half-written data file behind.
        /// </summary>
        public static string Regenerate(string wavesJsonPath)
        {
            return Regenerate(wavesJsonPath, MeasuredAllyDps);
        }

        /// <summary>Tuning-loop hook: swaps the difficulty ramp before a regeneration.</summary>
        public static void SetStageDifficultyScalars(float[] values)
        {
            if (values == null || values.Length != stageDifficultyScalar.Length)
            {
                throw new ArgumentException(
                    "One difficulty scalar per minor stage is required.", nameof(values));
            }

            stageDifficultyScalar = values;
        }

        public static string Regenerate(string wavesJsonPath, IReadOnlyList<float> allyDpsPerStage)
        {
            // Deliberately units.json alone, not GameConfig: every act-structure change makes the
            // current waves.json invalid, and the generator is what fixes that. It must not need
            // the broken file to load first.
            var units = new JsonConfigSource().Load<UnitCatalog>("units.json");
            IReadOnlyList<WaveSetSpec> specs = BuildSpecs(allyDpsPerStage);
            var sets = new WaveSetDef[specs.Count];
            for (int index = 0; index < specs.Count; index++)
            {
                sets[index] = WaveSetGenerator.Generate(units, specs[index]);
            }

            File.WriteAllText(
                wavesJsonPath,
                JsonCodec.Serialize(new WaveCatalog { WaveSets = sets }) + Environment.NewLine,
                new UTF8Encoding(false));

            GameConfig config = GameConfig.Load();
            var trace = new StringBuilder();
            for (int index = 0; index < specs.Count; index++)
            {
                trace.Append(WaveSetGenerator.DescribeBudget(config, specs[index]));
            }

            return trace.ToString();
        }
    }
}
