using System;
using System.Collections.Generic;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.View
{
    /// <summary>Build-safe JSON source generated from the seven runtime files in Assets/GameData.</summary>
    [CreateAssetMenu(menuName = "HanziDefend/Game Config Text Bundle", fileName = "HanziDefendGameConfig")]
    public sealed class GameConfigTextBundle : ScriptableObject
    {
        public TextAsset Units;
        public TextAsset Commanders;
        public TextAsset Waves;
        public TextAsset Levels;
        public TextAsset Effects;
        public TextAsset Economy;
        public TextAsset Feedback;

        public IConfigSource CreateSource()
        {
            return new BundleConfigSource(this);
        }

        private sealed class BundleConfigSource : IConfigSource
        {
            private readonly Dictionary<string, TextAsset> files;

            internal BundleConfigSource(GameConfigTextBundle bundle)
            {
                if (bundle == null)
                {
                    throw new ArgumentNullException(nameof(bundle));
                }

                files = new Dictionary<string, TextAsset>(StringComparer.Ordinal)
                {
                    ["units.json"] = bundle.Units,
                    ["commanders.json"] = bundle.Commanders,
                    ["waves.json"] = bundle.Waves,
                    ["levels.json"] = bundle.Levels,
                    ["effects.json"] = bundle.Effects,
                    ["economy.json"] = bundle.Economy,
                    ["feedback.json"] = bundle.Feedback
                };
            }

            public string ReadText(string fileName)
            {
                if (!files.TryGetValue(fileName, out TextAsset textAsset) || textAsset == null)
                {
                    throw new ConfigLoadException($"Runtime config bundle is missing '{fileName}'.");
                }

                return textAsset.text;
            }
        }
    }

    public static class RuntimeGameConfigLoader
    {
        private const string ResourceName = "HanziDefendGameConfig";

        public static GameConfig Load()
        {
            GameConfigTextBundle bundle = Resources.Load<GameConfigTextBundle>(ResourceName);
            if (bundle == null)
            {
                throw new ConfigLoadException(
                    $"Runtime config resource '{ResourceName}' is missing. Regenerate the art/config catalogs in the Editor.");
            }

            return GameConfig.Load(bundle.CreateSource());
        }
    }
}
