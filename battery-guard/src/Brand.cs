using System.Drawing;

namespace DigitalArts.BatteryGuard
{
    /// <summary>
    /// Digital Arts की सारी ब्रांडिंग यहीं है। असली वेबसाइट / फ़ोन / WhatsApp
    /// नंबर यहाँ डालकर दोबारा बिल्ड करें।
    /// </summary>
    static class Brand
    {
        public const string AppName     = "Battery Guard";
        public const string Company     = "Digital Arts";
        public const string Tagline     = "हम वेबसाइट, मोबाइल ऐप और सॉफ्टवेयर बनाते हैं";
        public const string TaglineEn   = "We build websites, mobile apps & software";
        public const string Location    = "Port Blair, Andaman & Nicobar Islands";

        // TODO: असली जानकारी डालें। खाली छोड़ी गई चीज़ ऐप में नहीं दिखेगी।
        public static readonly string Website = "";   // जैसे "https://www.yoursite.com"
        public static readonly string Phone = "";   // जैसे "+91 98765 43210"
        public static readonly string WhatsApp = "";   // देश-कोड सहित, बिना + और स्पेस के, जैसे "919876543210"
        public static readonly string Email = "";

        public const string WhatsAppMsg = "नमस्ते Digital Arts, मैंने Battery Guard इस्तेमाल किया। मुझे अपना ऐप / वेबसाइट / सॉफ्टवेयर बनवाना है।";
        public const string WhatsAppMsgEn = "Hello Digital Arts, I used Battery Guard. I would like to get an app / website / software built.";

        public static readonly Color Primary   = Color.FromArgb(31, 58, 147);   // गहरा नीला
        public static readonly Color PrimaryDk = Color.FromArgb(20, 38, 100);
        public static readonly Color Good      = Color.FromArgb(16, 150, 100);  // हरा
        public static readonly Color Warn      = Color.FromArgb(230, 126, 34);  // नारंगी
        public static readonly Color Bad       = Color.FromArgb(211, 47, 47);   // लाल
        public static readonly Color Muted     = Color.FromArgb(100, 110, 125);
        public static readonly Color Surface   = Color.FromArgb(246, 248, 252);

        public const string FontName = "Nirmala UI"; // Windows 8+ में हिंदी के लिए
    }
}
