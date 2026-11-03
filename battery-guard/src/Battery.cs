using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows.Forms;
using System.Xml;

namespace DigitalArts.BatteryGuard
{
    struct BatterySnapshot
    {
        public bool HasBattery;
        public int Percent;          // 0-100, या -1 अगर पता नहीं
        public bool PluggedIn;
        public bool Charging;
        public int SecondsLeft;      // -1 अगर पता नहीं

        public static BatterySnapshot Read()
        {
            var s = new BatterySnapshot();
#if DEBUG
            // टेस्टिंग: BG_FAKE="76,1,1"  => 76%, चार्जर लगा, चार्ज हो रहा
            var fake = Environment.GetEnvironmentVariable("BG_FAKE");
            if (!string.IsNullOrEmpty(fake))
            {
                var p = fake.Split(',');
                s.HasBattery = true; s.Percent = int.Parse(p[0]);
                s.PluggedIn = p.Length > 1 && p[1] == "1"; s.Charging = p.Length > 2 && p[2] == "1";
                s.SecondsLeft = s.PluggedIn ? -1 : 8100;
                return s;
            }
#endif
            PowerStatus ps;
            try { ps = SystemInformation.PowerStatus; }
            catch { s.Percent = -1; s.SecondsLeft = -1; return s; }
            var cs = ps.BatteryChargeStatus;
            s.HasBattery = (cs & BatteryChargeStatus.NoSystemBattery) == 0 && cs != BatteryChargeStatus.Unknown;
            float f = ps.BatteryLifePercent;              // 0.0 - 1.0 ; पता न हो तो 2.55
            s.Percent = f >= 0f && f <= 1f ? (int)Math.Round(f * 100) : -1;
            s.PluggedIn = ps.PowerLineStatus == PowerLineStatus.Online;
            s.Charging = (cs & BatteryChargeStatus.Charging) != 0;
            s.SecondsLeft = ps.BatteryLifeRemaining;
            if (s.Percent < 0) s.HasBattery = false;
            return s;
        }
    }

    class HealthInfo
    {
        public long DesignCapacity;      // mWh
        public long FullChargeCapacity;  // mWh
        public int CycleCount = -1;
        public bool Ok => DesignCapacity > 0 && FullChargeCapacity > 0;
        public double Percent => Ok ? Math.Min(100.0, FullChargeCapacity * 100.0 / DesignCapacity) : 0;

        public string Verdict
        {
            get
            {
                if (!Ok) return Lang.T("जानकारी उपलब्ध नहीं", "Not available");
                double p = Percent;
                if (p >= 80) return Lang.T("बढ़िया – बैटरी अच्छी हालत में है", "Good – battery is in good condition");
                if (p >= 60) return Lang.T("ठीक-ठाक – थोड़ी कमज़ोर हो चुकी है", "Fair – battery has worn a little");
                return Lang.T("कमज़ोर – बदलने पर विचार करें", "Weak – consider replacing it");
            }
        }
    }

    static class BatteryHealth
    {
        /// <summary>पहले WMI से, न मिले तो powercfg रिपोर्ट से बैटरी की क्षमता पढ़ता है।</summary>
        public static HealthInfo Read()
        {
            var h = new HealthInfo();
            try { ReadWmi(h); } catch { }
            if (!h.Ok)
            {
                try { ReadPowercfg(h); } catch { }
            }
            return h;
        }

        static void ReadWmi(HealthInfo h)
        {
            long design = 0, full = 0; int cycles = -1;
            using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData"))
                foreach (ManagementObject o in s.Get())
                    using (o) design += Convert.ToInt64(o["DesignedCapacity"]);
            using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                foreach (ManagementObject o in s.Get())
                    using (o) full += Convert.ToInt64(o["FullChargedCapacity"]);
            try
            {
                using (var s = new ManagementObjectSearcher(@"root\WMI", "SELECT CycleCount FROM BatteryCycleCount"))
                    foreach (ManagementObject o in s.Get())
                        using (o) { int c = Convert.ToInt32(o["CycleCount"]); if (c > 0) cycles = Math.Max(cycles, 0) + c; }
            }
            catch { }
            h.DesignCapacity = design; h.FullChargeCapacity = full; h.CycleCount = cycles;
        }

        static void ReadPowercfg(HealthInfo h)
        {
            string xmlPath = Path.Combine(Path.GetTempPath(), "DigitalArts_battery_report.xml");
            if (!RunPowercfg("/batteryreport /xml /output \"" + xmlPath + "\"") || !File.Exists(xmlPath)) return;
            var doc = new XmlDocument();
            doc.Load(xmlPath);
            long design = 0, full = 0; int cycles = -1;
            foreach (XmlNode b in doc.SelectNodes("//*[local-name()='Battery']"))
            {
                design += ParseLong(b.SelectSingleNode("*[local-name()='DesignCapacity']"));
                full   += ParseLong(b.SelectSingleNode("*[local-name()='FullChargeCapacity']"));
                long c = ParseLong(b.SelectSingleNode("*[local-name()='CycleCount']"));
                if (c > 0) cycles = (int)(Math.Max(cycles, 0) + c);
            }
            h.DesignCapacity = design; h.FullChargeCapacity = full;
            if (h.CycleCount <= 0) h.CycleCount = cycles;
            try { File.Delete(xmlPath); } catch { }
        }

        static long ParseLong(XmlNode n) => n != null && long.TryParse(n.InnerText.Trim(), out var v) ? v : 0;

        /// <summary>Windows की पूरी बैटरी रिपोर्ट (HTML) बनाकर खोलता है।</summary>
        public static string CreateFullReport()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Battery-Report.html");
            return RunPowercfg("/batteryreport /output \"" + path + "\"") && File.Exists(path) ? path : null;
        }

        static bool RunPowercfg(string args)
        {
            var psi = new ProcessStartInfo("powercfg.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var p = Process.Start(psi))
            {
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                return p.WaitForExit(20000) && p.ExitCode == 0;
            }
        }
    }
}
