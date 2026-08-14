using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HanziDefend.Data;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor
{
    /// <summary>
    /// Human-facing editor for the JSON files that remain the single source of game data.
    /// </summary>
    public sealed class DataEditorWindow : EditorWindow
    {
        public const string MenuPath = "HanziDefend/Data Editor";

        private const string WindowTitle = "Data Editor";
        private const float SidebarWidth = 190f;
        private const float IdColumnWidth = 92f;
        private const float NameColumnWidth = 76f;
        private const float MinimumColumnWidth = 58f;
        private const float MaximumColumnWidth = 132f;

        private static readonly string[] UnitStatNames =
        {
            "hp", "atk", "range", "minRange", "atkSpeed", "cooldown", "armor", "pierce", "moveSpeed"
        };

        private static readonly string[] BossNumberNames =
        {
            "hp", "armor", "atk", "range", "atkSpeed", "pierce"
        };

        [SerializeField]
        private float numericColumnWidth = 76f;

        [SerializeField]
        private string selectedFileName = "units.json";

        [SerializeField]
        private List<StagedDocument> stagedDocuments = new List<StagedDocument>();

        private DataEditorWorkspace workspace;
        private IReadOnlyList<DataEditorValidationError> validationErrors =
            Array.Empty<DataEditorValidationError>();
        private Vector2 fileScroll;
        private Vector2 contentScroll;
        private Vector2 validationScroll;
        private GUIStyle rawTextStyle;
        private string initializationError;
        private string validationFailure;
        private string statusMessage;

        [MenuItem(MenuPath)]
        public static void OpenWindow()
        {
            DataEditorWindow window = GetWindow<DataEditorWindow>(WindowTitle);
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(820f, 520f);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            saveChangesMessage =
                "HanziDefend JSON data has unsaved changes. Save before closing the Data Editor?";
            EnsureWorkspace();
        }

        private void OnDisable()
        {
            CaptureStagedDocuments();
        }

        private void OnGUI()
        {
            EnsureWorkspace();

            if (!string.IsNullOrEmpty(initializationError))
            {
                EditorGUILayout.HelpBox(initializationError, MessageType.Error);
                if (GUILayout.Button("Retry", GUILayout.Width(100f)))
                {
                    workspace = null;
                    EnsureWorkspace();
                }

                return;
            }

            DrawTopToolbar();

            EditorGUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawFileList();
            DrawCurrentDocument();
            EditorGUILayout.EndHorizontal();

            DrawValidationErrors();
            DrawStatusLine();
            SynchronizeUnsavedState();
        }

        public override void SaveChanges()
        {
            if (workspace == null)
            {
                base.SaveChanges();
                return;
            }

            RefreshValidation();
            if (!string.IsNullOrEmpty(validationFailure) || validationErrors.Count > 0)
            {
                throw new InvalidOperationException(BuildValidationSummary());
            }

            string previouslySelected = workspace.CurrentDocument != null
                ? workspace.CurrentDocument.FileName
                : null;
            DataEditorDocument[] dirtyDocuments = workspace.Documents
                .Where(document => document.IsDirty)
                .ToArray();

            foreach (DataEditorDocument document in dirtyDocuments)
            {
                workspace.Open(document.FileName);
                workspace.SaveCurrent();
                ImportDocument(document);
            }

            RestoreSelection(previouslySelected);
            RefreshValidation();
            CaptureStagedDocuments();
            SynchronizeUnsavedState();
            base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            if (workspace != null)
            {
                string previouslySelected = workspace.CurrentDocument != null
                    ? workspace.CurrentDocument.FileName
                    : null;
                DataEditorDocument[] dirtyDocuments = workspace.Documents
                    .Where(document => document.IsDirty)
                    .ToArray();

                foreach (DataEditorDocument document in dirtyDocuments)
                {
                    workspace.Open(document.FileName);
                    workspace.ReloadCurrent();
                }

                RestoreSelection(previouslySelected);
                RefreshValidation();
                CaptureStagedDocuments();
                SynchronizeUnsavedState();
            }

            base.DiscardChanges();
        }

        private void EnsureWorkspace()
        {
            if (workspace != null || !string.IsNullOrEmpty(initializationError))
            {
                return;
            }

            try
            {
                string dataRoot = Path.Combine(Application.dataPath, "GameData");
                workspace = new DataEditorWorkspace(dataRoot);
                workspace.DiscoverFiles();
                RestoreStagedDocuments();

                DataEditorDocument initial = workspace.Documents.FirstOrDefault(document =>
                    string.Equals(document.FileName, selectedFileName, StringComparison.Ordinal));
                if (initial == null)
                {
                    initial = workspace.Documents.FirstOrDefault(document =>
                        string.Equals(document.FileName, "units.json", StringComparison.Ordinal));
                }

                if (initial == null)
                {
                    initial = workspace.Documents.FirstOrDefault();
                }

                if (initial != null)
                {
                    workspace.Open(initial.FileName);
                    selectedFileName = initial.FileName;
                }

                initializationError = null;
                RefreshValidation();
                SynchronizeUnsavedState();
            }
            catch (Exception exception)
            {
                workspace = null;
                initializationError = "Unable to initialize Assets/GameData: " + exception.Message;
            }
        }

        private void DrawTopToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(
                workspace.CurrentDocument != null ? workspace.CurrentDocument.FileName : "No JSON selected",
                EditorStyles.boldLabel,
                GUILayout.MinWidth(150f));
            GUILayout.FlexibleSpace();

            GUILayout.Label("Column width", EditorStyles.miniLabel, GUILayout.Width(78f));
            numericColumnWidth = GUILayout.HorizontalSlider(
                numericColumnWidth,
                MinimumColumnWidth,
                MaximumColumnWidth,
                GUILayout.Width(120f));
            GUILayout.Label(Mathf.RoundToInt(numericColumnWidth).ToString(), GUILayout.Width(28f));

            using (new EditorGUI.DisabledScope(workspace.CurrentDocument == null))
            {
                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(62f)))
                {
                    ReloadCurrentWithConfirmation();
                }

                if (GUILayout.Button("Diff Preview", EditorStyles.toolbarButton, GUILayout.Width(88f)))
                {
                    ShowCurrentDiff();
                }

                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(52f)))
                {
                    TrySaveCurrentWithConfirmation();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawFileList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true));
            GUILayout.Label("Assets/GameData", EditorStyles.boldLabel);
            fileScroll = EditorGUILayout.BeginScrollView(fileScroll, GUILayout.ExpandHeight(true));

            foreach (DataEditorDocument document in workspace.Documents)
            {
                bool selected = ReferenceEquals(document, workspace.CurrentDocument);
                string label = (document.IsDirty ? "● " : string.Empty) + document.FileName;
                bool requested = GUILayout.Toggle(
                    selected,
                    label,
                    EditorStyles.toolbarButton,
                    GUILayout.ExpandWidth(true));
                if (requested && !selected)
                {
                    SelectDocument(document.FileName);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawCurrentDocument()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DataEditorDocument document = workspace.CurrentDocument;
            if (document == null)
            {
                EditorGUILayout.HelpBox("No editable JSON files were found.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            if (string.Equals(document.FileName, "units.json", StringComparison.Ordinal))
            {
                DrawUnitsDocument(document);
            }
            else
            {
                DrawRawDocument(document);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawUnitsDocument(DataEditorDocument document)
        {
            UnitCatalog catalog;
            try
            {
                catalog = JsonCodec.Deserialize<UnitCatalog>(document.CurrentText);
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(
                    "The unit table cannot be rendered until units.json is valid JSON: " + exception.Message,
                    MessageType.Error);
                DrawRawDocument(document);
                return;
            }

            bool changed = false;
            UnitDef[] units = catalog.Units ?? Array.Empty<UnitDef>();
            int bonusSlotCount = Math.Max(
                1,
                units.Where(unit => unit != null)
                    .Select(unit => unit.BonusVs?.Length ?? 0)
                    .DefaultIfEmpty(0)
                    .Max());
            float totalWidth = IdColumnWidth + NameColumnWidth
                + 4f * numericColumnWidth
                + bonusSlotCount * numericColumnWidth * 2f
                + UnitStatNames.Length * numericColumnWidth * 2f
                + 64f;

            contentScroll = EditorGUILayout.BeginScrollView(
                contentScroll,
                true,
                true,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));

            GUILayout.Label("Units — base / growth", EditorStyles.boldLabel);
            DrawUnitHeader(totalWidth, bonusSlotCount);
            foreach (UnitDef unit in units)
            {
                if (unit != null)
                {
                    changed |= DrawUnitRow(document, unit, totalWidth, bonusSlotCount);
                }
            }

            GUILayout.Space(12f);
            GUILayout.Label("Bosses", EditorStyles.boldLabel);
            DrawBossHeader(totalWidth);
            foreach (BossDef boss in catalog.Bosses ?? Array.Empty<BossDef>())
            {
                if (boss != null)
                {
                    changed |= DrawBossRow(document, boss, totalWidth);
                }
            }

            EditorGUILayout.EndScrollView();

            if (changed)
            {
                DocumentChanged();
            }
        }

        private void DrawUnitHeader(float totalWidth, int bonusSlotCount)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Width(totalWidth));
            GUILayout.Label("id", GUILayout.Width(IdColumnWidth));
            GUILayout.Label("name", GUILayout.Width(NameColumnWidth));
            GUILayout.Label("unit type", EditorStyles.miniBoldLabel, GUILayout.Width(numericColumnWidth));
            GUILayout.Label("armor type", EditorStyles.miniBoldLabel, GUILayout.Width(numericColumnWidth));
            GUILayout.Label("attack type", EditorStyles.miniBoldLabel, GUILayout.Width(numericColumnWidth));
            GUILayout.Label("footprint", EditorStyles.miniBoldLabel, GUILayout.Width(numericColumnWidth));
            for (int index = 0; index < bonusSlotCount; index++)
            {
                string prefix = bonusSlotCount == 1 ? "bonus" : $"bonus {index + 1}";
                GUILayout.Label(prefix + " target", EditorStyles.miniBoldLabel, GUILayout.Width(numericColumnWidth));
                GUILayout.Label(prefix + " value", EditorStyles.miniBoldLabel, GUILayout.Width(numericColumnWidth));
            }

            foreach (string stat in UnitStatNames)
            {
                GUILayout.Label(stat + " base", EditorStyles.miniBoldLabel, GUILayout.Width(numericColumnWidth));
                GUILayout.Label(stat + " growth", EditorStyles.miniBoldLabel, GUILayout.Width(numericColumnWidth));
            }

            EditorGUILayout.EndHorizontal();
        }

        private bool DrawUnitRow(
            DataEditorDocument document,
            UnitDef unit,
            float totalWidth,
            int bonusSlotCount)
        {
            bool changed = false;
            EditorGUILayout.BeginHorizontal(GUILayout.Width(totalWidth));
            GUILayout.Label(unit.Id, GUILayout.Width(IdColumnWidth));
            GUILayout.Label(unit.Name, GUILayout.Width(NameColumnWidth));

            changed |= DrawUnitEnum(document, unit.Id, "unitType", unit.UnitType);
            changed |= DrawUnitEnum(document, unit.Id, "armorType", unit.ArmorType);
            changed |= DrawUnitEnum(document, unit.Id, "atkType", unit.AtkType);
            changed |= DrawUnitEnum(document, unit.Id, "footprint", unit.Footprint);

            BonusVsDef[] bonuses = unit.BonusVs ?? Array.Empty<BonusVsDef>();
            for (int index = 0; index < bonusSlotCount; index++)
            {
                if (index >= bonuses.Length || bonuses[index] == null)
                {
                    GUILayout.Label("-", GUILayout.Width(numericColumnWidth));
                    GUILayout.Label("-", GUILayout.Width(numericColumnWidth));
                    continue;
                }

                changed |= DrawUnitBonusTarget(document, unit.Id, index, bonuses[index].Target);
                changed |= DrawUnitBonusValue(document, unit.Id, index, bonuses[index].Value);
            }

            foreach (string statName in UnitStatNames)
            {
                StatCurve curve = GetCurve(unit, statName);
                if (curve == null)
                {
                    GUILayout.Label("missing", GUILayout.Width(numericColumnWidth * 2f));
                    continue;
                }

                changed |= DrawUnitNumber(document, unit.Id, statName, "base", curve.Base);
                changed |= DrawUnitNumber(document, unit.Id, statName, "growth", curve.Growth);
            }

            EditorGUILayout.EndHorizontal();
            return changed;
        }

        private bool DrawUnitEnum<T>(DataEditorDocument document, string unitId, string field, T value)
            where T : Enum
        {
            EditorGUI.BeginChangeCheck();
            T next = (T)EditorGUILayout.EnumPopup(value, GUILayout.Width(numericColumnWidth));
            if (!EditorGUI.EndChangeCheck())
            {
                return false;
            }

            document.SetUnitString(unitId, field, next.ToString());
            return true;
        }

        private bool DrawUnitBonusTarget(
            DataEditorDocument document,
            string unitId,
            int bonusIndex,
            BonusTarget value)
        {
            EditorGUI.BeginChangeCheck();
            BonusTarget next = (BonusTarget)EditorGUILayout.EnumPopup(value, GUILayout.Width(numericColumnWidth));
            if (!EditorGUI.EndChangeCheck())
            {
                return false;
            }

            document.SetUnitBonusTarget(unitId, bonusIndex, next);
            return true;
        }

        private bool DrawUnitBonusValue(
            DataEditorDocument document,
            string unitId,
            int bonusIndex,
            float value)
        {
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.FloatField(value, GUILayout.Width(numericColumnWidth));
            if (!EditorGUI.EndChangeCheck())
            {
                return false;
            }

            document.SetUnitBonusValue(unitId, bonusIndex, next);
            return true;
        }

        private bool DrawUnitNumber(
            DataEditorDocument document,
            string unitId,
            string statName,
            string component,
            float value)
        {
            EditorGUI.BeginChangeCheck();
            float nextValue = EditorGUILayout.FloatField(value, GUILayout.Width(numericColumnWidth));
            if (!EditorGUI.EndChangeCheck())
            {
                return false;
            }

            document.SetUnitCurveValue(unitId, statName, component, nextValue);
            return true;
        }

        private void DrawBossHeader(float totalWidth)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Width(totalWidth));
            GUILayout.Label("id", GUILayout.Width(IdColumnWidth));
            GUILayout.Label("name", GUILayout.Width(NameColumnWidth));
            foreach (string field in BossNumberNames)
            {
                GUILayout.Label(field, EditorStyles.miniBoldLabel, GUILayout.Width(numericColumnWidth));
            }

            EditorGUILayout.EndHorizontal();
        }

        private bool DrawBossRow(DataEditorDocument document, BossDef boss, float totalWidth)
        {
            bool changed = false;
            EditorGUILayout.BeginHorizontal(GUILayout.Width(totalWidth));
            GUILayout.Label(boss.Id, GUILayout.Width(IdColumnWidth));
            GUILayout.Label(boss.Name, GUILayout.Width(NameColumnWidth));

            foreach (string field in BossNumberNames)
            {
                float currentValue = GetBossNumber(boss, field);
                EditorGUI.BeginChangeCheck();
                float nextValue = EditorGUILayout.FloatField(currentValue, GUILayout.Width(numericColumnWidth));
                if (EditorGUI.EndChangeCheck())
                {
                    document.SetBossNumber(boss.Id, field, nextValue);
                    changed = true;
                }
            }

            EditorGUILayout.EndHorizontal();
            return changed;
        }

        private void DrawRawDocument(DataEditorDocument document)
        {
            GUIStyle style = GetRawTextStyle();
            contentScroll = EditorGUILayout.BeginScrollView(
                contentScroll,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));

            EditorGUI.BeginChangeCheck();
            string nextText = EditorGUILayout.TextArea(
                document.CurrentText,
                style,
                GUILayout.MinHeight(Mathf.Max(320f, position.height - 190f)),
                GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
            {
                document.ReplaceText(nextText);
                DocumentChanged();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawValidationErrors()
        {
            if (string.IsNullOrEmpty(validationFailure) && validationErrors.Count == 0)
            {
                return;
            }

            Color previousBackground = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.48f, 0.48f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MaxHeight(142f));
            GUI.backgroundColor = previousBackground;
            GUILayout.Label("Schema validation", EditorStyles.boldLabel);

            validationScroll = EditorGUILayout.BeginScrollView(validationScroll, GUILayout.MaxHeight(112f));
            if (!string.IsNullOrEmpty(validationFailure))
            {
                EditorGUILayout.LabelField("Validation", "$", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(validationFailure, EditorStyles.wordWrappedLabel);
            }

            foreach (DataEditorValidationError error in validationErrors)
            {
                EditorGUILayout.LabelField(
                    error.FileName + "  " + error.Path,
                    EditorStyles.boldLabel);
                EditorGUILayout.LabelField(error.Message, EditorStyles.wordWrappedLabel);
                GUILayout.Space(2f);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawStatusLine()
        {
            if (string.IsNullOrEmpty(statusMessage))
            {
                return;
            }

            EditorGUILayout.LabelField(statusMessage, EditorStyles.miniLabel);
        }

        private void SelectDocument(string fileName)
        {
            workspace.Open(fileName);
            selectedFileName = fileName;
            contentScroll = Vector2.zero;
            statusMessage = null;
            RefreshValidation();
            CaptureStagedDocuments();
            SynchronizeUnsavedState();
            Repaint();
        }

        private void ReloadCurrentWithConfirmation()
        {
            DataEditorDocument document = workspace.CurrentDocument;
            if (document == null)
            {
                return;
            }

            if (document.IsDirty && !EditorUtility.DisplayDialog(
                    "Discard unsaved changes?",
                    "Reload " + document.FileName + " from disk and discard its unsaved edits?",
                    "Reload",
                    "Cancel"))
            {
                return;
            }

            workspace.ReloadCurrent();
            RefreshValidation();
            CaptureStagedDocuments();
            SynchronizeUnsavedState();
            statusMessage = "Reloaded " + document.FileName + ".";
            GUI.FocusControl(null);
            Repaint();
        }

        private void ShowCurrentDiff()
        {
            DataEditorDocument document = workspace.CurrentDocument;
            if (document == null)
            {
                return;
            }

            DiffPreviewWindow.ShowPreview(document.FileName, document.BuildLineDiff());
        }

        private void TrySaveCurrentWithConfirmation()
        {
            DataEditorDocument document = workspace.CurrentDocument;
            if (document == null)
            {
                return;
            }

            RefreshValidation();
            if (!string.IsNullOrEmpty(validationFailure) || validationErrors.Count > 0)
            {
                statusMessage = "Save blocked: fix schema validation errors first.";
                EditorUtility.DisplayDialog("Cannot save JSON", BuildValidationSummary(), "OK");
                return;
            }

            if (!document.IsDirty)
            {
                statusMessage = document.FileName + " has no unsaved changes.";
                return;
            }

            string diff = document.BuildLineDiff();
            string preview = TruncateForDialog(diff, 9000);
            if (!EditorUtility.DisplayDialog(
                    "Save " + document.FileName + "?",
                    "The following line changes will be written:\n\n" + preview,
                    "Save",
                    "Cancel"))
            {
                return;
            }

            try
            {
                workspace.SaveCurrent();
                ImportDocument(document);
                RefreshValidation();
                CaptureStagedDocuments();
                SynchronizeUnsavedState();
                statusMessage = "Saved " + document.FileName + ".";
            }
            catch (Exception exception)
            {
                statusMessage = "Save failed: " + exception.Message;
                EditorUtility.DisplayDialog("Save failed", exception.Message, "OK");
            }

            Repaint();
        }

        private void DocumentChanged()
        {
            statusMessage = null;
            RefreshValidation();
            CaptureStagedDocuments();
            SynchronizeUnsavedState();
            Repaint();
        }

        private void RefreshValidation()
        {
            if (workspace == null)
            {
                validationErrors = Array.Empty<DataEditorValidationError>();
                validationFailure = null;
                return;
            }

            try
            {
                validationErrors = workspace.ValidateAll();
                validationFailure = null;
            }
            catch (Exception exception)
            {
                validationErrors = Array.Empty<DataEditorValidationError>();
                validationFailure = exception.Message;
            }
        }

        private void SynchronizeUnsavedState()
        {
            hasUnsavedChanges = workspace != null && workspace.Documents.Any(document => document.IsDirty);
        }

        private void CaptureStagedDocuments()
        {
            if (workspace == null)
            {
                return;
            }

            stagedDocuments = workspace.Documents
                .Where(document => document.IsDirty)
                .Select(document => new StagedDocument
                {
                    FileName = document.FileName,
                    CurrentText = document.CurrentText
                })
                .ToList();

            if (workspace.CurrentDocument != null)
            {
                selectedFileName = workspace.CurrentDocument.FileName;
            }
        }

        private void RestoreStagedDocuments()
        {
            if (stagedDocuments == null || stagedDocuments.Count == 0)
            {
                return;
            }

            foreach (StagedDocument staged in stagedDocuments)
            {
                DataEditorDocument document = workspace.Documents.FirstOrDefault(candidate =>
                    string.Equals(candidate.FileName, staged.FileName, StringComparison.Ordinal));
                if (document != null)
                {
                    document.ReplaceText(staged.CurrentText);
                }
            }
        }

        private void RestoreSelection(string fileName)
        {
            if (!string.IsNullOrEmpty(fileName)
                && workspace.Documents.Any(document =>
                    string.Equals(document.FileName, fileName, StringComparison.Ordinal)))
            {
                workspace.Open(fileName);
                selectedFileName = fileName;
            }
        }

        private string BuildValidationSummary()
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(validationFailure))
            {
                lines.Add("Validation: $: " + validationFailure);
            }

            lines.AddRange(validationErrors.Select(error =>
                error.FileName + ":" + error.Path + ": " + error.Message));

            if (lines.Count == 0)
            {
                return "No schema validation errors.";
            }

            return TruncateForDialog(string.Join(Environment.NewLine, lines), 9000);
        }

        private static void ImportDocument(DataEditorDocument document)
        {
            string normalizedAssetsPath = Path.GetFullPath(Application.dataPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string normalizedFilePath = Path.GetFullPath(document.FilePath);
            string prefix = normalizedAssetsPath + Path.DirectorySeparatorChar;
            if (!normalizedFilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string assetPath = "Assets/" + normalizedFilePath
                .Substring(prefix.Length)
                .Replace('\\', '/');
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }

        private static StatCurve GetCurve(UnitDef unit, string statName)
        {
            switch (statName)
            {
                case "hp": return unit.Hp;
                case "atk": return unit.Atk;
                case "range": return unit.Range;
                case "atkSpeed": return unit.AtkSpeed;
                case "cooldown": return unit.Cooldown;
                case "armor": return unit.Armor;
                case "pierce": return unit.Pierce;
                case "moveSpeed": return unit.MoveSpeed;
                default: throw new ArgumentOutOfRangeException(nameof(statName), statName, "Unknown unit stat.");
            }
        }

        private static float GetBossNumber(BossDef boss, string field)
        {
            switch (field)
            {
                case "hp": return boss.Hp;
                case "armor": return boss.Armor;
                case "atk": return boss.Atk;
                case "range": return boss.Range;
                case "atkSpeed": return boss.AtkSpeed;
                case "pierce": return boss.Pierce;
                default: throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown boss field.");
            }
        }

        private GUIStyle GetRawTextStyle()
        {
            if (rawTextStyle == null)
            {
                rawTextStyle = new GUIStyle(EditorStyles.textArea)
                {
                    font = EditorStyles.textArea.font,
                    wordWrap = false
                };
            }

            return rawTextStyle;
        }

        private static string TruncateForDialog(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maximumLength)
            {
                return value;
            }

            return value.Substring(0, maximumLength) + "\n… diff truncated; use Diff Preview for the full text.";
        }

        [Serializable]
        private sealed class StagedDocument
        {
            public string FileName;
            public string CurrentText;
        }

        private sealed class DiffPreviewWindow : EditorWindow
        {
            private string diffText;
            private Vector2 scroll;

            public static void ShowPreview(string fileName, string diff)
            {
                DiffPreviewWindow window = CreateInstance<DiffPreviewWindow>();
                window.titleContent = new GUIContent("Diff — " + fileName);
                window.diffText = diff ?? "No changes.";
                window.minSize = new Vector2(680f, 420f);
                window.position = new Rect(180f, 120f, 820f, 600f);
                window.ShowModalUtility();
            }

            private void OnGUI()
            {
                GUILayout.Label("Line diff", EditorStyles.boldLabel);
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextArea(
                        diffText,
                        new GUIStyle(EditorStyles.textArea) { wordWrap = false },
                        GUILayout.ExpandWidth(true),
                        GUILayout.ExpandHeight(true));
                }

                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("Close", GUILayout.Width(90f)))
                {
                    Close();
                }
            }
        }
    }
}
