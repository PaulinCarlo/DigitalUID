using System.Runtime.InteropServices;

namespace DigitalUID;

/// <summary>
/// High-level managed wrapper around the SecuGen SGFPLIB native library.
/// Implements <see cref="IDisposable"/> – use inside a <c>using</c> block or
/// call <see cref="Dispose"/> to release the device and native handle.
/// </summary>
internal sealed class FingerprintDevice : IDisposable
{
    private IntPtr _handle = IntPtr.Zero;
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
        // ── 1. Check that the native library can be loaded ────────────────
        CheckNativeLibraryAvailable();

        // ── 2. Create the SDK handle ──────────────────────────────────────
        var err = (SgError)NativeMethods.Create(out _handle);
        ThrowIfError(err, "SGFPM_Create");

        // ── 3. Initialise for the requested device type ───────────────────
        err = (SgError)NativeMethods.Init(_handle, (uint)deviceType);
        if (err == SgError.DeviceNotFound)
            ThrowDeviceNotFound(deviceType);
        ThrowIfError(err, "SGFPM_Init");

        // ── 4. Open the physical USB device ──────────────────────────────
        err = (SgError)NativeMethods.OpenDevice(_handle, deviceIndex);
        if (err == SgError.DeviceNotFound)
            ThrowDeviceNotFound(deviceType);
        ThrowIfError(err, "SGFPM_OpenDevice");

