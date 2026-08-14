using System.Collections.Generic;
using HanziDefend.View;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor
{
    /// <summary>
    /// One-shot remote review capture. It observes presentation milestones only and delegates
    /// every image to the same 1080x1920 screenshot implementation used by the public menu.
    /// </summary>
    [InitializeOnLoad]
    internal static class BattleReviewCaptureCoordinator
    {
        private const string MenuPath = "HanziDefend/Capture Battle Review";
        private const string ActiveSessionKey = "HanziDefend.WOB4Capture.Active";
        private const string CapturedMaskSessionKey = "HanziDefend.WOB4Capture.CapturedMask";
        private const string PauseOwnedSessionKey = "HanziDefend.WOB4Capture.PauseOwned";
        private const string OriginalPauseSessionKey = "HanziDefend.WOB4Capture.OriginalPause";
        private const int RequiredStageCount = 4;
        private static readonly HashSet<BattleReviewStage> CapturedStages =
            new HashSet<BattleReviewStage>();
        private static bool active;
        private static bool captureInProgress;
        private static BattleReviewStage pendingStage;
        private static int pendingEditorUpdates;
        private static BattlePresentationBootstrap pausedBootstrap;
        private static bool resumeAutoRun;
        private static bool resumePending;
        private static int resumeEditorUpdates;
        private static bool editorPauseOwned;
        private static bool originalEditorPause;

        static BattleReviewCaptureCoordinator()
        {
            active = SessionState.GetBool(ActiveSessionKey, false);
            RestoreCapturedStages();
            captureInProgress = false;
            pendingStage = BattleReviewStage.None;
            pendingEditorUpdates = 0;
            pausedBootstrap = null;
            resumeAutoRun = false;
            resumePending = false;
            resumeEditorUpdates = 0;
            editorPauseOwned = SessionState.GetBool(PauseOwnedSessionKey, false);
            originalEditorPause = SessionState.GetBool(OriginalPauseSessionKey, false);
            HoldEditorPause();
            EditorApplication.update += Update;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem(MenuPath)]
        private static void StartCapture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Exit Play Mode before starting a WO-B4 review capture session.");
                return;
            }

            CapturedStages.Clear();
            active = true;
            captureInProgress = false;
            pendingStage = BattleReviewStage.None;
            pendingEditorUpdates = 0;
            pausedBootstrap = null;
            resumeAutoRun = false;
            resumePending = false;
            resumeEditorUpdates = 0;
            AcquireEditorPause();
            PersistSession();
            Debug.Log("[WO-B4 Capture] Session started; entering Play Mode.");
            try
            {
                EditorApplication.EnterPlaymode();
            }
            catch
            {
                active = false;
                PersistSession();
                RestoreEditorPause();
                throw;
            }
        }

        private static void Update()
        {
            if (captureInProgress || !EditorApplication.isPlaying)
            {
                return;
            }

            if (resumePending)
            {
                // Camera.Render and PNG encoding are synchronous. Keep AutoRun frozen for a full
                // subsequent Editor update so their long delta is observed only by the paused
                // presentation frame and cannot contaminate the resumed FPS sample.
                if (resumeEditorUpdates++ == 0)
                {
                    return;
                }

                ResumeBootstrap();
                RestoreEditorPause();
                return;
            }

            if (!active)
            {
                return;
            }

            if (!CapturedStages.Contains(BattleReviewStage.Opening))
            {
                HoldEditorPause();
            }

            if (!CapturedStages.Contains(BattleReviewStage.Opening) &&
                pendingStage == BattleReviewStage.None &&
                TryPrimeOpeningCapture())
            {
                return;
            }

            if (pendingStage != BattleReviewStage.None)
            {
                // The first update freezes the presentation at the milestone. Capture only from
                // a later Editor update so transforms, HUD text, and Canvas geometry have all
                // projected the event state before Camera.Render is invoked.
                if (pendingEditorUpdates++ == 0)
                {
                    return;
                }

                CapturePendingStage();
                return;
            }

            BattleView view = Object.FindFirstObjectByType<BattleView>();
            if (view == null)
            {
                return;
            }

            if (!view.TryDequeueReviewStage(out BattleReviewStage stage))
            {
                return;
            }

            if (stage == BattleReviewStage.None || CapturedStages.Contains(stage))
            {
                return;
            }

            ScheduleCapture(stage);
        }

        private static void CapturePendingStage()
        {
            captureInProgress = true;
            BattleReviewStage stage = pendingStage;
            bool captureSucceeded = false;
            try
            {
                BattleView view = Object.FindFirstObjectByType<BattleView>();
                ValidateStageWaveIndex(stage, view == null ? -1 : view.HudState.WaveIndex);
                if (stage == BattleReviewStage.Opening)
                {
                    // Commander passive visuals are emitted during initialization. Advance only
                    // BattleView's transient presentation clock so the opening evidence shows the
                    // stable faction palette without ticking BattleSystem or changing WAVE 0.
                    view.AdvancePresentation(0.25f);
                    ValidateStageWaveIndex(stage, view.HudState.WaveIndex);
                }

                string assetPath = ScreenshotTool.CaptureForReview(stage);
                CapturedStages.Add(stage);
                view?.AcknowledgeReviewStage(stage);
                PersistSession();
                Debug.Log($"[WO-B4 Capture] {stage}: {assetPath}");
                captureSucceeded = true;
            }
            catch
            {
                if (stage == BattleReviewStage.Opening)
                {
                    active = false;
                    PersistSession();
                }

                throw;
            }
            finally
            {
                captureInProgress = false;
                pendingStage = BattleReviewStage.None;
                pendingEditorUpdates = 0;
                if (captureSucceeded && pausedBootstrap != null)
                {
                    resumePending = true;
                    resumeEditorUpdates = 0;
                }
                else if (!captureSucceeded)
                {
                    ResumeBootstrap();
                    RestoreEditorPause();
                }
            }

            if (stage == BattleReviewStage.Settlement &&
                CapturedStages.Count == RequiredStageCount)
            {
                active = false;
                PersistSession();
                Debug.Log("[WO-B4 Capture] Four-stage review capture completed; " +
                          "exit Play Mode to import the staged PNG files into Assets/Screenshots.");
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                if (active)
                {
                    HoldEditorPause();
                    TryPrimeOpeningCapture();
                }

                return;
            }

            if (state != PlayModeStateChange.ExitingPlayMode &&
                state != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }

            bool endedEarly = active && CapturedStages.Count < RequiredStageCount;
            ResumeBootstrap();
            RestoreEditorPause();

            active = false;
            captureInProgress = false;
            pendingStage = BattleReviewStage.None;
            pendingEditorUpdates = 0;
            resumePending = false;
            resumeEditorUpdates = 0;
            CapturedStages.Clear();
            PersistSession();
            if (endedEarly && state == PlayModeStateChange.ExitingPlayMode)
            {
                Debug.LogWarning("[WO-B4 Capture] Session ended before all four stages were captured.");
            }
        }

        private static bool TryPrimeOpeningCapture()
        {
            BattlePresentationBootstrap bootstrap =
                Object.FindFirstObjectByType<BattlePresentationBootstrap>();
            PauseBootstrap(bootstrap);

            BattleView view = Object.FindFirstObjectByType<BattleView>();
            if (view == null)
            {
                return false;
            }

            if (view.TryDequeueReviewStage(out BattleReviewStage stage))
            {
                if (stage == BattleReviewStage.Opening)
                {
                    ScheduleCapture(stage);
                    return true;
                }

                Debug.LogWarning($"[WO-B4 Capture] Expected Opening first, received {stage}.");
                return false;
            }

            if (view.CurrentReviewStage == BattleReviewStage.Opening)
            {
                ScheduleCapture(BattleReviewStage.Opening);
                return true;
            }

            return false;
        }

        private static void ValidateStageWaveIndex(BattleReviewStage stage, int waveIndex)
        {
            if (stage == BattleReviewStage.Opening && waveIndex != 0)
            {
                throw new System.InvalidOperationException(
                    $"[WO-B4 Capture] Refusing to save Opening at WAVE {waveIndex}; " +
                    "Opening evidence must be captured before the first battle tick (WAVE 0).");
            }
        }

        private static void ScheduleCapture(BattleReviewStage stage)
        {
            pendingStage = stage;
            pendingEditorUpdates = 0;
            PauseBootstrap(Object.FindFirstObjectByType<BattlePresentationBootstrap>());
        }

        private static void PauseBootstrap(BattlePresentationBootstrap bootstrap)
        {
            if (bootstrap == null || pausedBootstrap == bootstrap)
            {
                return;
            }

            ResumeBootstrap();
            pausedBootstrap = bootstrap;
            resumeAutoRun = bootstrap.AutoRun;
            bootstrap.AutoRun = false;
        }

        private static void ResumeBootstrap()
        {
            if (pausedBootstrap != null)
            {
                pausedBootstrap.AutoRun = resumeAutoRun;
            }

            pausedBootstrap = null;
            resumeAutoRun = false;
            resumePending = false;
            resumeEditorUpdates = 0;
        }

        private static void AcquireEditorPause()
        {
            if (!editorPauseOwned)
            {
                originalEditorPause = EditorApplication.isPaused;
                editorPauseOwned = true;
                PersistPauseState();
            }

            HoldEditorPause();
        }

        private static void HoldEditorPause()
        {
            if (editorPauseOwned && !EditorApplication.isPaused)
            {
                EditorApplication.isPaused = true;
            }
        }

        private static void RestoreEditorPause()
        {
            if (!editorPauseOwned)
            {
                return;
            }

            EditorApplication.isPaused = originalEditorPause;
            editorPauseOwned = false;
            originalEditorPause = false;
            PersistPauseState();
        }

        private static void PersistPauseState()
        {
            SessionState.SetBool(PauseOwnedSessionKey, editorPauseOwned);
            SessionState.SetBool(OriginalPauseSessionKey, originalEditorPause);
        }

        private static void RestoreCapturedStages()
        {
            CapturedStages.Clear();
            int mask = SessionState.GetInt(CapturedMaskSessionKey, 0);
            foreach (BattleReviewStage stage in System.Enum.GetValues(typeof(BattleReviewStage)))
            {
                if (stage != BattleReviewStage.None && (mask & StageBit(stage)) != 0)
                {
                    CapturedStages.Add(stage);
                }
            }
        }

        private static void PersistSession()
        {
            int mask = 0;
            foreach (BattleReviewStage stage in CapturedStages)
            {
                mask |= StageBit(stage);
            }

            SessionState.SetBool(ActiveSessionKey, active);
            SessionState.SetInt(CapturedMaskSessionKey, mask);
        }

        private static int StageBit(BattleReviewStage stage)
        {
            return 1 << (int)stage;
        }
    }
}
