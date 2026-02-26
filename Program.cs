using System.Text;
using DigitalUID;

// ═══════════════════════════════════════════════════════════════════════════
//  DigitalUID – SecuGen Fingerprint Reader Demo
//  Demonstrates every capability of the SecuGen SGFPLIB SDK:
//    • Device discovery and detailed diagnostics if no reader is found
//    • Device information (model, firmware, serial, resolution)
//    • Sensor tuning (brightness / gain)
//    • Image capture with ASCII-art preview
//    • Image quality assessment
//    • Template (minutiae) extraction with statistics
//    • 1 : 1 matching at multiple security thresholds
//    • Liveness / fake-finger detection
//    • BMP file export of captured images
// ═══════════════════════════════════════════════════════════════════════════

Console.OutputEncoding = Encoding.UTF8;
PrintBanner();

// ─── 1. Open the device (detailed error if not found) ────────────────────
FingerprintDevice device;
try
{
    Console.WriteLine("Connecting to SecuGen fingerprint reader …\n");
    device = FingerprintDevice.Open(SgDeviceType.Auto);
}
catch (SecuGenException ex)
{
    PrintError("Device initialisation failed", ex.Message);
    return 1;
}
catch (Exception ex)
{
    PrintError("Unexpected error", ex.ToString());
    return 1;
}

using (device)
{
    // ─── 2. Device information ────────────────────────────────────────────
    Section("DEVICE INFORMATION");
    var devInfo   = device.GetDeviceInfo();
    var imageInfo = device.GetImageInfo();

    PrintKV("Device ID",        devInfo.DevId);
    PrintKV("Device Type",      FormatDeviceType(devInfo.DevType));
    PrintKV("Firmware Version", FormatFirmware(devInfo.FWVersion));
    PrintKV("Serial Number",    string.IsNullOrEmpty(devInfo.SerialNumber) ? "(not available)" : devInfo.SerialNumber);
    PrintKV("Image Size",       $"{imageInfo.ImageWidth} × {imageInfo.ImageHeight} px");
    PrintKV("Resolution",       $"{imageInfo.Resolution} DPI");
    PrintKV("Brightness",       imageInfo.Brightness.ToString());
    PrintKV("Contrast",         imageInfo.Contrast.ToString());
    PrintKV("Gain",             imageInfo.Gain.ToString());

    uint templateSize = device.GetTemplateSize();
    PrintKV("Template Size",    $"{templateSize} bytes");

    // ─── 3. Sensor tuning ────────────────────────────────────────────────
    Section("SENSOR TUNING");
    Console.WriteLine("Applying default brightness (128) and gain (128) …");
    device.SetBrightness(128);
    device.SetGain(128);
    Console.WriteLine("[OK] Sensor settings applied.\n");

    // ─── 4. First fingerprint capture ────────────────────────────────────
    Section("FINGERPRINT CAPTURE – SCAN 1");
    byte[] image1 = CaptureWithPrompt(device, 1);

    // ─── 5. Quality assessment ────────────────────────────────────────────
    Section("IMAGE QUALITY ASSESSMENT");
    var (quality1, desc1) = device.AssessQuality(image1);
    PrintKV("Quality Grade", $"{quality1} ({(uint)quality1}/5)");
    PrintKV("Assessment",    desc1);
    PrintQualityBar(quality1);

    // ─── 6. ASCII-art preview ─────────────────────────────────────────────
    Section("FINGERPRINT ASCII PREVIEW (Scan 1)");
    PrintAsciiFingerprint(image1, imageInfo.ImageWidth, imageInfo.ImageHeight, previewWidth: 64);

    // ─── 7. Template extraction ───────────────────────────────────────────
    Section("TEMPLATE EXTRACTION – SCAN 1");
    byte[] template1 = device.CreateTemplate(image1);
    PrintKV("Template Length", $"{template1.Length} bytes");
    PrintKV("Non-zero bytes",  $"{Array.FindAll(template1, b => b != 0).Length}");
    PrintKV("Hex preview",     BytesToHexPreview(template1, 24));

    // ─── 8. Liveness detection ────────────────────────────────────────────
    Section("LIVENESS / FAKE-FINGER DETECTION");
    uint? livenessVal = device.GetFakeDetectInfo();
    if (livenessVal is null)
        Console.WriteLine("  This device does not support liveness detection.\n");
    else
        PrintKV("Liveness Value", $"{livenessVal} (0 = live finger)");

    // ─── 9. Second capture for matching ───────────────────────────────────
    Section("FINGERPRINT CAPTURE – SCAN 2  (for 1:1 matching)");
    byte[] image2 = CaptureWithPrompt(device, 2);

    var (quality2, desc2) = device.AssessQuality(image2);
    PrintKV("Quality Grade", $"{quality2} ({(uint)quality2}/5)");
    PrintKV("Assessment",    desc2);

    Section("FINGERPRINT ASCII PREVIEW (Scan 2)");
    PrintAsciiFingerprint(image2, imageInfo.ImageWidth, imageInfo.ImageHeight, previewWidth: 64);

    byte[] template2 = device.CreateTemplate(image2);

    // ─── 10. 1:1 matching at multiple thresholds ─────────────────────────
    Section("FINGERPRINT MATCHING  (Scan 1 vs Scan 2)");
    MatchAt("Lowest  (FAR ≈ 1/10)",          template1, template2, SgThreshold.Lowest,  device);
    MatchAt("Normal  (FAR ≈ 1/1,000,000)",   template1, template2, SgThreshold.Normal,  device);
    MatchAt("High    (FAR ≈ 1/100,000,000)", template1, template2, SgThreshold.High,    device);
    MatchAt("Highest (most strict)",          template1, template2, SgThreshold.Highest, device);

    // ─── 11. BMP export ───────────────────────────────────────────────────
    Section("BMP EXPORT");
    string path1 = Path.Combine(AppContext.BaseDirectory, "scan1.bmp");
    string path2 = Path.Combine(AppContext.BaseDirectory, "scan2.bmp");
    SaveBmp(image1, imageInfo.ImageWidth, imageInfo.ImageHeight, path1);
    SaveBmp(image2, imageInfo.ImageWidth, imageInfo.ImageHeight, path2);
    Console.WriteLine($"  Scan 1 → {path1}");
    Console.WriteLine($"  Scan 2 → {path2}");
    Console.WriteLine();

    // ─── Done ─────────────────────────────────────────────────────────────
    Section("DEMO COMPLETE");
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine("  All SecuGen capabilities demonstrated successfully.");
    Console.ResetColor();
    Console.WriteLine();
}

