using System;
using System.Collections.Generic;
using HanziDefend.Data;
using HanziDefend.Gameplay;
using HanziDefend.Gameplay.Reward;
using UnityEngine;
using UnityEngine.UI;

namespace HanziDefend.View
{
    /// <summary>Modal reward/victory/defeat projection. Every terminal state owns a visible exit.</summary>
    [DisallowMultipleComponent]
    public sealed class RewardScreen : MonoBehaviour
    {
        private readonly List<GameObject> generated = new List<GameObject>();
        private GameFlow flow;
        private GameConfig config;
        private IBattleArtSource artSource;
        private Font font;
        private RectTransform cardRow;
        private Text title;
        private Text status;
        private CanvasGroup canvasGroup;
        private float transitionTime;

        public int ExitButtonCount { get; private set; }

        public int RenderedCardCount { get; private set; }

        public void Show(GameFlow owner, GameConfig gameConfig, IBattleArtSource source, GameFlowPhase phase)
        {
            flow = owner ?? throw new ArgumentNullException(nameof(owner));
            config = gameConfig ?? throw new ArgumentNullException(nameof(gameConfig));
            artSource = source ?? throw new ArgumentNullException(nameof(source));
            font = font ?? RuntimeUiFactory.LoadFont();
            ClearGenerated();
            BuildShell();

            switch (phase)
            {
                case GameFlowPhase.Reward:
                    BuildReward();
                    break;
                case GameFlowPhase.MajorVictory:
                    BuildTerminal("大关通关", "五战告捷 · 本大关状态将在继续后重置", flow.CompleteMajorVictoryAndRestart);
                    break;
                case GameFlowPhase.Defeat:
                    BuildTerminal("营地失守", "本大关已结束，可立即重开", flow.RestartMajorStage);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(phase));
            }
        }

