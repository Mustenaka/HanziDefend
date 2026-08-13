using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HanziDefend.Editor
{
    public sealed class DataEditorDocument
    {
        public DataEditorDocument(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path cannot be empty.", nameof(filePath));
            }

            FilePath = Path.GetFullPath(filePath);
            FileName = Path.GetFileName(FilePath);
            Reload();
        }

        public string FileName { get; }

        public string FilePath { get; }

        public string OriginalText { get; private set; }

        public string CurrentText { get; private set; }

        public bool IsDirty => !string.Equals(OriginalText, CurrentText, StringComparison.Ordinal);

        public void ReplaceText(string text)
        {
            CurrentText = text ?? string.Empty;
        }

        public void Reload()
        {
            OriginalText = File.ReadAllText(FilePath);
            CurrentText = OriginalText;
        }

        public void Save()
        {
            File.WriteAllText(FilePath, CurrentText);
            OriginalText = CurrentText;
        }

        public void SetUnitCurveValue(string unitId, string stat, string component, double value)
        {
            if (FileName != "units.json")
            {
                throw new InvalidOperationException("Unit curves can only be edited in units.json.");
            }

            if (component != "base" && component != "growth")
            {
                throw new ArgumentOutOfRangeException(nameof(component), component, "Curve component must be base or growth.");
            }

            JObject root = ParseRoot(CurrentText);
            JArray units = root["units"] as JArray
                ?? throw new InvalidOperationException("units.json is missing units[].");
            JObject unit = FindById(units, unitId)
                ?? throw new KeyNotFoundException($"units.json has no unit '{unitId}'.");
            JObject curve = unit[stat] as JObject
                ?? throw new KeyNotFoundException($"Unit '{unitId}' has no curve '{stat}'.");
            JValue token = curve[component] as JValue
                ?? throw new KeyNotFoundException($"Unit '{unitId}' curve '{stat}' has no '{component}'.");

            ReplaceToken(token, value.ToString("0.0###############", CultureInfo.InvariantCulture));
        }

        public void SetBossNumber(string bossId, string field, double value)
        {
            if (FileName != "units.json")
            {
                throw new InvalidOperationException("Boss values can only be edited in units.json.");
            }

            JObject root = ParseRoot(CurrentText);
            JArray bosses = root["bosses"] as JArray
                ?? throw new InvalidOperationException("units.json is missing bosses[].");
            JObject boss = FindById(bosses, bossId)
                ?? throw new KeyNotFoundException($"units.json has no boss '{bossId}'.");
            JValue token = boss[field] as JValue
                ?? throw new KeyNotFoundException($"Boss '{bossId}' has no field '{field}'.");

            ReplaceToken(token, value.ToString("0.0###############", CultureInfo.InvariantCulture));
        }

        public string BuildLineDiff()
        {
            string[] before = SplitLines(OriginalText);
            string[] after = SplitLines(CurrentText);
            var result = new List<string>();
            int count = Math.Max(before.Length, after.Length);
            for (int index = 0; index < count; index++)
            {
                string left = index < before.Length ? before[index] : null;
                string right = index < after.Length ? after[index] : null;
                if (string.Equals(left, right, StringComparison.Ordinal))
                {
                    continue;
                }

                result.Add($"@@ line {index + 1} @@");
                if (left != null)
                {
                    result.Add("- " + left);
                }

                if (right != null)
                {
                    result.Add("+ " + right);
                }
            }

            return result.Count == 0 ? "No changes." : string.Join("\n", result);
        }

        private static JObject ParseRoot(string text)
        {
            using (var reader = new JsonTextReader(new StringReader(text)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                return JObject.Load(reader, new JsonLoadSettings { LineInfoHandling = LineInfoHandling.Load });
            }
        }

        private static JObject FindById(JArray array, string id)
        {
            foreach (JToken token in array)
            {
                if (token is JObject item && string.Equals((string)item["id"], id, StringComparison.Ordinal))
                {
                    return item;
                }
            }

            return null;
        }

        private void ReplaceToken(JValue token, string replacement)
        {
            if (!(token is IJsonLineInfo lineInfo) || !lineInfo.HasLineInfo())
            {
                throw new InvalidOperationException("JSON token has no source location.");
            }

            int lineStart = GetOffset(CurrentText, lineInfo.LineNumber, 1);
            int lineEnd = CurrentText.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = CurrentText.Length;
            }

            string propertyName = (token.Parent as JProperty)?.Name
                ?? throw new InvalidOperationException("JSON value is not owned by a property.");
            string propertyMarker = "\"" + propertyName + "\"";
            int marker = CurrentText.IndexOf(propertyMarker, lineStart, lineEnd - lineStart, StringComparison.Ordinal);
            if (marker < 0)
            {
                throw new InvalidOperationException($"Cannot locate JSON property '{propertyName}' on its source line.");
            }

            int start = CurrentText.IndexOf(':', marker + propertyMarker.Length);
            if (start < 0 || start >= lineEnd)
            {
                throw new InvalidOperationException($"Cannot locate JSON value for '{propertyName}'.");
            }

            start++;
            while (start < lineEnd && char.IsWhiteSpace(CurrentText[start]))
            {
                start++;
            }

            int length = ScanTokenLength(CurrentText, start);
            CurrentText = CurrentText.Substring(0, start) + replacement + CurrentText.Substring(start + length);
        }

        private static int GetOffset(string text, int lineNumber, int linePosition)
        {
            int offset = 0;
            for (int line = 1; line < lineNumber; line++)
            {
                int next = text.IndexOf('\n', offset);
                if (next < 0)
                {
                    throw new InvalidOperationException("JSON source location is outside the document.");
                }

                offset = next + 1;
            }

            return offset + linePosition - 1;
        }

        private static int ScanTokenLength(string text, int start)
        {
            int index = start;
            if (index < text.Length && text[index] == '"')
            {
                index++;
                bool escaped = false;
                while (index < text.Length)
                {
                    char value = text[index++];
                    if (value == '"' && !escaped)
                    {
                        break;
                    }

                    escaped = value == '\\' && !escaped;
                    if (value != '\\')
                    {
                        escaped = false;
                    }
                }

                return index - start;
            }

            while (index < text.Length && !char.IsWhiteSpace(text[index]) && text[index] != ',' && text[index] != '}' && text[index] != ']')
            {
                index++;
            }

            return index - start;
        }

        private static string[] SplitLines(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }
    }
}
