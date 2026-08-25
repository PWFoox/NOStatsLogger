using System.Globalization;
using System.Text;

namespace NOStatsLogger
{
    // Простейший ручной JSON-сериализатор под наш плоский объект,
    // чтобы не тащить в плагин Newtonsoft.Json как отдельную зависимость.
    internal static class TinyJson
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static string Str(string key, string value) =>
            $"\"{key}\":\"{Escape(value)}\"";

        public static string Num(string key, double value) =>
            $"\"{key}\":{value.ToString(CultureInfo.InvariantCulture)}";
    }
}
