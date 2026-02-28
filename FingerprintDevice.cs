using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using SecuGen.FDxSDKPro.Windows;

namespace DigitalUID;

/// <summary>
/// High-level managed wrapper around the SecuGen SDK (SGFingerPrintManager).
/// Implements <see cref="IDisposable"/> – use inside a <c>using</c> block or
/// call <see cref="Dispose"/> to release the device.
/// </summary>
internal sealed class FingerprintDevice : IDisposable
{
    private SGFingerPrintManager? _sgfpm;
    private bool _deviceOpen;
    private bool _disposed;

    // ── Construction / Teardown ───────────────────────────────────────────

    private FingerprintDevice() { }

    /// <summary>
    /// Attempts to open the SecuGen device.
    /// Returns a ready-to-use <see cref="FingerprintDevice"/> on success.
    /// On failure, throws <see cref="SecuGenException"/> with a descriptive
    /// message that includes SDK-installation hints.
    /// </summary>
    /// <param name="deviceType">
    /// Pass <see cref="SgDeviceType.Auto"/> (the default) to let the SDK
    /// detect the model automatically.
    /// </param>
    /// <param name="deviceIndex">
    /// Zero-based index when multiple readers are attached (default 0).
    /// </param>
    public static FingerprintDevice Open(
        SgDeviceType deviceType = SgDeviceType.Auto,
        uint deviceIndex = 0)
    {
        var dev = new FingerprintDevice();
        try
        {
            dev.Initialize(deviceType, deviceIndex);
            return dev;
        }
        catch
        {
            dev.Dispose();
            throw;
        }
    }

    private void Initialize(SgDeviceType deviceType, uint deviceIndex)
    {
        // ── 1. Create the managed SDK instance (loads sgfplib.dll) ────────
        try
        {
            _sgfpm = new SGFingerPrintManager();
        }
        catch (FileNotFoundException)
        {
            ThrowNativeLibraryNotFound();
        }

        // ── 2. Initialise for the requested device type ───────────────────
        var err = (SGFPMError)_sgfpm!.Init(MapDeviceType(deviceType));
        if (err == SGFPMError.ERROR_DEVICE_NOT_FOUND)
            ThrowDeviceNotFound(deviceType);
        ThrowIfError(err, "Init");

        // ── 3. Open the physical USB device ──────────────────────────────
        err = (SGFPMError)_sgfpm.OpenDevice((int)deviceIndex);
        if (err == SGFPMError.ERROR_DEVICE_NOT_FOUND)
            ThrowDeviceNotFound(deviceType);
        ThrowIfError(err, "OpenDevice");

        _deviceOpen = true;
    }

    private static void ThrowNativeLibraryNotFound()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string libName = isWindows ? "sgfplib.dll" : "libsgfplib.so";
        string installInstructions = isWindows
            ? """
              Install the SecuGen Fingerprint SDK for Windows:
                1. Download from https://secugen.com/products/sdk/
                2. Run the installer – it copies sgfplib.dll to the System32 folder.
                3. Make sure the USB driver is installed (included in the SDK package).
              """
            : """
              Install the SecuGen Fingerprint SDK for Linux:
                1. Download the Linux SDK from https://secugen.com/products/sdk/
                2. Copy libsgfplib.so to /usr/lib or /usr/local/lib
                3. Run: sudo ldconfig
                4. Plug in your reader and verify with: lsusb | grep -i secugen
              """;

