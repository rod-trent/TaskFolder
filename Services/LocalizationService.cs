using System;
using System.Globalization;
using System.Resources;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace TaskFolder.Services
{
    /// <summary>S3 resource contract; remaining application surfaces are localized in S8.</summary>
    internal static class LocalizationService
    {
        private static readonly ResourceManager Resources = new ResourceManager("TaskFolder.Resources.Strings", typeof(LocalizationService).Assembly);
        internal static string CurrentLanguage { get; set; } = "English";
        internal static string Text(string english, params object[] arguments)
        {
            string key = "Ui_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(english))).Substring(0, 16);
            var culture = CurrentLanguage == "Русский" ? CultureInfo.GetCultureInfo("ru") : CurrentLanguage == "English" ? CultureInfo.GetCultureInfo("en") : CultureInfo.CurrentUICulture;
            string value = Resources.GetString(key, culture) ?? english;
            return arguments.Length == 0 ? value : string.Format(culture, value, arguments);
        }
        internal static string Plural(string prefix, long count, string language)
        {
            bool ru = language == "Русский" || language == "System" && CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru";
            string suffix = ru ? count % 10 == 1 && count % 100 != 11 ? "One" : count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 12 || count % 100 > 14) ? "Few" : "Many" : count == 1 ? "One" : "Many";
            return string.Format(Get(prefix + suffix, language), count);
        }
        internal static IEnumerable<KeyValuePair<string, string>> Entries(string culture)
        {
            var set = Resources.GetResourceSet(CultureInfo.GetCultureInfo(culture), true, true);
            foreach (System.Collections.DictionaryEntry entry in set) yield return new KeyValuePair<string, string>((string)entry.Key, (string)entry.Value);
        }
        internal static string Relocalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var match = Entries("en").Concat(Entries("ru")).FirstOrDefault(entry => entry.Value == text);
            return match.Key == null ? text : Get(match.Key, CurrentLanguage);
        }
        internal static string Get(string key, string language)
        {
            var culture = language == "Русский" ? CultureInfo.GetCultureInfo("ru") : language == "English"
                ? CultureInfo.GetCultureInfo("en") : CultureInfo.CurrentUICulture;
            return Resources.GetString(key, culture) ?? throw new InvalidOperationException("Missing UI resource: " + key);
        }
    }
}