return 0;

// ═══════════════════════════════════════════════════════════════════════════
//  Helper methods
// ═══════════════════════════════════════════════════════════════════════════

static byte[] CaptureWithPrompt(FingerprintDevice device, int scanNumber)
{
    while (true)
    {
        Console.Write($"  Place your finger on the sensor and press [Enter] (Scan {scanNumber}) … ");
        Console.ReadLine();
        try
        {
            byte[] image = device.CaptureImage();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  [OK] Scan {scanNumber} captured ({image.Length:N0} bytes).\n");
            Console.ResetColor();
            return image;
        }
        catch (SecuGenException ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"  [RETRY] Capture failed: {ex.Message}");
            Console.ResetColor();
        }
    }
}

static void MatchAt(string label, byte[] t1, byte[] t2, uint threshold, FingerprintDevice device)
{
    bool matched = device.MatchTemplates(t1, t2, threshold);
    Console.ForegroundColor = matched ? ConsoleColor.Green : ConsoleColor.Red;
    Console.Write($"  {(matched ? "MATCH   ✓" : "NO MATCH ✗")}");
    Console.ResetColor();
    Console.WriteLine($"  Threshold: {label}  (value={threshold})");
}

static void PrintAsciiFingerprint(byte[] image, uint w, uint h, int previewWidth)
{
    // Scale down to previewWidth x proportional height, map greyscale to ASCII
    const string shades = "@#S%?*+;:, ";   // dark to light
    const int MaxPixelValue = 255;          // maximum 8-bit greyscale value
    int pw = Math.Min(previewWidth, (int)w);
    int ph = (int)(h * pw / w / 2);        // divide by 2 because chars are ~2× taller than wide

    for (int row = 0; row < ph; row++)
    {
        Console.Write("  ");
        for (int col = 0; col < pw; col++)
        {
            int srcX = (int)(col * w / pw);
            int srcY = (int)(row * h / ph);
            byte pixel = image[srcY * w + srcX];
            char ch = shades[pixel * (shades.Length - 1) / MaxPixelValue];
            Console.Write(ch);
        }
        Console.WriteLine();
    }
    Console.WriteLine();
}

