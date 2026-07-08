using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace VideoInferenceDemo;

internal sealed class HikLinuxProbeResult
{
    public bool NativeLibraryLoaded { get; init; }
    public uint? SdkVersion { get; init; }
    public int InitializeResult { get; init; }
    public int EnumerateResult { get; init; }
    public int? CreateHandleResult { get; init; }
    public int? OpenDeviceResult { get; init; }
    public int? CloseDeviceResult { get; init; }
    public int? DestroyHandleResult { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<HikNativeDeviceInfo> Devices { get; init; } = Array.Empty<HikNativeDeviceInfo>();
}

internal static class HikLinuxProbe
{
    public static HikLinuxProbeResult Run()
    {
        var nativeLibraryLoaded = false;
        var handle = IntPtr.Zero;
        int? createHandleResult = null;
        int? openDeviceResult = null;
        int? closeDeviceResult = null;
        int? destroyHandleResult = null;

        try
        {
            _ = NativeLibrary.Load("libMvCameraControl.so");
            nativeLibraryLoaded = true;

            var initializeResult = HikLinuxNative.Initialize();
            if (initializeResult != HikLinuxNative.MvOk)
            {
                return new HikLinuxProbeResult
                {
                    NativeLibraryLoaded = true,
                    InitializeResult = initializeResult,
                    ErrorMessage = $"MV_CC_Initialize failed with 0x{unchecked((uint)initializeResult):X8}."
                };
            }

            var sdkVersion = HikLinuxNative.GetSdkVersion();

            var deviceList = new HikLinuxNative.MvCcDeviceInfoList
            {
                DeviceInfoPointers = new IntPtr[256]
            };

            var enumerateResult = HikLinuxNative.EnumDevices(
                HikLinuxNative.MvGigEDevice | HikLinuxNative.MvUsbDevice,
                ref deviceList);

            if (enumerateResult != HikLinuxNative.MvOk)
            {
                return new HikLinuxProbeResult
                {
                    NativeLibraryLoaded = true,
                    SdkVersion = sdkVersion,
                    InitializeResult = initializeResult,
                    EnumerateResult = enumerateResult,
                    ErrorMessage = $"MV_CC_EnumDevices failed with 0x{unchecked((uint)enumerateResult):X8}."
                };
            }

            var devices = new List<HikNativeDeviceInfo>((int)deviceList.DeviceCount);
            for (var i = 0; i < deviceList.DeviceCount && i < deviceList.DeviceInfoPointers.Length; i++)
            {
                var ptr = deviceList.DeviceInfoPointers[i];
                if (ptr == IntPtr.Zero)
                {
                    continue;
                }

                var nativeInfo = Marshal.PtrToStructure<HikLinuxNative.MvCcDeviceInfo>(ptr);
                devices.Add(MapDevice(ptr, nativeInfo));
            }

            devices = DeduplicateDevices(devices)
                .OrderByDescending(item => item.IsAccessible)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (devices.Count > 0)
            {
                var first = devices[0];
                createHandleResult = HikLinuxNative.CreateHandle(out handle, first.DeviceInfoPointer);
                if (createHandleResult == HikLinuxNative.MvOk && handle != IntPtr.Zero)
                {
                    openDeviceResult = HikLinuxNative.OpenDevice(handle);
                    if (openDeviceResult == HikLinuxNative.MvOk)
                    {
                        closeDeviceResult = HikLinuxNative.CloseDevice(handle);
                    }

                    destroyHandleResult = HikLinuxNative.DestroyHandle(handle);
                    handle = IntPtr.Zero;
                }
            }

            return new HikLinuxProbeResult
            {
                NativeLibraryLoaded = nativeLibraryLoaded,
                SdkVersion = sdkVersion,
                InitializeResult = initializeResult,
                EnumerateResult = enumerateResult,
                CreateHandleResult = createHandleResult,
                OpenDeviceResult = openDeviceResult,
                CloseDeviceResult = closeDeviceResult,
                DestroyHandleResult = destroyHandleResult,
                Devices = devices
            };
        }
        catch (Exception ex)
        {
            return new HikLinuxProbeResult
            {
                NativeLibraryLoaded = nativeLibraryLoaded,
                InitializeResult = int.MinValue,
                EnumerateResult = int.MinValue,
                CreateHandleResult = createHandleResult,
                OpenDeviceResult = openDeviceResult,
                CloseDeviceResult = closeDeviceResult,
                DestroyHandleResult = destroyHandleResult,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            if (handle != IntPtr.Zero)
            {
                _ = HikLinuxNative.DestroyHandle(handle);
            }
        }
    }

    private static HikNativeDeviceInfo MapDevice(IntPtr deviceInfoPointer, HikLinuxNative.MvCcDeviceInfo nativeInfo)
    {
        var isAccessible = HikLinuxNative.IsDeviceAccessible(deviceInfoPointer, HikLinuxNative.MvAccessExclusive);
        if (nativeInfo.TransportLayerType == HikLinuxNative.MvGigEDevice)
        {
            var gigE = HikLinuxNative.ByteArrayToStruct<HikLinuxNative.MvGigEDeviceInfo>(nativeInfo.SpecialInfo.GigEInfo);
            return new HikNativeDeviceInfo(
                deviceInfoPointer,
                nativeInfo.TransportLayerType,
                HikLinuxNative.ReadUtf8(gigE.ManufacturerName),
                HikLinuxNative.ReadUtf8(gigE.ModelName),
                HikLinuxNative.ReadUtf8(gigE.SerialNumber),
                HikLinuxNative.ReadUtf8(gigE.UserDefinedName),
                HikLinuxNative.FormatIpv4(gigE.CurrentIp),
                isAccessible);
        }

        var usb = HikLinuxNative.ByteArrayToStruct<HikLinuxNative.MvUsb3DeviceInfo>(nativeInfo.SpecialInfo.Usb3VInfo);
        return new HikNativeDeviceInfo(
            deviceInfoPointer,
            nativeInfo.TransportLayerType,
            HikLinuxNative.ReadUtf8(usb.ManufacturerName),
            HikLinuxNative.ReadUtf8(usb.ModelName),
            HikLinuxNative.ReadUtf8(usb.SerialNumber),
            HikLinuxNative.ReadUtf8(usb.UserDefinedName),
            null,
            isAccessible);
    }

    private static IEnumerable<HikNativeDeviceInfo> DeduplicateDevices(IEnumerable<HikNativeDeviceInfo> devices)
    {
        return devices
            .GroupBy(BuildIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.IsAccessible)
                .First());
    }

    private static string BuildIdentity(HikNativeDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.SerialNumber))
        {
            return $"sn:{device.SerialNumber.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(device.UserDefinedName))
        {
            return $"user:{device.UserDefinedName.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(device.IpAddress))
        {
            return $"ip:{device.IpAddress.Trim()}";
        }

        if (!string.IsNullOrWhiteSpace(device.ModelName))
        {
            return $"model:{device.ModelName.Trim()}:{device.TransportLayerType:X8}";
        }

        return $"{device.ManufacturerName?.Trim()}:{device.TransportLayerType:X8}";
    }
}