        _deviceOpen = true;
    }

    /// <summary>
    /// Probes for the shared library <em>before</em> any P/Invoke call so
    /// we can emit an actionable error message instead of a DllNotFoundException
    /// with no context.
    /// </summary>
    private static void CheckNativeLibraryAvailable()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string libName = isWindows ? "sgfplib.dll" : "libsgfplib.so";

        // Try to load via NativeLibrary – gives us a clean true/false result.
        if (!NativeLibrary.TryLoad(libName, out IntPtr lib))
        {
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

        NativeLibrary.Free(lib);
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

        if (_handle != IntPtr.Zero)
        {
            if (_deviceOpen)
            {
                NativeMethods.CloseDevice(_handle);
                _deviceOpen = false;
            }
            NativeMethods.Terminate(_handle);
            _handle = IntPtr.Zero;
        }
    }

    // ── Device information ────────────────────────────────────────────────

    /// <summary>Returns model, firmware version and serial number.</summary>
    public SgDeviceInfo GetDeviceInfo()
    {
        EnsureOpen();
        var info = new SgDeviceInfo();
        ThrowIfError((SgError)NativeMethods.GetDeviceInfo(_handle, ref info), "SGFPM_GetDeviceInfo");
        return info;
    }

    /// <summary>Returns sensor image size, brightness, contrast, gain and DPI.</summary>
    public SgFingerInfo GetImageInfo()
    {
        EnsureOpen();
        var info = new SgFingerInfo();
        ThrowIfError((SgError)NativeMethods.GetImageInfo(_handle, ref info), "SGFPM_GetImageInfo");
        return info;
    }

    // ── Sensor tuning ─────────────────────────────────────────────────────

    /// <summary>
    /// Adjusts the sensor LED brightness (0–255, default = 128).
    /// Higher values suit low-contrast fingers; lower values suit oily fingers.
    /// </summary>
    public void SetBrightness(uint brightness)
    {
        EnsureOpen();
        ThrowIfError((SgError)NativeMethods.SetBrightness(_handle, brightness), "SGFPM_SetBrightness");
    }

    /// <summary>Adjusts the analogue gain (0–255, default = 128).</summary>
    public void SetGain(uint gain)
    {
        EnsureOpen();
        ThrowIfError((SgError)NativeMethods.SetGain(_handle, gain), "SGFPM_SetGain");
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
        ThrowIfError((SgError)NativeMethods.GetImage(_handle, buffer), "SGFPM_GetImage");
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
        ThrowIfError(
            (SgError)NativeMethods.GetImageQuality(
                _handle, info.ImageWidth, info.ImageHeight, imageBuffer, out uint q),
            "SGFPM_GetImageQuality");

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
        ThrowIfError((SgError)NativeMethods.GetTemplateSize(_handle, out uint size), "SGFPM_GetTemplateSize");
        return size;
    }

    /// <summary>
    /// Extracts a fingerprint template (minutiae set) from a captured image buffer.
    /// The returned byte array can be stored persistently and later used for matching.
    /// </summary>
    public byte[] CreateTemplate(byte[] imageBuffer)
    {
        EnsureOpen();
        var info = GetImageInfo();
        uint size = GetTemplateSize();
        var template = new byte[size];
        ThrowIfError(
            (SgError)NativeMethods.CreateTemplate(_handle, ref info, imageBuffer, template),
            "SGFPM_CreateTemplate");
        return template;
    }

    // ── Matching ──────────────────────────────────────────────────────────

    /// <summary>
    /// Compares two templates using the supplied security threshold.
    /// Returns <c>true</c> when the two fingerprints are considered a match.
    /// </summary>
    /// <param name="threshold">
    /// A value from <see cref="SgThreshold"/>.
    /// <see cref="SgThreshold.Normal"/> (FAR 1/1,000,000) is recommended.
    /// </param>
    public bool MatchTemplates(byte[] template1, byte[] template2,
        uint threshold = SgThreshold.Normal)
    {
        EnsureOpen();
        ThrowIfError(
            (SgError)NativeMethods.MatchTemplate(
                _handle, template1, template2, threshold, out bool matched),
            "SGFPM_MatchTemplate");
        return matched;
    }

    // ── Liveness detection ────────────────────────────────────────────────

    /// <summary>
    /// Reads the liveness / fake-finger indicator.
    /// Returns <c>null</c> when the device does not support this feature.
    /// </summary>
    public uint? GetFakeDetectInfo()
    {
        EnsureOpen();
        var err = (SgError)NativeMethods.GetFakeDetectInfo(_handle, out uint value);
        if (err == SgError.FunctionFailed) return null; // not supported on this model
        ThrowIfError(err, "SGFPM_GetFakeDetectInfo");
        return value;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void EnsureOpen()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_deviceOpen)
            throw new InvalidOperationException("Device is not open.");
    }

    private static void ThrowIfError(SgError error, string functionName)
    {
        if (error == SgError.None) return;

        string detail = error switch
        {
            SgError.CreationFailed     => "SDK object creation failed (check SDK installation).",
            SgError.FunctionFailed     => "The SDK function failed (check device connection).",
            SgError.InvalidParam       => "An invalid parameter was passed to the SDK.",
            SgError.NotInitialized     => "Device has not been initialised. Call Init first.",
            SgError.AlreadyInitialized => "Device is already initialised.",
            SgError.DeviceNotFound     => "No SecuGen device was found on any USB port.",
            SgError.DeviceBusy         => "Device is currently busy – retry after a short delay.",
            SgError.Timeout            => "The capture operation timed out. Try again.",
            SgError.InvalidDevice      => "The device type specified is not valid.",
            SgError.ChangeSettings     => "Could not apply the requested device settings.",
            SgError.WrongImage         => "The image buffer contains invalid data.",
            SgError.LackOfBandwidth    => "USB bandwidth insufficient – disconnect other USB devices.",
            SgError.MemoryFailed       => "SDK memory allocation failed.",
            SgError.SysFileFailed      => "A required SDK system file is missing.",
            SgError.TamperAlert        => "Tamper alert triggered on the device.",
            SgError.IniFileFailed      => "The SDK INI configuration file is missing or corrupt.",
            SgError.TemplateZero       => "Template extraction returned a zero-length template.",
            _                          => $"Unknown SDK error code {(uint)error}.",
        };

        throw new SecuGenException($"{functionName} failed [{error}]: {detail}");
    }
}

/// <summary>Exception thrown when a SecuGen SDK call fails.</summary>
public sealed class SecuGenException(string message) : Exception(message);
