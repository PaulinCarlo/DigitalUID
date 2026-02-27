using System.Runtime.InteropServices;

namespace DigitalUID;

// ─────────────────────────────────────────────────────────────────────────────
//  SecuGen SGFPLIB – P/Invoke bindings
//  Tested against SecuGen Fingerprint SDK v4.x (SGFPLIB)
//  Native library: sgfplib.dll (Windows) / libsgfplib.so (Linux)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Device type identifiers passed to <see cref="NativeMethods.Init"/>.</summary>
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

/// <summary>Error codes returned by SGFPLIB functions. Zero means success.</summary>
internal enum SgError : uint
{
    None               = 0,
    CreationFailed     = 1,
    FunctionFailed     = 2,
    InvalidParam       = 3,
    NotInitialized     = 4,
    AlreadyInitialized = 5,
    DeviceNotFound     = 6,
    DeviceBusy         = 7,
    Timeout            = 8,
    InvalidDevice      = 9,
    ChangeSettings     = 10,
    WrongImage         = 11,
    LackOfBandwidth    = 12,
    MemoryFailed       = 13,
    SysFileFailed      = 14,
    TamperAlert        = 15,
    IniFileFailed      = 16,
    TemplateZero       = 17,
}

/// <summary>Image-quality grades returned by <see cref="NativeMethods.GetImageQuality"/>.</summary>
internal enum SgQuality : uint
{
    Excellent = 1,
    VeryGood  = 2,
    Good      = 3,
    Fair      = 4,
    Poor      = 5,
}

/// <summary>Matching score thresholds for <see cref="NativeMethods.MatchTemplate"/>.</summary>
internal static class SgThreshold
{
    public const uint Lowest  = 1;
    public const uint Lower   = 5;
    public const uint Low     = 10;
    public const uint Normal  = 14;  // FAR 1/1,000,000  (recommended)
    public const uint High    = 21;  // FAR 1/100,000,000
    public const uint Higher  = 27;
    public const uint Highest = 36;
}

// ─────────────────────────────────────────────────────────────────────────────
//  Marshalled structs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Device information filled by <see cref="NativeMethods.GetDeviceInfo"/>.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
internal struct SgDeviceInfo
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 25)]
    public string DevId;

    public uint ComPort;
    public uint DevType;
    public uint FWVersion;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 25)]
    public string SerialNumber;
}

/// <summary>
/// Image and sensor settings filled by <see cref="NativeMethods.GetImageInfo"/>
/// and supplied to <see cref="NativeMethods.CreateTemplate"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SgFingerInfo
{
    public uint ImageWidth;
    public uint ImageHeight;
    public uint Brightness;
    public uint Contrast;
    public uint Gain;
    public uint Resolution;  // DPI
}

// ─────────────────────────────────────────────────────────────────────────────
//  P/Invoke declarations  (DllImport – handles all marshalling scenarios)
// ─────────────────────────────────────────────────────────────────────────────

internal static class NativeMethods
{
    // The library name differs by OS.  The runtime resolves the platform suffix.
    private const string Lib = "sgfplib";

    // ── Life-cycle ────────────────────────────────────────────────────────

    /// <summary>Allocates a new fingerprint-manager handle.</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_Create")]
    internal static extern uint Create(out IntPtr handle);

    /// <summary>Initialises the specified device type (must call before OpenDevice).</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_Init")]
    internal static extern uint Init(IntPtr handle, uint deviceType);

    /// <summary>Opens the physical USB device (dwDevId = 0 for the first device).</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_OpenDevice")]
    internal static extern uint OpenDevice(IntPtr handle, uint deviceId);

    /// <summary>Closes the physical USB device.</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_CloseDevice")]
    internal static extern uint CloseDevice(IntPtr handle);

    /// <summary>Releases the fingerprint-manager handle allocated by Create.</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_Terminate")]
    internal static extern uint Terminate(IntPtr handle);

    // ── Device information ────────────────────────────────────────────────

    /// <summary>Returns device model, firmware version and serial number.</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_GetDeviceInfo")]
    internal static extern uint GetDeviceInfo(IntPtr handle, ref SgDeviceInfo info);

    /// <summary>Returns sensor image dimensions, brightness, contrast, gain and DPI.</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_GetImageInfo")]
    internal static extern uint GetImageInfo(IntPtr handle, ref SgFingerInfo info);

    // ── Sensor tuning ─────────────────────────────────────────────────────

    /// <summary>Sets the LED brightness (0–255, 128 = default).</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_SetBrightness")]
    internal static extern uint SetBrightness(IntPtr handle, uint brightness);

    /// <summary>Sets the analogue gain (0–255, 128 = default).</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_SetGain")]
    internal static extern uint SetGain(IntPtr handle, uint gain);

    // ── Image capture ─────────────────────────────────────────────────────

    /// <summary>
    /// Captures a raw greyscale image into <paramref name="imageBuffer"/>.
    /// The buffer must be at least <c>ImageWidth x ImageHeight</c> bytes
    /// (obtained from <see cref="GetImageInfo"/>).
    /// </summary>
    [DllImport(Lib, EntryPoint = "SGFPM_GetImage")]
    internal static extern uint GetImage(IntPtr handle, byte[] imageBuffer);

    /// <summary>
    /// Scores the quality of the captured image.
    /// Returns a <see cref="SgQuality"/> value (1 = Excellent ... 5 = Poor).
    /// </summary>
    [DllImport(Lib, EntryPoint = "SGFPM_GetImageQuality")]
    internal static extern uint GetImageQuality(
        IntPtr handle, uint width, uint height,
        byte[] imageBuffer, out uint quality);

    // ── Template operations ───────────────────────────────────────────────

    /// <summary>Returns the actual template size in bytes for the open device.</summary>
    [DllImport(Lib, EntryPoint = "SGFPM_GetTemplateSize")]
    internal static extern uint GetTemplateSize(IntPtr handle, out uint templateSize);

    /// <summary>
    /// Extracts a fingerprint template (minutiae) from a previously captured image.
    /// </summary>
    [DllImport(Lib, EntryPoint = "SGFPM_CreateTemplate")]
    internal static extern uint CreateTemplate(
        IntPtr handle, ref SgFingerInfo info,
        byte[] imageBuffer, byte[] templateBuffer);

    /// <summary>
    /// Matches two templates.  Returns <c>true</c> in <paramref name="matched"/>
    /// when the score meets or exceeds <paramref name="threshold"/>.
    /// Use <see cref="SgThreshold"/> constants for <paramref name="threshold"/>.
    /// </summary>
    [DllImport(Lib, EntryPoint = "SGFPM_MatchTemplate")]
    internal static extern uint MatchTemplate(
        IntPtr handle, byte[] template1, byte[] template2,
        uint threshold, [MarshalAs(UnmanagedType.Bool)] out bool matched);

    // ── Fake-finger (liveness) detection ─────────────────────────────────

    /// <summary>
    /// Reads the liveness / fake-finger detection value.
    /// Not all devices support this feature.
    /// </summary>
    [DllImport(Lib, EntryPoint = "SGFPM_GetFakeDetectInfo")]
    internal static extern uint GetFakeDetectInfo(IntPtr handle, out uint fakeInfo);
}
