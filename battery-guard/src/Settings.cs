using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DigitalArts.BatteryGuard
{
    static class Settings
    {
        public static int  High = 80;          // इतने % पर "चार्जर निकालें"
        public static int  Low = 20;           // इतने % पर "चार्जर लगाएँ"
        public static int  RepeatMinutes = 5;  // अलार्म दोबारा कितने मिनट बाद
        public static bool Sound = true;
        public static bool AutoStart = true;
        public static bool FirstRun = true;
        public static bool TrayHintShown = false;
        public static string Language = "hi";  // "hi" = हिंदी, "en" = English

        static string Dir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DigitalArts", "BatteryGuard");
        static string FilePath => Path.Combine(Dir, "settings.ini");

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) map[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
                High          = Int(map, "High", High, 50, 100);
                Low           = Int(map, "Low", Low, 5, 50);
                RepeatMinutes = Int(map, "RepeatMinutes", RepeatMinutes, 1, 60);
                Sound         = Bool(map, "Sound", Sound);
                AutoStart     = Bool(map, "AutoStart", AutoStart);
                TrayHintShown = Bool(map, "TrayHintShown", TrayHintShown);
                if (map.TryGetValue("Language", out var lang)) Language = lang == "en" ? "en" : "hi";
                FirstRun = false;
                if (Low >= High - 5) { High = 80; Low = 20; }
            }
            catch { /* खराब फ़ाइल हो तो डिफ़ॉल्ट से चलें */ }
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var sb = new StringBuilder();
                sb.AppendLine("# Battery Guard by Digital Arts");
                sb.AppendLine("High=" + High);
                sb.AppendLine("Low=" + Low);
                sb.AppendLine("RepeatMinutes=" + RepeatMinutes);
                sb.AppendLine("Sound=" + Sound);
                sb.AppendLine("AutoStart=" + AutoStart);
                sb.AppendLine("TrayHintShown=" + TrayHintShown);
                sb.AppendLine("Language=" + Language);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
                FirstRun = false;
            }
            catch { }
        }

        static int Int(Dictionary<string, string> m, string k, int def, int min, int max)
            => m.TryGetValue(k, out var v) && int.TryParse(v, out var n) ? Math.Max(min, Math.Min(max, n)) : def;

        static bool Bool(Dictionary<string, string> m, string k, bool def)
            => m.TryGetValue(k, out var v) && bool.TryParse(v, out var b) ? b : def;
    }

    /// <summary>हिंदी / English अनुवाद।</summary>
    static class Lang
    {
        public static bool En => Settings.Language == "en";
        public static string T(string hi, string en) => En ? en : hi;
    }

    static class AutoStartHelper
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "DigitalArtsBatteryGuard";

        /// <summary>Windows शुरू होने पर ऐप अपने-आप चालू हो (सिर्फ़ इस यूज़र के लिए, एडमिन की ज़रूरत नहीं)।</summary>
        public static void Apply(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" --minimized");
                    else key.DeleteValue(ValueName, false);
                }
            }
            catch { }
        }
    }
}
