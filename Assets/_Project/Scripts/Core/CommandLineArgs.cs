using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlueHorizon.Core
{
    /// <summary>
    /// Parses "-key value" and "-flag" style arguments. Keys are case-insensitive and stored without the dash.
    /// </summary>
    public sealed class CommandLineArgs
    {
        readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        public CommandLineArgs(IReadOnlyList<string> args)
        {
            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                if (!IsKey(arg))
                    continue;

                string key = arg.TrimStart('-');
                string value = "";
                if (i + 1 < args.Count && !IsKey(args[i + 1]))
                {
                    value = args[i + 1];
                    i++;
                }
                _values[key] = value;
            }
        }

        static bool IsKey(string s)
        {
            // Negative numbers are values, not keys.
            return !string.IsNullOrEmpty(s) && s.Length > 1 && s[0] == '-' && !char.IsDigit(s[1]) && s[1] != '.';
        }

        public static CommandLineArgs FromEnvironment() => new(Environment.GetCommandLineArgs());

        public bool Has(string key) => _values.ContainsKey(key);

        public string GetString(string key, string fallback) =>
            _values.TryGetValue(key, out string v) && !string.IsNullOrEmpty(v) ? v : fallback;

        public int GetInt(string key, int fallback) =>
            _values.TryGetValue(key, out string v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : fallback;

        public float GetFloat(string key, float fallback) =>
            _values.TryGetValue(key, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : fallback;
    }
}
