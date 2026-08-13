using System;

namespace HanziDefend.Data
{
    public sealed class ConfigLoadException : Exception
    {
        public ConfigLoadException(string message)
            : base(message)
        {
        }

        public ConfigLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
