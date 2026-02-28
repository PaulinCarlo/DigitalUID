namespace DigitalUID;

// ─────────────────────────────────────────────────────────────────────────────
//  SecuGen SDK – managed wrapper types
//  Uses SecuGen.FDxSDKPro.DotNet.Windows (SGFingerPrintManager)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Device type identifiers passed to <see cref="FingerprintDevice.Open"/>.</summary>
internal enum SgDeviceType : uint
{
    FDP02    = 0,   // Hamster II
    FDP02A   = 1,   // Hamster II (USB dongle variant)
    FDU02    = 2,   // Hamster IV
    FDU04    = 3,   // Hamster III
    FDU03    = 4,   // Hamster Plus
    FDU05    = 5,   // DEX (Hamster)
    FDU07    = 7,   // Hamster Pro 10
    FDU08    = 8,   // Hamster Pro 20
    FDU09    = 9,   // Hamster Pro 20 AP
    FDU10    = 10,  // SDU03P
    FDU11    = 11,  // SDU04P
    FDU12    = 12,  // Hamster Pro Duo SC/PIV
    FDU14    = 14,  // Hamster Pro Duo CL
    Auto     = 255, // Auto-detect (recommended default)
}

/// <summary>Image-quality grades returned by <see cref="FingerprintDevice.AssessQuality"/>.</summary>
internal enum SgQuality : uint
{
    Excellent = 1,
    VeryGood  = 2,
    Good      = 3,
    Fair      = 4,
    Poor      = 5,
}

/// <summary>Matching security levels for <see cref="FingerprintDevice.MatchTemplates"/>.
/// Values correspond to <c>SGFPMSecurityLevel</c> in the managed SDK.</summary>
internal static class SgThreshold
{
    public const uint Lowest  = 1;
    public const uint Lower   = 2;
    public const uint Low     = 3;
    public const uint Normal  = 5;  // FAR ~1/1,000,000 (recommended)
    public const uint High    = 7;  // FAR ~1/100,000,000
    public const uint Higher  = 8;
    public const uint Highest = 9;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Data structs (populated by FingerprintDevice from SGFPMDeviceInfoParam)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Device information returned by <see cref="FingerprintDevice.GetDeviceInfo"/>.</summary>
internal struct SgDeviceInfo
{
    public string DevId;
    public uint ComPort;
    public uint DevType;
    public uint FWVersion;
    public string SerialNumber;
}

/// <summary>
/// Image and sensor settings returned by <see cref="FingerprintDevice.GetImageInfo"/>.
/// </summary>
internal struct SgFingerInfo
{
    public uint ImageWidth;
    public uint ImageHeight;
    public uint Brightness;
    public uint Contrast;
    public uint Gain;
    public uint Resolution;  // DPI
}
