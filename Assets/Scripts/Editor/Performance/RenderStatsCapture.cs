using HanziDefend.Data;
using HanziDefend.Gameplay.Battle;
using HanziDefend.View;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor.Performance
{
    public static class RenderStatsCapture
    {
        private const int ReviewUnitCountPerTeam = 100;
        private const uint ReviewSeed = 0xE3200200u;

        private static GameObject reviewRoot;
        private static BattleSystem reviewSystem;

        [MenuItem("HanziDefend/Performance/Build 200 Unit Render Rig", true)]
        private static bool ValidateBuildTwoHundredUnitRenderRig()
        {
            return EditorApplication.isPlaying
                   && !EditorApplication.isPaused
                   && !EditorApplication.isCompiling;
        }

        [MenuItem("HanziDefend/Performance/Build 200 Unit Render Rig")]
        public static void BuildTwoHundredUnitRenderRig()
        {
            DisposeReviewRig();

            M1GameBootstrap[] bootstraps = Object.FindObjectsByType<M1GameBootstrap>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int index = 0; index < bootstraps.Length; index++)
            {
                bootstraps[index].gameObject.SetActive(false);
            }

            GameConfig config = GameConfig.Load();
            reviewRoot = new GameObject("[WO-E3] 200 Unit Render Rig");
            BattleView view = reviewRoot.AddComponent<BattleView>();
            view.Initialize(config, new ResourcesBattleArtSource(), null);
            reviewSystem = BattleSystem.CreateEncounter(
                config,
                "level_1_1",
                ReviewSeed,
                view,
                simulatePhysics: false);
            view.BindSystem(reviewSystem);
            SpawnReviewUnits(reviewSystem, config);
            view.SyncFromBoundSystem();
            view.AdvancePresentation(1f);

            Debug.Log(
                $"[WO-E3] 200-unit render rig ready: active entity views="
                + $"{view.ActiveVisualCount}, created entity views={view.CreatedVisualCount}. "
                + "Capture render stats after at least one rendered frame.");
        }

        [MenuItem("HanziDefend/Performance/Capture Current Render Stats", true)]
        private static bool ValidateCaptureCurrentRenderStats()
        {
            return EditorApplication.isPlaying
                   && !EditorApplication.isPaused
                   && !EditorApplication.isCompiling;
        }

        [MenuItem("HanziDefend/Performance/Capture Current Render Stats")]
        public static void CaptureCurrentRenderStats()
        {
            int batches = UnityStats.batches;
            int setPassCalls = UnityStats.setPassCalls;
            Debug.Log(
                $"[WO-E3] Render stats: batches/draw calls={batches}, "
                + $"set-pass={setPassCalls}, triangles={UnityStats.triangles}, "
                + $"vertices={UnityStats.vertices}. "
                   + "Gate: batches/draw calls < 50 in the 200-unit review setup.");
        }

        private static void SpawnReviewUnits(BattleSystem system, GameConfig config)
        {
            const int columns = 10;
            const float xSpacing = 0.82f;
            const float ySpacing = 0.74f;
            float left = -(columns - 1) * xSpacing * 0.5f;

            for (int index = 0; index < ReviewUnitCountPerTeam; index++)
            {
                int column = index % columns;
                int row = index / columns;
                float x = left + column * xSpacing;
                // Formation rows intentionally share art, matching normal deployment and allowing
                // Unity's sprite batching to be measured without adversarial texture interleaving.
                UnitDef ally = config.AllyUnits[row % config.AllyUnits.Count];
                UnitDef enemy = config.EnemyUnits[row % config.EnemyUnits.Count];
                system.Spawn(new UnitSpawnRequest(
                    ally.Id,
                    1,
                    new Vector2(x, -0.5f - row * ySpacing)));
                system.Spawn(new UnitSpawnRequest(
                    enemy.Id,
                    1,
                    new Vector2(x, 0.5f + row * ySpacing)));
            }
        }

        private static void DisposeReviewRig()
        {
            reviewSystem?.Dispose();
            reviewSystem = null;
            if (reviewRoot != null)
            {
                Object.Destroy(reviewRoot);
                reviewRoot = null;
            }
        }
    }
}
