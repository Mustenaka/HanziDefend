using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace HanziDefend.Data
{
    public static class JsonCodec
    {
        private static readonly JsonSerializerSettings SerializerSettings = CreateSettings();

        public static string Serialize<T>(T value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return JsonConvert.SerializeObject(value, SerializerSettings);
        }

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("JSON content cannot be empty.", nameof(json));
            }

            T result = JsonConvert.DeserializeObject<T>(json, SerializerSettings);
            if (result == null)
            {
                throw new JsonSerializationException($"JSON content produced a null {typeof(T).Name}.");
            }

            return result;
        }

        private static JsonSerializerSettings CreateSettings()
        {
            var enumConverter = new StringEnumConverter
            {
                AllowIntegerValues = false
            };

            return new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = { enumConverter },
                Culture = CultureInfo.InvariantCulture,
                Formatting = Formatting.Indented,
                MissingMemberHandling = MissingMemberHandling.Error,
                NullValueHandling = NullValueHandling.Include,
                TypeNameHandling = TypeNameHandling.None
            };
        }
    }
}