        throw new SecuGenException(
            $"""
            SecuGen SDK native library '{libName}' could not be loaded.

            {installInstructions}
            Supported devices: Hamster Pro 10/20/20 AP, Hamster IV/III/Plus/Duo, DEX.
            """);
    }

    private static void ThrowDeviceNotFound(SgDeviceType requested)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        string usbCheck = isWindows
            ? "Device Manager → Universal Serial Bus controllers"
            : "lsusb | grep -i '1162'   # 0x1162 is the SecuGen USB Vendor ID";

        string driverCheck = isWindows
            ? "Device Manager → check for yellow ⚠ icon on the SecuGen device"
            : "dmesg | tail -30   # look for 'usb … SecuGen' lines";

        throw new SecuGenException(
            $"""
            No SecuGen fingerprint reader was found (requested type: {requested}).

            Troubleshooting steps:
              1. Plug in your SecuGen reader and wait a few seconds.
              2. Verify the device appears in the USB list:
                   {usbCheck}
              3. Verify the USB driver is installed:
                   {driverCheck}
              4. Try unplugging and re-plugging the device.
              5. Confirm the correct device type by checking the label on the reader;
                 pass SgDeviceType.Auto to let the SDK detect the model automatically.
              6. On Linux ensure you are running with sufficient USB permissions:
                   sudo cp /path/to/secugen.rules /etc/udev/rules.d/
                   sudo udevadm control --reload-rules && sudo udevadm trigger
            """);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_sgfpm != null)
        {
            if (_deviceOpen)
            {
                _sgfpm.CloseDevice();
                _deviceOpen = false;
            }
            _sgfpm.Dispose();
            _sgfpm = null;
        }
    }

    // ── Device information ────────────────────────────────────────────────

    /// <summary>Returns model, firmware version and serial number.</summary>
    public SgDeviceInfo GetDeviceInfo()
    {
        EnsureOpen();
        var p = new SGFPMDeviceInfoParam();
        ThrowIfError(_sgfpm!.GetDeviceInfo(p), "GetDeviceInfo");

        uint devType = 0;
        if (_sgfpm.EnumerateDevice() == (int)SGFPMError.ERROR_NONE && _sgfpm.NumberOfDevice > 0)
        {
            var dl = new SGFPMDeviceList();
            if (_sgfpm.GetEnumDeviceInfo(0, dl) == (int)SGFPMError.ERROR_NONE)
                devType = MapDeviceNameToType(dl.DevName);
        }

        return new SgDeviceInfo
        {
            DevId        = p.DeviceID.ToString(),
            ComPort      = (uint)p.ComPort,
            DevType      = devType,
            FWVersion    = (uint)p.FWVersion,
            SerialNumber = Encoding.ASCII.GetString(p.DeviceSN).TrimEnd('\0'),
        };
    }

    /// <summary>Returns sensor image size, brightness, contrast, gain and DPI.</summary>
    public SgFingerInfo GetImageInfo()
    {
        EnsureOpen();
        var p = new SGFPMDeviceInfoParam();
        ThrowIfError(_sgfpm!.GetDeviceInfo(p), "GetDeviceInfo");

        return new SgFingerInfo
        {
            ImageWidth  = (uint)p.ImageWidth,
            ImageHeight = (uint)p.ImageHeight,
            Brightness  = (uint)p.Brightness,
            Contrast    = (uint)p.Contrast,
            Gain        = (uint)p.Gain,
            Resolution  = (uint)p.ImageDPI,
        };
    }

    // ── Sensor tuning ─────────────────────────────────────────────────────

    /// <summary>
    /// Adjusts the sensor LED brightness (0–255, default = 128).
    /// Higher values suit low-contrast fingers; lower values suit oily fingers.
    /// </summary>
    public void SetBrightness(uint brightness)
    {
        EnsureOpen();
        ThrowIfError(_sgfpm!.SetBrightness((int)brightness), "SetBrightness");
    }

    /// <summary>Adjusts the analogue gain (0–255, default = 128).
    /// <para><b>Note:</b> The managed SDK (<c>SGFingerPrintManager</c>) does not expose
    /// a gain-control method; calling this has no effect.</para></summary>
    public void SetGain(uint gain)
    {
        EnsureOpen();
        // SGFingerPrintManager does not expose a SetGain method.
    }

    // ── Capture ───────────────────────────────────────────────────────────

    /// <summary>
    /// Captures one fingerprint image.  Returns the raw greyscale pixel buffer
    /// (width × height bytes, 8 bpp, 0 = black, 255 = white).
    /// </summary>
    public byte[] CaptureImage()
    {
        EnsureOpen();
        var info = GetImageInfo();
        var buffer = new byte[info.ImageWidth * info.ImageHeight];
        ThrowIfError(_sgfpm!.GetImage(buffer), "GetImage");
        return buffer;
    }

    // ── Quality assessment ────────────────────────────────────────────────

    /// <summary>
    /// Scores the quality of a previously captured image buffer.
    /// Returns a <see cref="SgQuality"/> value and the corresponding
    /// human-readable description.
    /// </summary>
    public (SgQuality Quality, string Description) AssessQuality(byte[] imageBuffer)
    {
        EnsureOpen();
        var info = GetImageInfo();
        int q = 0;
        ThrowIfError(
            _sgfpm!.GetImageQuality((int)info.ImageWidth, (int)info.ImageHeight, imageBuffer, ref q),
            "GetImageQuality");

        var quality = (SgQuality)q;
        string desc = quality switch
        {
            SgQuality.Excellent => "Excellent – ideal for enrolment and verification",
            SgQuality.VeryGood  => "Very Good – suitable for enrolment and verification",
            SgQuality.Good      => "Good – suitable for verification",
            SgQuality.Fair      => "Fair – may affect match accuracy, re-capture recommended",
            SgQuality.Poor      => "Poor – re-capture required",
            _                   => $"Unknown quality code {q}",
        };
        return (quality, desc);
    }

    // ── Template operations ───────────────────────────────────────────────

    /// <summary>Returns the template size in bytes reported by the current device.</summary>
    public uint GetTemplateSize()
    {
        EnsureOpen();
        int size = 0;
        ThrowIfError(_sgfpm!.GetMaxTemplateSize(ref size), "GetMaxTemplateSize");
        return (uint)size;
    }

    /// <summary>
    /// Extracts a fingerprint template (minutiae set) from a captured image buffer.
    /// The returned byte array can be stored persistently and later used for matching.
    /// </summary>
    public byte[] CreateTemplate(byte[] imageBuffer)
    {
        EnsureOpen();
        uint size = GetTemplateSize();
        var template = new byte[size];
        ThrowIfError(_sgfpm!.CreateTemplate(imageBuffer, template), "CreateTemplate");
        return template;
    }

    // ── Matching ──────────────────────────────────────────────────────────

    /// <summary>
    /// Compares two templates using the supplied security level.
    /// Returns <c>true</c> when the two fingerprints are considered a match.
    /// </summary>
    /// <param name="threshold">
    /// A value from <see cref="SgThreshold"/>.
    /// <see cref="SgThreshold.Normal"/> (FAR ~1/1,000,000) is recommended.
    /// </param>
    public bool MatchTemplates(byte[] template1, byte[] template2,
        uint threshold = SgThreshold.Normal)
    {
        EnsureOpen();
        bool matched = false;
        ThrowIfError(
            _sgfpm!.MatchTemplate(template1, template2, (SGFPMSecurityLevel)threshold, ref matched),
            "MatchTemplate");
        return matched;
    }

    // ── Liveness detection ────────────────────────────────────────────────

    /// <summary>
    /// Reads the liveness / fake-finger indicator.
    /// Returns <c>null</c> when the device does not support this feature or
    /// when the SDK reports <c>ERROR_FUNCTION_FAILED</c> / <c>ERROR_UNSUPPORTED_DEV</c>.
    /// </summary>
    public uint? GetFakeDetectInfo()
    {
        EnsureOpen();
        int level = 0;
        var err = (SGFPMError)_sgfpm!.GetFakeDetectionLevel(ref level);
        if (err == SGFPMError.ERROR_FUNCTION_FAILED ||
            err == SGFPMError.ERROR_UNSUPPORTED_DEV)
            return null;
        ThrowIfError(err, "GetFakeDetectionLevel");
        return (uint)level;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_deviceOpen)
            throw new InvalidOperationException("Device is not open.");
    }

    private static void ThrowIfError(int errorCode, string functionName)
        => ThrowIfError((SGFPMError)errorCode, functionName);

    private static void ThrowIfError(SGFPMError error, string functionName)
    {
        if (error == SGFPMError.ERROR_NONE) return;

        string detail = error switch
        {
            SGFPMError.ERROR_CREATION_FAILED   => "SDK object creation failed (check SDK installation).",
            SGFPMError.ERROR_FUNCTION_FAILED   => "The SDK function failed (check device connection).",
            SGFPMError.ERROR_INVALID_PARAM     => "An invalid parameter was passed to the SDK.",
            SGFPMError.ERROR_DLLLOAD_FAILED    => "The SDK DLL (sgfplib.dll) could not be loaded.",
            SGFPMError.ERROR_DLLLOAD_FAILED_DRV  => "The SDK driver DLL could not be loaded.",
            SGFPMError.ERROR_DLLLOAD_FAILED_ALGO => "The SDK algorithm DLL could not be loaded.",
            SGFPMError.ERROR_DEVICE_NOT_FOUND  => "No SecuGen device was found on any USB port.",
            SGFPMError.ERROR_TIME_OUT          => "The capture operation timed out. Try again.",
            SGFPMError.ERROR_WRONG_IMAGE       => "The image buffer contains invalid data.",
            SGFPMError.ERROR_LACK_OF_BANDWIDTH => "USB bandwidth insufficient – disconnect other USB devices.",
            SGFPMError.ERROR_UNSUPPORTED_DEV   => "The device type specified is not supported.",
            SGFPMError.ERROR_EXTRACT_FAIL      => "Template extraction failed.",
            SGFPMError.ERROR_MATCH_FAIL        => "Template matching failed.",
            _                                  => $"SDK error code {(int)error}.",
        };

        throw new SecuGenException($"{functionName} failed [{error}]: {detail}");
    }

    /// <summary>Maps <see cref="SgDeviceType"/> to the managed SDK's <see cref="SGFPMDeviceName"/>.</summary>
    private static SGFPMDeviceName MapDeviceType(SgDeviceType type) => type switch
    {
        SgDeviceType.Auto  => SGFPMDeviceName.DEV_AUTO,
        SgDeviceType.FDP02 => SGFPMDeviceName.DEV_FDP02,
        SgDeviceType.FDU02 => SGFPMDeviceName.DEV_FDU02,
        SgDeviceType.FDU04 => SGFPMDeviceName.DEV_FDU04,
        SgDeviceType.FDU03 => SGFPMDeviceName.DEV_FDU03,
        SgDeviceType.FDU05 => SGFPMDeviceName.DEV_FDU05,
        SgDeviceType.FDU07 => SGFPMDeviceName.DEV_FDU07,
        SgDeviceType.FDU08 => SGFPMDeviceName.DEV_FDU08,
        SgDeviceType.FDU09 => SGFPMDeviceName.DEV_FDU09,
        _                  => SGFPMDeviceName.DEV_AUTO,
    };

    /// <summary>Maps <see cref="SGFPMDeviceName"/> back to the legacy <c>SgDeviceInfo.DevType</c> value.</summary>
    private static uint MapDeviceNameToType(SGFPMDeviceName devName) => devName switch
    {
        SGFPMDeviceName.DEV_FDP02   => 0,   // Hamster II
        SGFPMDeviceName.DEV_FDU02   => 2,   // Hamster IV
        SGFPMDeviceName.DEV_FDU04   => 3,   // Hamster III
        SGFPMDeviceName.DEV_FDU03   => 4,   // Hamster Plus
        SGFPMDeviceName.DEV_FDU05   => 5,   // DEX
        SGFPMDeviceName.DEV_FDU07   => 7,   // Hamster Pro 10
        SGFPMDeviceName.DEV_FDU07A  => 7,   // Hamster Pro 10A
        SGFPMDeviceName.DEV_FDU08   => 8,   // Hamster Pro 20
        SGFPMDeviceName.DEV_FDU09   => 9,   // Hamster Pro 20 AP
        SGFPMDeviceName.DEV_FDU10A  => 10,  // SDU03P
        _                           => (uint)devName,
    };
}

/// <summary>Exception thrown when a SecuGen SDK call fails.</summary>
public sealed class SecuGenException(string message) : Exception(message);