        private void Update()
        {
            if (canvasGroup == null || canvasGroup.alpha >= 1f)
            {
                return;
            }
            transitionTime += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Clamp01(transitionTime / 0.18f);
            transform.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, canvasGroup.alpha);
        }

        private void BuildShell()
        {
            RectTransform root = GetComponent<RectTransform>();
            if (root == null)
            {
                root = gameObject.AddComponent<RectTransform>();
            }
            RuntimeUiFactory.Stretch(root);
            Image scrim = GetComponent<Image>();
            if (scrim == null)
            {
                scrim = gameObject.AddComponent<Image>();
            }
            scrim.color = new Color(0.01f, 0.015f, 0.025f, 0.72f);
            scrim.raycastTarget = true;
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }
            canvasGroup.alpha = 0f;
            transitionTime = 0f;
            transform.localScale = Vector3.one * 0.94f;

            RectTransform modal = RuntimeUiFactory.CreatePanel(
                "Reward Modal", root, new Color(0.07f, 0.09f, 0.13f, 0.98f));
            RuntimeUiFactory.SetAnchors(modal, new Vector2(0.06f, 0.2f), new Vector2(0.94f, 0.82f));
            generated.Add(modal.gameObject);
            title = RuntimeUiFactory.CreateText("Title", modal, font, 48, FontStyle.Bold, TextAnchor.MiddleCenter);
            RuntimeUiFactory.SetAnchors(title.rectTransform, new Vector2(0.05f, 0.83f), new Vector2(0.95f, 0.97f));
            status = RuntimeUiFactory.CreateText("Status", modal, font, 25, FontStyle.Normal, TextAnchor.MiddleCenter);
            RuntimeUiFactory.SetAnchors(status.rectTransform, new Vector2(0.05f, 0.06f), new Vector2(0.95f, 0.16f));

            var rowObject = new GameObject("Reward Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            cardRow = rowObject.GetComponent<RectTransform>();
            cardRow.SetParent(modal, false);
            RuntimeUiFactory.SetAnchors(cardRow, new Vector2(0.04f, 0.2f), new Vector2(0.96f, 0.81f));
            HorizontalLayoutGroup layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 18f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlHeight = true;
            layout.childControlWidth = false;
            layout.childForceExpandHeight = true;
            layout.childForceExpandWidth = false;
            generated.Add(rowObject);
            ExitButtonCount = 0;
            RenderedCardCount = 0;
        }

        private void BuildReward()
        {
            title.text = "战斗胜利 · 三选一";
            status.text = $"小关 {flow.StageNumber}/{flow.StageCount} · 每张可免费重随一次";
            SettlementOffer offer = flow.Reward.CurrentOffer;
            for (int index = 0; index < offer.Cards.Count; index++)
            {
                CreateRewardCard(index, offer.Cards[index]);
            }
        }

        private void CreateRewardCard(int slotIndex, SettlementRewardCard card)
        {
            Image panel = RuntimeUiFactory.CreateImage(
                $"Reward Card {slotIndex}", cardRow, new Color(0.15f, 0.25f, 0.4f, 1f));
            LayoutElement layout = panel.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 270f;

            Image icon = RuntimeUiFactory.CreateImage("Icon", panel.transform, Color.white);
            RuntimeUiFactory.SetAnchors(icon.rectTransform, new Vector2(0.12f, 0.52f), new Vector2(0.88f, 0.92f));
            icon.sprite = artSource.Find($"icon/{card.EffectId}") ?? artSource.Find($"effect/{card.EffectId}/icon");
            icon.color = icon.sprite == null ? new Color(1f, 1f, 1f, 0.1f) : Color.white;
            icon.preserveAspect = true;

            EffectDef effect = config.GetEffect(card.EffectId);
            Text name = RuntimeUiFactory.CreateText("Name", panel.transform, font, 29, FontStyle.Bold, TextAnchor.MiddleCenter);
            name.text = effect.Name;
            RuntimeUiFactory.SetAnchors(name.rectTransform, new Vector2(0.05f, 0.4f), new Vector2(0.95f, 0.52f));
            Text desc = RuntimeUiFactory.CreateText("Description", panel.transform, font, 20, FontStyle.Normal, TextAnchor.UpperCenter);
            desc.text = effect.Desc;
            RuntimeUiFactory.SetAnchors(desc.rectTransform, new Vector2(0.06f, 0.22f), new Vector2(0.94f, 0.4f));

            Button choose = RuntimeUiFactory.CreateButton(
                "Choose", panel.transform, new Color(0.72f, 0.28f, 0.12f), font, "选 择", 25);
            RuntimeUiFactory.SetAnchors(choose.GetComponent<RectTransform>(), new Vector2(0.05f, 0.04f), new Vector2(0.45f, 0.18f));
            choose.onClick.AddListener(() => Choose(slotIndex));

            Button reroll = RuntimeUiFactory.CreateButton(
                "Reroll", panel.transform, new Color(0.17f, 0.46f, 0.63f), font, "重随", 22);
            RuntimeUiFactory.SetAnchors(reroll.GetComponent<RectTransform>(), new Vector2(0.48f, 0.04f), new Vector2(0.72f, 0.18f));
            reroll.onClick.AddListener(() => Reroll(slotIndex));

            Button ad = RuntimeUiFactory.CreateButton(
                "Ad Reroll", panel.transform, new Color(0.45f, 0.28f, 0.6f), font, "广告", 20);
            RuntimeUiFactory.SetAnchors(ad.GetComponent<RectTransform>(), new Vector2(0.75f, 0.04f), new Vector2(0.95f, 0.18f));
            ad.onClick.AddListener(() => WatchAd(slotIndex));
            RenderedCardCount++;
        }

        private void Choose(int slotIndex)
        {
            flow.Reward.Select(slotIndex);
            BuildExit("继续", flow.BeginNextStage);
            status.text = "奖励已选择，可继续";
        }

        private void Reroll(int slotIndex)
        {
            try
            {
                flow.Reward.Reroll(slotIndex);
                Show(flow, config, artSource, GameFlowPhase.Reward);
            }
            catch (InvalidOperationException exception)
            {
                status.text = exception.Message;
            }
        }

        private void WatchAd(int slotIndex)
        {
            status.text = flow.Reward.WatchAd(slotIndex)
                ? "Mock 广告完成：该卡获得一次额外重随"
                : "当前不可领取广告重随";
        }

        private void BuildTerminal(string heading, string message, Action exit)
        {
            title.text = heading;
            status.text = message;
            BuildExit("重新开始", exit);
        }

        private void BuildExit(string label, Action exit)
        {
            Button button = RuntimeUiFactory.CreateButton(
                "Exit", cardRow, new Color(0.72f, 0.28f, 0.12f), font, label, 34);
            LayoutElement layout = button.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 360f;
            button.onClick.AddListener(() => exit());
            ExitButtonCount++;
        }

        private void ClearGenerated()
        {
            for (int index = 0; index < generated.Count; index++)
            {
                if (generated[index] != null)
                {
                    Destroy(generated[index]);
                }
            }
            generated.Clear();
        }
    }
}
