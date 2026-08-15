using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HanziDefend.Data;
using UnityEngine;
using UnityEngine.UI;

namespace HanziDefend.View
{
    /// <summary>
    /// The stat sheet behind a card. Everything it prints is read from <see cref="UnitDef"/> and
    /// levelled through <see cref="Formula.StatAtLevel"/> at display time — the panel keeps no copy
    /// of any number, so it cannot drift from <c>units.json</c> the way a hand-written table would.
    ///
    /// <para>Deliberately no full-screen scrim. The panel blocks pointers over its own rect and
    /// nothing else, so a player can read a unit's numbers and keep dragging cards around it, and
    /// closing it never leaves an invisible sheet swallowing the next drag.</para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class DeployUnitInfoPanel : MonoBehaviour
    {
        private static readonly Color BackdropColor = new Color(0.09f, 0.12f, 0.17f, 1f);
        private static readonly Color BorderColor = new Color(1f, 0.83f, 0.38f, 1f);
        private static readonly Color RowColor = new Color(0.86f, 0.90f, 0.96f, 1f);
        private static readonly Color HeadingColor = new Color(1f, 0.94f, 0.72f, 1f);

        private readonly List<string> rows = new List<string>();
        private RectTransform panel;
        private RectTransform container;
        private Text headingText;
        private Text bodyText;
        private Font font;

        /// <summary>Whether the sheet is currently on screen.</summary>
        internal bool IsOpen => container != null && container.gameObject.activeSelf;

        /// <summary>Unit id currently shown, or empty when closed.</summary>
        internal string ShownUnitId { get; private set; } = string.Empty;

        internal int ShownLevel { get; private set; }

        /// <summary>The rendered rows, one "label\t value" per line. Test surface.</summary>
        internal IReadOnlyList<string> Rows => rows;

        internal static DeployUnitInfoPanel Create(Transform parent, Font panelFont, Action onClosed)
        {
            var host = new GameObject("Unit Info Panel", typeof(RectTransform), typeof(DeployUnitInfoPanel));
            host.transform.SetParent(parent, false);
            DeployUnitInfoPanel created = host.GetComponent<DeployUnitInfoPanel>();
            created.Build(panelFont, onClosed);
            return created;
        }

        private void Build(Font panelFont, Action onClosed)
        {
            font = panelFont;
            RectTransform root = GetComponent<RectTransform>();
            RuntimeUiFactory.Stretch(root);

            // Border first, backdrop inside it: one opaque sheet framed in gold, so the sheet reads
            // as a card in front of the board rather than as a tint laid over it.
            RectTransform frame = RuntimeUiFactory.CreatePanel("Info Frame", root, BorderColor);
            // Sized to its content and kept clear of the hand row, so the sheet never covers the
            // cards a player might want to drag while reading it.
            RuntimeUiFactory.SetAnchors(frame, new Vector2(0.085f, 0.40f), new Vector2(0.915f, 0.865f));

            panel = RuntimeUiFactory.CreatePanel("Info Backdrop", frame, BackdropColor);
            RuntimeUiFactory.Stretch(panel);
            panel.offsetMin = new Vector2(4f, 4f);
            panel.offsetMax = new Vector2(-4f, -4f);

            // The sheet swallows pointers over itself and nowhere else — no full-screen scrim — so
            // a player can read a unit and keep dragging cards around the panel.
            Image backdropImage = panel.GetComponent<Image>();
            backdropImage.raycastTarget = true;

            headingText = RuntimeUiFactory.CreateText(
                "Info Heading", panel, font, 34, FontStyle.Bold, TextAnchor.MiddleLeft);
            RuntimeUiFactory.SetAnchors(headingText.rectTransform, new Vector2(0.06f, 0.86f), new Vector2(0.75f, 0.965f));
            headingText.color = HeadingColor;

            bodyText = RuntimeUiFactory.CreateText(
                "Info Body", panel, font, 26, FontStyle.Normal, TextAnchor.UpperLeft);
            RuntimeUiFactory.SetAnchors(bodyText.rectTransform, new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.83f));
            bodyText.color = RowColor;
            bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bodyText.verticalOverflow = VerticalWrapMode.Overflow;
            bodyText.lineSpacing = 1.08f;

            Button close = RuntimeUiFactory.CreateButton(
                "Info Close", panel, new Color(0.62f, 0.20f, 0.24f), font, "关闭");
            RuntimeUiFactory.SetAnchors(
                close.GetComponent<RectTransform>(), new Vector2(0.78f, 0.855f), new Vector2(0.955f, 0.97f));
            close.onClick.AddListener(() =>
            {
                Close();
                onClosed?.Invoke();
            });

            frame.gameObject.SetActive(false);
            container = frame;
        }