static void PrintQualityBar(SgQuality quality)
{
    int filled = quality switch
    {
        SgQuality.Excellent => 5,
        SgQuality.VeryGood  => 4,
        SgQuality.Good      => 3,
        SgQuality.Fair      => 2,
        SgQuality.Poor      => 1,
        _                   => 0,
    };
    Console.ForegroundColor = filled >= 3 ? ConsoleColor.Green : ConsoleColor.Yellow;
    Console.Write("  Quality: [");
    Console.Write(new string('█', filled));
    Console.Write(new string('░', 5 - filled));
    Console.WriteLine("]");
    Console.ResetColor();
    Console.WriteLine();
}

static void SaveBmp(byte[] pixels, uint width, uint height, string path)
{
    // Greyscale BMP (8-bit palette)
    const int paletteSize = 256 * 4;
    const int headerSize  = 54 + paletteSize;
    uint stride = (width + 3) & ~3u;        // rows padded to 4-byte boundary
    uint pixelDataSize = stride * height;
    uint fileSize = (uint)(headerSize + pixelDataSize);

    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
    using var bw = new BinaryWriter(fs);

    // BITMAPFILEHEADER
    bw.Write((ushort)0x4D42);               // 'BM'
    bw.Write(fileSize);
    bw.Write((uint)0);                      // reserved
    bw.Write((uint)headerSize);             // pixel data offset

    // BITMAPINFOHEADER
    bw.Write((uint)40);                     // header size
    bw.Write((int)width);
    bw.Write(-(int)height);                 // negative = top-down
    bw.Write((ushort)1);                    // colour planes
    bw.Write((ushort)8);                    // bits-per-pixel
    bw.Write((uint)0);                      // no compression
    bw.Write(pixelDataSize);
    bw.Write((int)2835); bw.Write((int)2835); // ~72 DPI
    bw.Write((uint)256);                    // palette colours
    bw.Write((uint)0);                      // all important

    // Greyscale palette
    for (int i = 0; i < 256; i++)
    {
        bw.Write((byte)i); bw.Write((byte)i); bw.Write((byte)i); bw.Write((byte)0);
    }

    // Pixel data (padded rows, bottom-up already reversed via negative height)
    byte[] pad = new byte[stride - width];
    for (uint row = 0; row < height; row++)
    {
        bw.Write(pixels, (int)(row * width), (int)width);
        bw.Write(pad);
    }
}

// ── Display helpers ──────────────────────────────────────────────────────────

static void PrintBanner()
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("""
  ╔══════════════════════════════════════════════════════════╗
  ║          D I G I T A L U I D  –  F i n g e r p r i n t ║
  ║               S e c u G e n   S D K   D e m o           ║
  ╚══════════════════════════════════════════════════════════╝
  """);
    Console.ResetColor();
}

static void Section(string title)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"  ── {title} {new string('─', Math.Max(0, 52 - title.Length))}");
    Console.ResetColor();
}

static void PrintKV(string key, string value)
    => Console.WriteLine($"  {key,-22}: {value}");

static void PrintError(string title, string message)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"\n  ✖ {title}");
    Console.ResetColor();
    Console.Error.WriteLine();

    // Indent each line of the message for readability
    foreach (string line in message.Split('\n'))
        Console.Error.WriteLine($"    {line.TrimEnd()}");

    Console.Error.WriteLine();
}

static string FormatDeviceType(uint type) => type switch
{
    0   => "Hamster II (FDP02)",
    1   => "Hamster II USB Dongle (FDP02A)",
    2   => "Hamster IV (FDU02)",
    3   => "Hamster III (FDU04)",
    4   => "Hamster Plus (FDU03)",
    5   => "DEX / Hamster (FDU05)",
    7   => "Hamster Pro 10 (FDU07)",
    8   => "Hamster Pro 20 (FDU08)",
    9   => "Hamster Pro 20 AP (FDU09)",
    10  => "SDU03P (FDU10)",
    11  => "SDU04P (FDU11)",
    12  => "Hamster Pro Duo SC/PIV (FDU12)",
    14  => "Hamster Pro Duo CL (FDU14)",
    _   => $"Unknown ({type})",
};

static string FormatFirmware(uint fw)
    => fw == 0 ? "(not available)" : $"{fw >> 8}.{fw & 0xFF:D2}";

static string BytesToHexPreview(byte[] data, int count)
{
    var sb = new StringBuilder();
    int take = Math.Min(count, data.Length);
    for (int i = 0; i < take; i++)
    {
        if (i > 0 && i % 8 == 0) sb.Append(' ');
        sb.Append(data[i].ToString("X2")).Append(' ');
    }
    if (data.Length > count) sb.Append("…");
    return sb.ToString().TrimEnd();
}

