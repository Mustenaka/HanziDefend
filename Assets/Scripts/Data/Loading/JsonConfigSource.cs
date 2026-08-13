using System;
using System.IO;

namespace HanziDefend.Data
{
    public sealed class JsonConfigSource : IConfigSource
    {
        public const string DefaultRelativeDirectory = "Assets/GameData";

        private readonly string rootDirectory;
        private readonly string rootPrefix;

        public JsonConfigSource()
            : this(Path.Combine(Directory.GetCurrentDirectory(), "Assets", "GameData"))
        {
        }

        public JsonConfigSource(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
            {
                throw new ArgumentException("Config root directory cannot be empty.", nameof(rootDirectory));
            }

            this.rootDirectory = Path.GetFullPath(rootDirectory);
            rootPrefix = this.rootDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
        }

        public string RootDirectory => rootDirectory;

        public string ReadText(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("Config file name cannot be empty.", nameof(fileName));
            }

            string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, fileName));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new ConfigLoadException($"Config path '{fileName}' escapes root '{rootDirectory}'.");
            }

            if (!File.Exists(fullPath))
            {
                throw new ConfigLoadException($"Config file was not found: '{fullPath}'.");
            }

            try
            {
                return File.ReadAllText(fullPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                throw new ConfigLoadException($"Failed to read config file '{fullPath}': {exception.Message}", exception);
            }
        }

        public T Load<T>(string fileName)
        {
            string json = ReadText(fileName);
            try
            {
                return JsonCodec.Deserialize<T>(json);
            }
            catch (Exception exception) when (exception is Newtonsoft.Json.JsonException || exception is ArgumentException)
            {
                throw new ConfigLoadException(
                    $"Failed to parse config file '{fileName}' as {typeof(T).Name}: {exception.Message}",
                    exception);
            }
        }
    }
}
