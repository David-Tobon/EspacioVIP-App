using System;
using System.Globalization;
using System.Text;

namespace Espacio_VIP_SL_App.Utils
{
    public static class MoneyUtil
    {
        public static bool TryParseDecimalFlexible(string? input, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(input)) return false;

            var s = input.Trim().Replace(" ", "");

            // Normalización:
            // - Si tiene ',' y '.', tomamos como separador decimal el último que aparezca.
            // - Eliminamos el otro (lo tratamos como ruido / separador raro).
            int lastDot = s.LastIndexOf('.');
            int lastComma = s.LastIndexOf(',');

            char decSep = '\0';
            if (lastDot >= 0 || lastComma >= 0)
            {
                decSep = (lastDot > lastComma) ? '.' : ',';
            }

            var sb = new StringBuilder();
            bool hasDec = false;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (char.IsDigit(c))
                {
                    sb.Append(c);
                    continue;
                }

                if ((c == '.' || c == ',') && decSep != '\0' && c == decSep && !hasDec)
                {
                    sb.Append('.'); // usamos '.' internamente para InvariantCulture
                    hasDec = true;
                    continue;
                }

                if (c == '-' && sb.Length == 0)
                {
                    sb.Append('-');
                    continue;
                }

                // todo lo demás se ignora
            }

            var normalized = sb.ToString();
            if (normalized == "" || normalized == "-" || normalized == ".") return false;

            return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        public static long ToCents(object? value)
        {
            if (value == null) return 0;

            if (value is long l) return l;
            if (value is int i) return i;

            if (value is decimal d)
                return (long)Math.Round(d * 100m, MidpointRounding.AwayFromZero);

            if (value is double db)
                return (long)Math.Round((decimal)db * 100m, MidpointRounding.AwayFromZero);

            var s = value.ToString();
            if (!TryParseDecimalFlexible(s, out var dec)) return 0;

            return (long)Math.Round(dec * 100m, MidpointRounding.AwayFromZero);
        }

        public static decimal ToEuros(long cents) => cents / 100m;

        public static string EurosString(long cents) =>
            ToEuros(cents).ToString("0.00", CultureInfo.InvariantCulture); // muestra con punto

        public static string UiDate(DateTime dt) => dt.ToString("dd/MM/yyyy");
        public static string DbDate(DateTime dt) => dt.ToString("yyyy-MM-dd");

        public static bool TryParseDate(object? value, out string dbDate)
        {
            dbDate = "";
            if (value == null) return false;

            if (value is DateTime dt)
            {
                dbDate = DbDate(dt);
                return true;
            }

            var s = (value.ToString() ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return false;

            // Acepta dd/MM/yyyy o yyyy-MM-dd (y variantes)
            if (DateTime.TryParseExact(s, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ||
                DateTime.TryParseExact(s, "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ||
                DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) ||
                DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
            {
                dbDate = DbDate(dt);
                return true;
            }

            return false;
        }

        public static DateTime DbToDateTime(string dbDate)
        {
            if (DateTime.TryParseExact(dbDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            if (DateTime.TryParse(dbDate, out dt)) return dt;
            return DateTime.Today;
        }
    }
}
