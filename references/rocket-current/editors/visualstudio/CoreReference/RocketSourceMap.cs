using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace Rocket.VisualStudio.Core
{
    internal sealed class RocketSourceMap
    {
        internal IList<string> Sources { get; } = new List<string>();
        internal IList<string> Symbols { get; } = new List<string>();

        internal static RocketSourceMap Read(string path)
        {
            var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path)) as IDictionary<string, object>;
            if (root == null || !root.TryGetValue("format", out var format) || Convert.ToString(format) != "rocket-source-map-1")
                throw new InvalidDataException("The Rocket source map has an unsupported format.");
            var result = new RocketSourceMap();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!root.TryGetValue("functions", out var functionsObject) || !(functionsObject is IEnumerable functions)) return result;
            foreach (var functionObject in functions)
            {
                if (!(functionObject is IDictionary<string, object> function)) continue;
                AddString(function, "source", result.Sources, seen);
                if (function.TryGetValue("symbol", out var symbol) && symbol != null) result.Symbols.Add(Convert.ToString(symbol));
                if (!function.TryGetValue("locations", out var locationsObject) || !(locationsObject is IEnumerable locations)) continue;
                foreach (var locationObject in locations)
                    if (locationObject is IDictionary<string, object> location) AddString(location, "source", result.Sources, seen);
            }
            return result;
        }

        private static void AddString(IDictionary<string, object> values, string key, IList<string> destination, HashSet<string> seen)
        {
            if (!values.TryGetValue(key, out var value) || value == null) return;
            var text = Convert.ToString(value);
            if (!string.IsNullOrWhiteSpace(text) && seen.Add(text)) destination.Add(text);
        }
    }
}