        /// <summary>Fills the sheet from the definition and shows it.</summary>
        internal void Show(UnitDef definition, int level)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            rows.Clear();
            Add("汉字名", definition.Name);
            Add("显示名", definition.DisplayName);
            Add("占格", Footprint(definition));
            Add("等级", level.ToString(CultureInfo.InvariantCulture));
            Add("HP", Stat(definition.Hp, level));
            Add("ATK", Stat(definition.Atk, level));
            Add("攻速", Stat(definition.AtkSpeed, level));
            Add("射程", Stat(definition.Range, level));
            Add("移速", Stat(definition.MoveSpeed, level));
            Add("护甲值", Stat(definition.Armor, level));
            Add("穿透", Stat(definition.Pierce, level));
            Add("护甲类型", ArmorLabel(definition.ArmorType));
            Add("攻击类型", AttackLabel(definition.AtkType));
            Add("兵种类型", UnitTypeLabel(definition.UnitType));
            Add("对位加成", BonusLabel(definition.BonusVs));
            Add("特性", TraitLabel(definition.Traits));

            ShownUnitId = definition.Id;
            ShownLevel = level;
            headingText.text = $"{definition.Name} · {definition.DisplayName}  Lv.{level}";

            var body = new StringBuilder();
            for (int index = 0; index < rows.Count; index++)
            {
                body.AppendLine(rows[index]);
            }
            bodyText.text = body.ToString();
            container.gameObject.SetActive(true);
            container.SetAsLastSibling();
        }

        internal void Close()
        {
            if (container != null)
            {
                container.gameObject.SetActive(false);
            }
            ShownUnitId = string.Empty;
            ShownLevel = 0;
        }

        private void Add(string label, string value)
        {
            rows.Add($"{label}    {value}");
        }

        private static string Stat(StatCurve curve, int level)
        {
            if (curve == null || curve.Base <= 0f)
            {
                return "—";
            }

            float value = Formula.StatAtLevel(curve.Base, curve.Growth, level);
            return value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string Footprint(UnitDef definition)
        {
            string shape;
            switch (definition.Footprint)
            {
                case UnitFootprintShape.MissingUpperRight: shape = " 缺右上"; break;
                case UnitFootprintShape.MissingLowerLeft: shape = " 缺左下"; break;
                default: shape = string.Empty; break;
            }
            return $"{definition.GridW}×{definition.GridH}{shape}";
        }

        private static string ArmorLabel(ArmorType value)
        {
            switch (value)
            {
                case ArmorType.Unarmored: return "无甲";
                case ArmorType.Light: return "轻甲";
                case ArmorType.Heavy: return "重甲";
                case ArmorType.Building: return "建筑";
                default: return "—";
            }
        }

        private static string AttackLabel(AttackType value)
        {
            switch (value)
            {
                case AttackType.Slash: return "斩击";
                case AttackType.Blunt: return "打击";
                case AttackType.Arrow: return "弓箭";
                case AttackType.Siege: return "器械";
                case AttackType.None: return "无攻击";
                default: return "—";
            }
        }

        private static string UnitTypeLabel(UnitType value)
        {
            switch (value)
            {
                case UnitType.Infantry: return "步兵";
                case UnitType.Cavalry: return "骑兵";
                case UnitType.Naval: return "水军";
                case UnitType.Air: return "空军";
                case UnitType.Building: return "建筑";
                case UnitType.Special: return "特殊";
                default: return "—";
            }
        }

        private static string BonusTargetLabel(BonusTarget value)
        {
            switch (value)
            {
                case BonusTarget.Cavalry: return "对骑兵";
                case BonusTarget.HeavyArmor: return "对重甲";
                case BonusTarget.Building: return "对建筑";
                default: return "对未知";
            }
        }

        private static string BonusLabel(BonusVsDef[] bonuses)
        {
            if (bonuses == null || bonuses.Length == 0)
            {
                return "无";
            }

            var text = new StringBuilder();
            for (int index = 0; index < bonuses.Length; index++)
            {
                if (index > 0) text.Append("，");
                text.Append(BonusTargetLabel(bonuses[index].Target))
                    .Append(" +")
                    .Append(bonuses[index].Value.ToString("0.##", CultureInfo.InvariantCulture));
            }
            return text.ToString();
        }

        private static string TraitLabel(UnitTraitDef[] traits)
        {
            if (traits == null || traits.Length == 0)
            {
                return "无";
            }

            var text = new StringBuilder();
            for (int index = 0; index < traits.Length; index++)
            {
                if (index > 0) text.Append("，");
                text.Append(TraitName(traits[index]));
            }
            return text.ToString();
        }

        private static string TraitName(UnitTraitDef trait)
        {
            switch (trait.Type)
            {
                case UnitTraitType.Charge: return $"冲锋（首击 ×{trait.Multiplier:0.##}）";
                case UnitTraitType.Trample: return "践踏（冲向后方，沿途普攻）";
                case UnitTraitType.PiercingShot:
                    return $"穿甲箭（首个 ×{trait.Multiplier:0.##}，每穿透递减）";
                case UnitTraitType.FireAura: return "光环 · 点燃";
                case UnitTraitType.IceAura: return "光环 · 冰冻";
                case UnitTraitType.DeathSpawn: return $"死亡召唤 {trait.Count} 个「{trait.UnitId}」";
                default: return "未知特性";
            }
        }
    }
}
