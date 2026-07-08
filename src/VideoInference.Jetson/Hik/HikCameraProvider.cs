using System;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Threading;
using OpenCvSharp;

namespace VideoInferenceDemo;

internal sealed class HikCameraProvider : ICameraProvider
{
    private const int GrabTimeoutMs = 200;
    private const int MaxTransientGrabFailures = 20;

    public string ProviderId => CameraProviderIds.HikRobot;

    public string DisplayName => "HikRobot SDK";

    public IReadOnlyList<CameraDeviceInfo> EnumerateDevices()
    {
        var probe = HikLinuxProbe.Run();
        if (!string.IsNullOrWhiteSpace(probe.ErrorMessage))
        {
            throw new InvalidOperationException($"Enumerate Hik cameras failed: {probe.ErrorMessage}");
        }

        var result = probe.Devices
            .Select(ToCameraDeviceInfo)
            .ToList();

        var labels = result.Count == 0
            ? "none"
            : string.Join(", ", result.Select(item => item.DisplayName));
        CameraDiagnostics.Info("hik-provider-linux", $"Enumerated {result.Count} Hik camera(s): {labels}");
        return result;
    }

    public ICameraSession Open(CameraOpenOptions options)
    {
        var normalized = options.Normalize();
        CameraDiagnostics.Info(
            "hik-provider-linux",
            $"Resolving Hik camera. Selector={CameraOptionHelpers.GetSelector(normalized)}, TargetFps={normalized.TargetFps:F2}");

        var device = ResolveDevice(normalized);
        var createResult = HikLinuxNative.CreateHandle(out var handle, device.DeviceInfoPointer);
        EnsureSuccess(createResult, "Create Hik camera handle");

        try
        {
            var openResult = HikLinuxNative.OpenDevice(handle);
            EnsureSuccess(openResult, "Open Hik camera");
            ConfigureDevice(handle, normalized.TargetFps, normalized.TriggerMode);
            _ = HikLinuxNative.SetImageNodeNum(handle, 3);
            _ = HikLinuxNative.SetGrabStrategy(handle, HikLinuxNative.GrabStrategyLatestImages);
            _ = HikLinuxNative.SetOutputQueueSize(handle, 1);
            ConfigureTransport(device, handle);
            TryRunCommand(handle, "AcquisitionStart");
            EnsureSuccess(HikLinuxNative.StartGrabbing(handle), "Start Hik camera grabbing");

            var width = ReadIntValue(handle, "Width");
            var height = ReadIntValue(handle, "Height");
            var payloadSize = ReadIntValue(handle, "PayloadSize");
            var pixelFormat = ReadEnumValue(handle, "PixelFormat");
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("Failed to read Hik camera Width/Height.");
            }
            if (payloadSize <= 0)
            {
                payloadSize = checked(width * height * 3);
            }

            CameraDiagnostics.Info("hik-provider-linux", $"Opened Hik camera handle for {device.DisplayName}. Resolution={width}x{height}");
            LogCurrentParameters(handle);
            return new HikCameraSession(device, handle, width, height, payloadSize, pixelFormat, normalized.TargetFps, normalized.TriggerMode);
        }
        catch
        {
            if (handle != IntPtr.Zero)
            {
                _ = HikLinuxNative.DestroyHandle(handle);
            }

            throw;
        }
    }

    private static void ConfigureDevice(IntPtr handle, double targetFps, CameraTriggerMode triggerMode)
    {
        TrySetEnum(handle, "AcquisitionMode", "Continuous");
        if (triggerMode == CameraTriggerMode.Software)
        {
            TrySetEnum(handle, "TriggerSelector", "FrameStart");
            SetEnumRequired(handle, "TriggerMode", "On");
            SetEnumRequired(handle, "TriggerSource", "Software");
        }
        else if (triggerMode == CameraTriggerMode.HardwareLine0)
        {
            TrySetEnum(handle, "TriggerSelector", "FrameStart");
            SetEnumRequired(handle, "TriggerMode", "On");
            ConfigureHardwareTriggerSource(handle);
            TrySetEnum(handle, "TriggerActivation", "RisingEdge");
        }
        else
        {
            TrySetEnum(handle, "TriggerMode", "Off");
        }

        TrySetBool(handle, "TriggerCacheEnable", false);

        if (targetFps > 0)
        {
            TrySetBool(handle, "AcquisitionFrameRateEnable", true);
            TrySetFloat(handle, "AcquisitionFrameRate", (float)targetFps);
        }
    }

    private static void ConfigureTransport(HikNativeDeviceInfo device, IntPtr handle)
    {
        if (device.TransportLayerType != HikLinuxNative.MvGigEDevice)
        {
            return;
        }

        var optimalPacketSize = HikLinuxNative.GetOptimalPacketSize(handle);
        if (optimalPacketSize > 0)
        {
            var setResult = HikLinuxNative.SetGevScpsPacketSize(handle, (uint)optimalPacketSize);
            CameraDiagnostics.Info(
                "hik-provider-linux",
                $"Configured GigE packet size. Device={device.DisplayName}, Optimal={optimalPacketSize}, Result={FormatResult(setResult)}");
        }
        else
        {
            CameraDiagnostics.Info(
                "hik-provider-linux",
                $"GetOptimalPacketSize returned {optimalPacketSize} for {device.DisplayName}.");
        }
    }

    private static int ReadIntValue(IntPtr handle, string key)
    {
        var value = new HikLinuxNative.MvccIntValueEx
        {
            Reserved = new uint[16]
        };

        var result = HikLinuxNative.GetIntValueEx(handle, key, ref value);
        return result == HikLinuxNative.MvOk ? checked((int)value.CurrentValue) : 0;
    }

    private static uint ReadEnumValue(IntPtr handle, string key)
    {
        var value = new HikLinuxNative.MvccEnumValue
        {
            SupportedValues = new uint[64],
            Reserved = new uint[4]
        };

        var result = HikLinuxNative.GetEnumValue(handle, key, ref value);
        return result == HikLinuxNative.MvOk ? value.CurrentValue : 0;
    }

    private static void LogCurrentParameters(IntPtr handle)
    {
        LogEnumValue(handle, "TriggerMode");
        LogEnumValue(handle, "AcquisitionMode");
        LogBoolValue(handle, "AcquisitionFrameRateEnable");
        LogFloatValue(handle, "AcquisitionFrameRate");
        LogIntValue(handle, "PayloadSize");
        LogIntValue(handle, "GevSCPSPacketSize");
        LogEnumValue(handle, "PixelFormat");
    }

    private static void LogEnumValue(IntPtr handle, string key)
    {
        var value = new HikLinuxNative.MvccEnumValue
        {
            SupportedValues = new uint[64],
            Reserved = new uint[4]
        };

        var result = HikLinuxNative.GetEnumValue(handle, key, ref value);
        CameraDiagnostics.Info("hik-provider-linux", $"Node {key} => {FormatResult(result)} value={value.CurrentValue}");
    }

    private static string ConfigureHardwareTriggerSource(IntPtr handle)
    {
        var triggerSourceEntries = ReadSupportedEnumSymbols(handle, "TriggerSource");
        var lineModes = ProbeLineModes(handle);
        var candidates = HikHardwareTriggerSourceResolver.BuildCandidateOrder(lineModes, triggerSourceEntries);

        CameraDiagnostics.Info(
            "hik-provider-linux",
            $"Resolving hardware trigger source. TriggerSourceEntries={HikHardwareTriggerSourceResolver.FormatSymbols(triggerSourceEntries)}, LineModes={HikHardwareTriggerSourceResolver.FormatLineModes(lineModes)}, Candidates={HikHardwareTriggerSourceResolver.FormatSymbols(candidates)}");

        foreach (var candidate in candidates)
        {
            if (!TrySetEnumValue(handle, "TriggerSource", candidate))
            {
                continue;
            }

            TrySetEnumValue(handle, "LineSelector", candidate, logFailures: false);
            CameraDiagnostics.Info("hik-provider-linux", $"Selected Hik hardware trigger source {candidate}.");
            return candidate;
        }

        throw new InvalidOperationException(
            $"Unable to select Hik hardware trigger source. TriggerSourceEntries={HikHardwareTriggerSourceResolver.FormatSymbols(triggerSourceEntries)}, LineModes={HikHardwareTriggerSourceResolver.FormatLineModes(lineModes)}, Candidates={HikHardwareTriggerSourceResolver.FormatSymbols(candidates)}.");
    }

    private static IReadOnlyList<HikLineModeProbe> ProbeLineModes(IntPtr handle)
    {
        var lineSelectors = ReadSupportedEnumSymbols(handle, "LineSelector")
            .Where(item => item.StartsWith("Line", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (lineSelectors.Count == 0)
        {
            lineSelectors.AddRange(HikHardwareTriggerSourceResolver.DefaultLineCandidates);
        }

        var probes = new List<HikLineModeProbe>(lineSelectors.Count);
        foreach (var line in lineSelectors)
        {
            var selected = TrySetEnumValue(handle, "LineSelector", line, logFailures: false);
            var mode = selected ? TryReadCurrentEnumSymbol(handle, "LineMode") : null;
            probes.Add(new HikLineModeProbe(line, mode));
        }

        return probes;
    }

    private static IReadOnlyList<string> ReadSupportedEnumSymbols(IntPtr handle, string key)
    {
        var value = new HikLinuxNative.MvccEnumValue
        {
            SupportedValues = new uint[64],
            Reserved = new uint[4]
        };

        var result = HikLinuxNative.GetEnumValue(handle, key, ref value);
        if (result != HikLinuxNative.MvOk)
        {
            CameraDiagnostics.Info("hik-provider-linux", $"Read enum symbols {key} => {FormatResult(result)}");
            return [];
        }

        var symbols = new List<string>();
        var count = Math.Min(checked((int)value.SupportedCount), value.SupportedValues.Length);
        for (var i = 0; i < count; i++)
        {
            var symbolic = TryReadEnumSymbol(handle, key, value.SupportedValues[i]);
            if (!string.IsNullOrWhiteSpace(symbolic))
            {
                symbols.Add(symbolic);
            }
        }

        return symbols
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? TryReadCurrentEnumSymbol(IntPtr handle, string key)
    {
        var value = new HikLinuxNative.MvccEnumValue
        {
            SupportedValues = new uint[64],
            Reserved = new uint[4]
        };

        var result = HikLinuxNative.GetEnumValue(handle, key, ref value);
        return result == HikLinuxNative.MvOk
            ? TryReadEnumSymbol(handle, key, value.CurrentValue)
            : null;
    }

    private static string? TryReadEnumSymbol(IntPtr handle, string key, uint value)
    {
        try
        {
            var entry = new HikLinuxNative.MvccEnumEntry
            {
                Value = value,
                Symbolic = new byte[64],
                Reserved = new uint[4]
            };
            var result = HikLinuxNative.GetEnumEntrySymbolic(handle, key, ref entry);
            return result == HikLinuxNative.MvOk ? HikLinuxNative.ReadUtf8(entry.Symbolic) : null;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            CameraDiagnostics.Info("hik-provider-linux", $"Read enum symbolic {key} unavailable: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Info("hik-provider-linux", $"Read enum symbolic {key} value={value} threw: {ex.Message}");
            return null;
        }
    }

    private static void LogBoolValue(IntPtr handle, string key)
    {
        var result = HikLinuxNative.GetBoolValue(handle, key, out var value);
        CameraDiagnostics.Info("hik-provider-linux", $"Node {key} => {FormatResult(result)} value={value}");
    }

    private static void LogFloatValue(IntPtr handle, string key)
    {
        var value = new HikLinuxNative.MvccFloatValue
        {
            Reserved = new uint[4]
        };

        var result = HikLinuxNative.GetFloatValue(handle, key, ref value);
        CameraDiagnostics.Info("hik-provider-linux", $"Node {key} => {FormatResult(result)} value={value.CurrentValue:F3}");
    }

    private static void LogIntValue(IntPtr handle, string key)
    {
        var value = new HikLinuxNative.MvccIntValueEx
        {
            Reserved = new uint[16]
        };

        var result = HikLinuxNative.GetIntValueEx(handle, key, ref value);
        CameraDiagnostics.Info("hik-provider-linux", $"Node {key} => {FormatResult(result)} value={value.CurrentValue}");
    }

    private static void TrySetEnum(IntPtr handle, string key, string value)
    {
        _ = TrySetEnumValue(handle, key, value);
    }

    private static void SetEnumRequired(IntPtr handle, string key, string value)
    {
        if (!TrySetEnumValue(handle, key, value))
        {
            throw new InvalidOperationException($"Unable to set Hik enum node. Key={key}, Value={value}.");
        }
    }

    private static bool TrySetEnumValue(
        IntPtr handle,
        string key,
        string value,
        bool logFailures = true)
    {
        try
        {
            var result = HikLinuxNative.SetEnumValueByString(handle, key, value);
            if (result == HikLinuxNative.MvOk || logFailures)
            {
                CameraDiagnostics.Info("hik-provider-linux", $"Set enum {key}={value} => {FormatResult(result)}");
            }

            return result == HikLinuxNative.MvOk;
        }
        catch (Exception ex)
        {
            if (logFailures)
            {
                CameraDiagnostics.Info("hik-provider-linux", $"Set enum {key}={value} threw: {ex.Message}");
            }

            return false;
        }
    }

    private static void TrySetBool(IntPtr handle, string key, bool value)
    {
        try
        {
            var result = HikLinuxNative.SetBoolValue(handle, key, value);
            CameraDiagnostics.Info("hik-provider-linux", $"Set bool {key}={value} => {FormatResult(result)}");
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Info("hik-provider-linux", $"Set bool {key}={value} threw: {ex.Message}");
        }
    }

    private static void TrySetFloat(IntPtr handle, string key, float value)
    {
        try
        {
            var result = HikLinuxNative.SetFloatValue(handle, key, value);
            CameraDiagnostics.Info("hik-provider-linux", $"Set float {key}={value:F3} => {FormatResult(result)}");
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Info("hik-provider-linux", $"Set float {key}={value:F3} threw: {ex.Message}");
        }
    }

    private static void TryRunCommand(IntPtr handle, string key)
    {
        try
        {
            var result = HikLinuxNative.SetCommandValue(handle, key);
            CameraDiagnostics.Info("hik-provider-linux", $"Run command {key} => {FormatResult(result)}");
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Info("hik-provider-linux", $"Run command {key} threw: {ex.Message}");
        }
    }

    private static string FormatResult(int result)
    {
        return result == HikLinuxNative.MvOk
            ? "MV_OK"
            : $"0x{unchecked((uint)result):X8}";
    }

    private static HikNativeDeviceInfo ResolveDevice(CameraOpenOptions options)
    {
        var probe = HikLinuxProbe.Run();
        if (!string.IsNullOrWhiteSpace(probe.ErrorMessage))
        {
            throw new InvalidOperationException(probe.ErrorMessage);
        }

        var devices = probe.Devices;
        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No Hik cameras were found.");
        }

        if (!string.IsNullOrWhiteSpace(options.DeviceId))
        {
            var device = devices.FirstOrDefault(item => MatchesDeviceIdentifier(item, options.DeviceId!));
            if (device != null)
            {
                CameraDiagnostics.Info(
                    "hik-provider-linux",
                    $"Resolved requested Hik device '{options.DeviceId}' to {device.DisplayName}. Accessible={device.IsAccessible}");
                return device;
            }

            var sameSerial = devices
                .Where(item => string.Equals(item.SerialNumber, options.DeviceId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameSerial.Count > 0)
            {
                CameraDiagnostics.Info(
                    "hik-provider-linux",
                    $"Requested Hik device '{options.DeviceId}' had no exact stable-id match, but serial matched {sameSerial.Count} candidate(s). Falling back to first candidate.");
                return sameSerial[0];
            }

            var available = string.Join(", ", devices.Select(GetStableDeviceId));
            CameraDiagnostics.Info(
                "hik-provider-linux",
                $"Requested Hik device '{options.DeviceId}' was not found. Available={available}");
            throw new InvalidOperationException($"Hik camera '{options.DeviceId}' was not found.");
        }

        if (options.CameraIndex >= 0 && options.CameraIndex < devices.Count)
        {
            CameraDiagnostics.Info(
                "hik-provider-linux",
                $"Resolved Hik camera index {options.CameraIndex} to {devices[options.CameraIndex].DisplayName}. Accessible={devices[options.CameraIndex].IsAccessible}");
            return devices[options.CameraIndex];
        }

        throw new InvalidOperationException($"Hik camera index {options.CameraIndex} is out of range. Found {devices.Count} device(s).");
    }

    private static CameraDeviceInfo ToCameraDeviceInfo(HikNativeDeviceInfo device)
    {
        return new CameraDeviceInfo(
            CameraProviderIds.HikRobot,
            GetStableDeviceId(device),
            device.DisplayName,
            device.SerialNumber,
            device.UserDefinedName,
            device.ModelName);
    }

    private static bool MatchesDeviceIdentifier(HikNativeDeviceInfo device, string expected)
    {
        return string.Equals(GetStableDeviceId(device), expected, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(device.SerialNumber, expected, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(device.UserDefinedName, expected, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(device.DisplayName, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetStableDeviceId(HikNativeDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.SerialNumber))
        {
            return device.SerialNumber.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.UserDefinedName))
        {
            return device.UserDefinedName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(device.ModelName))
        {
            return device.ModelName.Trim();
        }

        return $"{device.ManufacturerName?.Trim()}-{device.TransportLayerType:X8}";
    }

    private static void EnsureSuccess(int result, string action)
    {
        if (result == HikLinuxNative.MvOk)
        {
            return;
        }

        throw new InvalidOperationException($"{action} failed with 0x{unchecked((uint)result):X8}.");
    }

    private sealed class HikCameraSession : ICameraSession
    {
        private readonly IntPtr _handle;
        private readonly TimestampDeltaPtsNormalizer _hostPtsNormalizer = new();
        private readonly DeviceTimestampPtsNormalizer _devicePtsNormalizer;
        private readonly MonotonicPtsClock _fallbackPtsClock = new();
        private readonly int _width;
        private readonly int _height;
        private readonly int _payloadSize;
        private readonly uint _pixelFormat;
        private readonly IntPtr _rawFrameBuffer;
        private readonly uint _rawFrameBufferSize;
        private readonly IntPtr _rawFrameInfoBuffer;
        private readonly byte[] _frameInfoBytes = new byte[256];
        private int _grabFailureLogCount;
        private int _frameCount;
        private bool _disposed;

        public HikCameraSession(
            HikNativeDeviceInfo device,
            IntPtr handle,
            int width,
            int height,
            int payloadSize,
            uint pixelFormat,
            double fallbackFps,
            CameraTriggerMode triggerMode)
        {
            _handle = handle;
            _width = width;
            _height = height;
            _payloadSize = payloadSize;
            _pixelFormat = pixelFormat;
            _rawFrameBufferSize = checked((uint)Math.Max(payloadSize, width * height));
            _rawFrameBuffer = Marshal.AllocHGlobal(checked((int)_rawFrameBufferSize));
            _rawFrameInfoBuffer = Marshal.AllocHGlobal(256);
            SourceId = $"{CameraProviderIds.HikRobot}:{GetStableDeviceId(device)}";
            DisplayName = device.DisplayName;
            ReportedFps = fallbackFps > 0 ? fallbackFps : 30;
            TriggerMode = triggerMode;
            _devicePtsNormalizer = new DeviceTimestampPtsNormalizer(ReportedFps);
        }

        public string SourceId { get; }

        public string DisplayName { get; }

        public double ReportedFps { get; }

        public CameraTriggerMode TriggerMode { get; }

        public bool TryRead(Mat destination, CancellationToken cancellationToken, out CameraFrameMetadata metadata)
        {
            metadata = default!;
            var transientFailures = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                destination.Create(_height, _width, MatType.CV_8UC3);
                var result = HikLinuxNative.GetOneFrameTimeout(
                    _handle,
                    _rawFrameBuffer,
                    _rawFrameBufferSize,
                    _rawFrameInfoBuffer,
                    GrabTimeoutMs);

                if (result != HikLinuxNative.MvOk)
                {
                    transientFailures++;
                    LogGrabFailure(result, transientFailures);
                    if (transientFailures >= MaxTransientGrabFailures)
                    {
                        throw new InvalidOperationException(
                            $"Hik frame grab failed with 0x{unchecked((uint)result):X8}.");
                    }

                    continue;
                }

                var frameInfo = ReadFrameInfo();
                transientFailures = 0;
                _frameCount++;
                if (_frameCount == 1)
                {
                    CameraDiagnostics.Info(
                        "hik-provider-linux",
                        $"FrameInfoRaw[0..128]={Convert.ToHexString(_frameInfoBytes.AsSpan(0, 128))}");
                }
                else if (_frameCount == 2)
                {
                    CameraDiagnostics.Info(
                        "hik-provider-linux",
                        $"FrameInfoRaw2[0..128]={Convert.ToHexString(_frameInfoBytes.AsSpan(0, 128))}");
                }

                ConvertToBgr(frameInfo, destination);
                if (_frameCount == 1)
                {
                    CameraDiagnostics.Info(
                        "hik-provider-linux",
                        $"Received first Hik frame. Size={_width}x{_height}, PixelFormat=0x{_pixelFormat:X8}, PayloadSize={_payloadSize}, FrameNum={frameInfo.FrameNumber}, LostPacket={frameInfo.LostPacket}");
                }
                else if (_frameCount == 2)
                {
                    CameraDiagnostics.Info(
                        "hik-provider-linux",
                        $"SecondFrameMeta FrameNum={frameInfo.FrameNumber}, HostTs={frameInfo.HostTimestamp}, DevTsHi={frameInfo.DeviceTimestampHigh}, DevTsLo={frameInfo.DeviceTimestampLow}, FrameLen={frameInfo.FrameLength}, FrameLenEx={frameInfo.FrameLengthEx}");
                }

                metadata = BuildFrameMetadata(frameInfo);
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CameraDiagnostics.Info("hik-provider-linux", $"Closing Hik camera {DisplayName}. SourceId={SourceId}");
            _ = HikLinuxNative.StopGrabbing(_handle);
            TryRunCommand(_handle, "AcquisitionStop");
            _ = HikLinuxNative.CloseDevice(_handle);
            _ = HikLinuxNative.DestroyHandle(_handle);
            if (_rawFrameBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_rawFrameBuffer);
            }
            if (_rawFrameInfoBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_rawFrameInfoBuffer);
            }
        }

        public bool TryTrigger(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            foreach (var key in new[] { "TriggerSoftware", "TriggerSoftwareExecute" })
            {
                var result = HikLinuxNative.SetCommandValue(_handle, key);
                if (result == HikLinuxNative.MvOk)
                {
                    return true;
                }

                CameraDiagnostics.Info(
                    "hik-provider-linux",
                    $"Execute command {key} failed with {FormatResult(result)}.");
            }

            throw new InvalidOperationException("Unable to execute Hik software trigger command.");
        }

        private HikLinuxNative.MvFrameOutInfoEx ReadFrameInfo()
        {
            Marshal.Copy(_rawFrameInfoBuffer, _frameInfoBytes, 0, _frameInfoBytes.Length);

            // Linux/aarch64 GetOneFrameTimeout does not line up with the Windows MV_FRAME_OUT_INFO_EX
            // marshaling layout. We parse the header manually using the observed native byte offsets.
            return new HikLinuxNative.MvFrameOutInfoEx
            {
                Width = ReadUInt16(0),
                Height = ReadUInt16(2),
                PixelType = ReadUInt32(8),
                FrameNumber = ReadUInt32(16),
                DeviceTimestampHigh = ReadUInt32(20),
                DeviceTimestampLow = ReadUInt32(24),
                Reserved0 = ReadUInt32(4),
                HostTimestamp = ReadInt64(32),
                FrameLength = ReadUInt32(40),
                SecondCount = 0,
                CycleCount = 0,
                CycleOffset = 0,
                Gain = ReadSingle(48),
                ExposureTime = ReadSingle(52),
                AverageBrightness = ReadUInt32(56),
                Red = ReadUInt32(60),
                Green = ReadUInt32(64),
                Blue = ReadUInt32(68),
                FrameCounter = ReadUInt32(72),
                TriggerIndex = ReadUInt32(76),
                Input = ReadUInt32(80),
                Output = ReadUInt32(84),
                OffsetX = ReadUInt16(88),
                OffsetY = ReadUInt16(90),
                ChunkWidth = ReadUInt16(92),
                ChunkHeight = ReadUInt16(94),
                LostPacket = ReadUInt32(44),
                UnparsedChunkCount = 0,
                UnparsedChunkContent = IntPtr.Zero,
                ExtendedWidth = ReadUInt32(120),
                ExtendedHeight = ReadUInt32(124),
                FrameLengthEx = ReadUInt32(40),
                Reserved = new uint[32]
            };
        }

        private ushort ReadUInt16(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_frameInfoBytes.AsSpan(offset, 2));

        private uint ReadUInt32(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_frameInfoBytes.AsSpan(offset, 4));

        private ulong ReadUInt64(int offset) => BinaryPrimitives.ReadUInt64LittleEndian(_frameInfoBytes.AsSpan(offset, 8));

        private long ReadInt64(int offset) => BinaryPrimitives.ReadInt64LittleEndian(_frameInfoBytes.AsSpan(offset, 8));

        private float ReadSingle(int offset)
        {
            var bits = BinaryPrimitives.ReadInt32LittleEndian(_frameInfoBytes.AsSpan(offset, 4));
            return BitConverter.Int32BitsToSingle(bits);
        }

        private IntPtr ReadIntPtr(int offset)
        {
            return IntPtr.Size == 8
                ? new IntPtr(unchecked((long)ReadUInt64(offset)))
                : new IntPtr(unchecked((int)ReadUInt32(offset)));
        }

        private void ConvertToBgr(HikLinuxNative.MvFrameOutInfoEx frameInfo, Mat destination)
        {
            if (_pixelFormat == HikLinuxNative.PixelTypeGvspMono8 && _payloadSize == _width * _height)
            {
                using var mono = Mat.FromPixelData(_height, _width, MatType.CV_8UC1, _rawFrameBuffer, _width);
                Cv2.CvtColor(mono, destination, ColorConversionCodes.GRAY2BGR);
                return;
            }

            if (_payloadSize == _width * _height && TryConvertBayer8ToBgr(destination))
            {
                return;
            }

            var sourceLength = (ulong)Math.Max(_payloadSize, _width * _height);
            var convert = new HikLinuxNative.MvCcPixelConvertParamEx
            {
                Width = (uint)_width,
                Height = (uint)_height,
                SourcePixelType = _pixelFormat,
                SourceData = _rawFrameBuffer,
                SourceDataLength = checked((uint)Math.Min(sourceLength, uint.MaxValue)),
                DestinationPixelType = HikLinuxNative.PixelTypeGvspBgr8Packed,
                DestinationBuffer = destination.Data,
                DestinationLength = 0,
                DestinationBufferSize = checked((uint)(_width * _height * 3)),
                Reserved = new uint[4]
            };

            var result = HikLinuxNative.ConvertPixelTypeEx(_handle, ref convert);
            if (result != HikLinuxNative.MvOk)
            {
                throw new InvalidOperationException(
                    $"Convert Hik frame to BGR failed with 0x{unchecked((uint)result):X8}. PixelType=0x{_pixelFormat:X8}, FrameNum={frameInfo.FrameNumber}, SourceLen={sourceLength}.");
            }
        }

        private bool TryConvertBayer8ToBgr(Mat destination)
        {
            var conversion = _pixelFormat switch
            {
                HikLinuxNative.PixelTypeGvspBayerRg8 => ColorConversionCodes.BayerRG2BGR,
                HikLinuxNative.PixelTypeGvspBayerGr8 => ColorConversionCodes.BayerGR2BGR,
                HikLinuxNative.PixelTypeGvspBayerGb8 => ColorConversionCodes.BayerGB2BGR,
                HikLinuxNative.PixelTypeGvspBayerBg8 => ColorConversionCodes.BayerBG2BGR,
                _ => (ColorConversionCodes?)null
            };

            if (conversion is null)
            {
                return false;
            }

            using var bayer = Mat.FromPixelData(_height, _width, MatType.CV_8UC1, _rawFrameBuffer, _width);
            Cv2.CvtColor(bayer, destination, conversion.Value);
            return true;
        }

        private void LogGrabFailure(int result, int transientFailures)
        {
            var shouldLog = transientFailures <= 5 ||
                            result != HikLinuxNative.MvENoData ||
                            transientFailures == 10 ||
                            transientFailures == MaxTransientGrabFailures;
            if (!shouldLog)
            {
                return;
            }

            _grabFailureLogCount++;
            CameraDiagnostics.Info(
                "hik-provider-linux",
                $"GetImageBuffer attempt failed. Result={FormatResult(result)}, Failures={transientFailures}, Logged={_grabFailureLogCount}");
        }

        private CameraFrameMetadata BuildFrameMetadata(HikLinuxNative.MvFrameOutInfoEx frameInfo)
        {
            var captureUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var rawDeviceTimestamp = ((long)frameInfo.DeviceTimestampHigh << 32) | frameInfo.DeviceTimestampLow;
            var rawHostTimestamp = frameInfo.HostTimestamp;

            if (_hostPtsNormalizer.TryNormalize(rawHostTimestamp, out var hostPtsMs))
            {
                return new CameraFrameMetadata(
                    hostPtsMs,
                    captureUtcMs,
                    CameraPtsSource.HostTimestamp,
                    rawDeviceTimestamp > 0 ? rawDeviceTimestamp : null,
                    rawHostTimestamp > 0 ? rawHostTimestamp : null,
                    frameInfo.FrameNumber);
            }

            if (_devicePtsNormalizer.TryNormalize(rawDeviceTimestamp, out var devicePtsMs))
            {
                return new CameraFrameMetadata(
                    devicePtsMs,
                    captureUtcMs,
                    CameraPtsSource.DeviceTimestamp,
                    rawDeviceTimestamp > 0 ? rawDeviceTimestamp : null,
                    rawHostTimestamp > 0 ? rawHostTimestamp : null,
                    frameInfo.FrameNumber);
            }

            return new CameraFrameMetadata(
                _fallbackPtsClock.Next(),
                captureUtcMs,
                CameraPtsSource.MonotonicClockFallback,
                rawDeviceTimestamp > 0 ? rawDeviceTimestamp : null,
                rawHostTimestamp > 0 ? rawHostTimestamp : null,
                frameInfo.FrameNumber);
        }
    }
}
