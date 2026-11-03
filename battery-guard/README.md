# Battery Guard – by Digital Arts

लैपटॉप बैटरी की उम्र बढ़ाने वाला मुफ़्त Windows ऐप। *Made with ♥ in Andaman.*

A free Windows tray app that reminds you to unplug the charger at 80% and plug it in at 20%, so your laptop battery lasts longer.

| हिंदी | English |
|---|---|
| ![Battery Guard हिंदी](docs/screenshot-hi.png) | ![Battery Guard English](docs/screenshot-en.png) |

## फ़ीचर / Features

- बैटरी **80%** पर पहुँचने पर पॉपअप और आवाज़: "चार्जर निकाल दें"। **20%** पर: "चार्जर लगाएँ"।
- चार्जर निकालते या लगाते ही अलार्म अपने-आप बंद। "इस बार याद न दिलाएँ" और "1 घंटे के लिए रोकें" के विकल्प।
- पॉपअप फ़ोकस नहीं छीनता, इसलिए टाइपिंग में बाधा नहीं आती।
- **बैटरी हेल्थ:** नई बैटरी की क्षमता बनाम अभी की क्षमता और चार्ज साइकल। पहले WMI से पढ़ता है, न मिले तो `powercfg` से।
- Windows की पूरी बैटरी रिपोर्ट एक क्लिक में।
- ट्रे आइकन पर बैटरी का % लिखा रहता है, और चार्जर लगा हो तो पीली पट्टी दिखती है।
- **हिंदी / English** टॉगल। चुनी हुई भाषा याद रहती है।
- Windows के साथ अपने-आप चालू होता है (सिर्फ़ इस यूज़र के लिए, एडमिन की ज़रूरत नहीं)।
- "Made with ♥ in Andaman" में धड़कता दिल, और "Need software made for your business?" वाला Digital Arts कार्ड।

## ज़रूरत / Requirements

Windows 10 या 11। .NET Framework 4.8 Windows में पहले से होता है, इसलिए कुछ अलग से इंस्टॉल नहीं करना पड़ता। बनी हुई `.exe` लगभग 70 KB की है।

## बिल्ड कैसे करें / How to build

**Windows पर:** .NET SDK 6 या उससे नया, या Visual Studio 2022 में ".NET desktop development" चाहिए।

```bat
cd battery-guard\src
dotnet build -c Release
```

बनी हुई फ़ाइल यहाँ मिलेगी: `src\bin\Release\net48\BatteryGuard.exe`

**Linux पर (ऑफ़लाइन, Mono के reference assemblies से):**

```bash
sudo apt install dotnet-sdk-8.0 mono-devel
cd battery-guard/src
dotnet build -c Release
```

Linux पर `.csproj` अपने-आप `/usr/lib/mono/4.8-api` इस्तेमाल करता है।

**टेस्टिंग:** Debug बिल्ड में `BG_FAKE` से नकली बैटरी दी जा सकती है, जैसे `BG_FAKE="82,1,1"` (82%, चार्जर लगा, चार्ज हो रहा)। Release बिल्ड में यह कोड शामिल नहीं होता।

## ब्रांडिंग बदलना / Branding

सारी कंपनी जानकारी `src/Brand.cs` में है। **वेबसाइट, फ़ोन, WhatsApp और ईमेल अभी खाली हैं।** इन्हें भरकर दोबारा बिल्ड करें:

- `WhatsApp` भरने पर "Talk to Digital Arts" बटन सीधे WhatsApp चैट खोलेगा, जिसमें संदेश पहले से लिखा होगा।
- सिर्फ़ `Website` भरा हो तो बटन वेबसाइट खोलेगा।
- दोनों खाली हों तो बटन "हमारे बारे में" बॉक्स दिखाता है।

ऐप का आइकन `tools/make_icon.py` (Python + Pillow) से बनता है:

```bash
python3 tools/make_icon.py src/app.ico
```

## फ़ाइलें / Files

| फ़ाइल | काम |
|---|---|
| `src/Program.cs` | शुरुआत, एक ही इंस्टेंस चलने देना, Windows API |
| `src/MainForm.cs` | मुख्य विंडो, ट्रे, अलार्म का लॉजिक, भाषा टॉगल, धड़कता दिल, प्रोमो कार्ड |
| `src/AlertForm.cs` | कोने में आने वाला अलार्म पॉपअप |
| `src/Battery.cs` | बैटरी की स्थिति और हेल्थ (WMI / powercfg) |
| `src/AlarmSound.cs` | प्रोग्राम में ही बनने वाली अलार्म टोन |
| `src/Settings.cs` | सेटिंग्स (`%APPDATA%\DigitalArts\BatteryGuard\settings.ini`), अपने-आप चालू होना, हिंदी/English |
| `src/Brand.cs` | Digital Arts की ब्रांडिंग और रंग |

## ध्यान दें / Notes

- `.exe` डिजिटल रूप से साइन नहीं है, इसलिए Windows SmartScreen "Windows protected your PC" दिखा सकता है। बड़े पैमाने पर बाँटने के लिए Code Signing Certificate या Microsoft Store बेहतर रहेगा।
- यह फ़ोल्डर Digital Toolbox के अपडेट सिस्टम (`latest.yml` और इंस्टॉलर) से अलग है। इससे Digital Toolbox की रिलीज़ पर कोई असर नहीं पड़ता।

---

© 2026 Digital Arts · Port Blair, Andaman & Nicobar Islands · Free to use
