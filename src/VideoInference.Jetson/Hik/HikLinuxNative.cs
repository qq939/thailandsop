using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VideoInferenceDemo;

internal static class HikLinuxNative
{
    private const string LibraryName = "libMvCameraControl.so";
    private const int MaxDeviceCount = 256;
    private const int InfoMaxBufferSize = 64;
    private const int MaxSymbolicBufferSize = 64;
    private const int MaxSpecialInfoSize = 540;
    private const int GigESpecialInfoSize = 216;

    public const uint MvGigEDevice = 0x00000001;
    public const uint MvUsbDevice = 0x00000004;
    public const uint MvAccessExclusive = 1;
    public const int MvENoData = unchecked((int)0x80000007);
    public const int MvELoadLibrary = unchecked((int)0x8000000C);
    public const int MvEPacket = unchecked((int)0x80000015);
    public const int MvOk = 0;
    public const uint PixelTypeGvspBgr8Packed = 0x02180015;
    public const uint PixelTypeGvspMono8 = 0x01080001;
    public const uint PixelTypeGvspBayerGr8 = 0x01080008;
    public const uint PixelTypeGvspBayerRg8 = 0x01080009;
    public const uint PixelTypeGvspBayerGb8 = 0x0108000A;
    public const uint PixelTypeGvspBayerBg8 = 0x0108000B;
    public const uint GrabStrategyLatestImages = 2;

    public static uint GetSdkVersion() => MV_CC_GetSDKVersion();

    public static int Initialize() => MV_CC_Initialize();

    public static int FinalizeSdk() => MV_CC_Finalize();

    public static int EnumDevices(uint transportLayerMask, ref MvCcDeviceInfoList devices) =>
        MV_CC_EnumDevices(transportLayerMask, ref devices);

    public static int CreateHandle(out IntPtr handle, IntPtr deviceInfoPointer) =>
        MV_CC_CreateHandle(out handle, deviceInfoPointer);

    public static bool IsDeviceAccessible(IntPtr deviceInfoPointer, uint accessMode) =>
        MV_CC_IsDeviceAccessible(deviceInfoPointer, accessMode);

    public static int StartGrabbing(IntPtr handle) => MV_CC_StartGrabbing(handle);

    public static int StopGrabbing(IntPtr handle) => MV_CC_StopGrabbing(handle);

    public static int SetImageNodeNum(IntPtr handle, uint count) => MV_CC_SetImageNodeNum(handle, count);

    public static int SetOutputQueueSize(IntPtr handle, uint count) => MV_CC_SetOutputQueueSize(handle, count);

    public static int SetGrabStrategy(IntPtr handle, uint strategy) => MV_CC_SetGrabStrategy(handle, strategy);

    public static int GetImageForBgr(IntPtr handle, IntPtr destination, uint destinationSize, ref MvFrameOutInfoEx frameInfo, int timeoutMs) =>
        MV_CC_GetImageForBGR(handle, destination, destinationSize, ref frameInfo, timeoutMs);

    public static int GetOneFrameTimeout(IntPtr handle, IntPtr destination, uint destinationSize, IntPtr frameInfo, int timeoutMs) =>
        MV_CC_GetOneFrameTimeout(handle, destination, destinationSize, frameInfo, timeoutMs);

    public static int GetIntValueEx(IntPtr handle, string key, ref MvccIntValueEx value) =>
        MV_CC_GetIntValueEx(handle, key, ref value);

    public static int SetIntValueEx(IntPtr handle, string key, long value) =>
        MV_CC_SetIntValueEx(handle, key, value);

    public static int GetEnumValue(IntPtr handle, string key, ref MvccEnumValue value) =>
        MV_CC_GetEnumValue(handle, key, ref value);

    public static int GetEnumEntrySymbolic(IntPtr handle, string key, ref MvccEnumEntry entry) =>
        MV_CC_GetEnumEntrySymbolic(handle, key, ref entry);

    public static int GetBoolValue(IntPtr handle, string key, out bool value) =>
        MV_CC_GetBoolValue(handle, key, out value);

    public static int GetFloatValue(IntPtr handle, string key, ref MvccFloatValue value) =>
        MV_CC_GetFloatValue(handle, key, ref value);

    public static int SetEnumValueByString(IntPtr handle, string key, string value) =>
        MV_CC_SetEnumValueByString(handle, key, value);

    public static int SetBoolValue(IntPtr handle, string key, bool value) =>
        MV_CC_SetBoolValue(handle, key, value);

    public static int SetFloatValue(IntPtr handle, string key, float value) =>
        MV_CC_SetFloatValue(handle, key, value);

    public static int SetCommandValue(IntPtr handle, string key) =>
        MV_CC_SetCommandValue(handle, key);

    public static int GetImageBuffer(IntPtr handle, ref MvFrameOut frame, int timeoutMs) =>
        MV_CC_GetImageBuffer(handle, ref frame, timeoutMs);

    public static int FreeImageBuffer(IntPtr handle, ref MvFrameOut frame) =>
        MV_CC_FreeImageBuffer(handle, ref frame);

    public static int ConvertPixelTypeEx(IntPtr handle, ref MvCcPixelConvertParamEx convertParam) =>
        MV_CC_ConvertPixelTypeEx(handle, ref convertParam);

    public static int GetOptimalPacketSize(IntPtr handle) => MV_CC_GetOptimalPacketSize(handle);

    public static int SetGevScpsPacketSize(IntPtr handle, uint value) => MV_GIGE_SetGevSCPSPacketSize(handle, value);

    public static int OpenDevice(IntPtr handle, uint accessMode = MvAccessExclusive, ushort switchoverKey = 0) =>
        MV_CC_OpenDevice(handle, accessMode, switchoverKey);

    public static int CloseDevice(IntPtr handle) => MV_CC_CloseDevice(handle);

    public static int DestroyHandle(IntPtr handle) => MV_CC_DestroyHandle(handle);

    public static string ReadUtf8(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
        {
            length = buffer.Length;
        }

        return Encoding.UTF8.GetString(buffer, 0, length).Trim();
    }

    public static string FormatIpv4(uint value)
    {
        var b1 = value & 0xFF;
        var b2 = (value >> 8) & 0xFF;
        var b3 = (value >> 16) & 0xFF;
        var b4 = (value >> 24) & 0xFF;
        return $"{b1}.{b2}.{b3}.{b4}";
    }

    [DllImport(LibraryName, EntryPoint = "MV_CC_Initialize")]
    private static extern int MV_CC_Initialize();

    [DllImport(LibraryName, EntryPoint = "MV_CC_Finalize")]
    private static extern int MV_CC_Finalize();

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetSDKVersion")]
    private static extern uint MV_CC_GetSDKVersion();

    [DllImport(LibraryName, EntryPoint = "MV_CC_EnumDevices")]
    private static extern int MV_CC_EnumDevices(uint transportLayerMask, ref MvCcDeviceInfoList deviceList);

    [DllImport(LibraryName, EntryPoint = "MV_CC_CreateHandle")]
    private static extern int MV_CC_CreateHandle(out IntPtr handle, IntPtr deviceInfoPointer);

    [DllImport(LibraryName, EntryPoint = "MV_CC_IsDeviceAccessible")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool MV_CC_IsDeviceAccessible(IntPtr deviceInfoPointer, uint accessMode);

    [DllImport(LibraryName, EntryPoint = "MV_CC_StartGrabbing")]
    private static extern int MV_CC_StartGrabbing(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "MV_CC_StopGrabbing")]
    private static extern int MV_CC_StopGrabbing(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "MV_CC_SetImageNodeNum")]
    private static extern int MV_CC_SetImageNodeNum(IntPtr handle, uint count);

    [DllImport(LibraryName, EntryPoint = "MV_CC_SetOutputQueueSize")]
    private static extern int MV_CC_SetOutputQueueSize(IntPtr handle, uint count);

    [DllImport(LibraryName, EntryPoint = "MV_CC_SetGrabStrategy")]
    private static extern int MV_CC_SetGrabStrategy(IntPtr handle, uint strategy);

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetImageForBGR")]
    private static extern int MV_CC_GetImageForBGR(
        IntPtr handle,
        IntPtr destination,
        uint destinationSize,
        ref MvFrameOutInfoEx frameInfo,
        int timeoutMs);

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetOneFrameTimeout")]
    private static extern int MV_CC_GetOneFrameTimeout(
        IntPtr handle,
        IntPtr destination,
        uint destinationSize,
        IntPtr frameInfo,
        int timeoutMs);

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetIntValueEx")]
    private static extern int MV_CC_GetIntValueEx(IntPtr handle, string key, ref MvccIntValueEx value);

    [DllImport(LibraryName, EntryPoint = "MV_CC_SetIntValueEx")]
    private static extern int MV_CC_SetIntValueEx(IntPtr handle, string key, long value);

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetEnumValue")]
    private static extern int MV_CC_GetEnumValue(IntPtr handle, string key, ref MvccEnumValue value);

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetEnumEntrySymbolic")]
    private static extern int MV_CC_GetEnumEntrySymbolic(IntPtr handle, string key, ref MvccEnumEntry entry);

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetBoolValue")]
    private static extern int MV_CC_GetBoolValue(IntPtr handle, string key, [MarshalAs(UnmanagedType.I1)] out bool value);

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetFloatValue")]
    private static extern int MV_CC_GetFloatValue(IntPtr handle, string key, ref MvccFloatValue value);

    [DllImport(LibraryName, EntryPoint = "MV_CC_SetEnumValueByString")]
    private static extern int MV_CC_SetEnumValueByString(IntPtr handle, string key, string value);

    [DllImport(LibraryName, EntryPoint = "MV_CC_SetBoolValue")]
    private static extern int MV_CC_SetBoolValue(IntPtr handle, string key, [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport(LibraryName, EntryPoint = "MV_CC_SetFloatValue")]
    private static extern int MV_CC_SetFloatValue(IntPtr handle, string key, float value);

    [DllImport(LibraryName, EntryPoint = "MV_CC_SetCommandValue")]
    private static extern int MV_CC_SetCommandValue(IntPtr handle, string key);

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetImageBuffer")]
    private static extern int MV_CC_GetImageBuffer(IntPtr handle, ref MvFrameOut frame, int timeoutMs);

    [DllImport(LibraryName, EntryPoint = "MV_CC_FreeImageBuffer")]
    private static extern int MV_CC_FreeImageBuffer(IntPtr handle, ref MvFrameOut frame);

    [DllImport(LibraryName, EntryPoint = "MV_CC_ConvertPixelTypeEx")]
    private static extern int MV_CC_ConvertPixelTypeEx(IntPtr handle, ref MvCcPixelConvertParamEx convertParam);

    [DllImport(LibraryName, EntryPoint = "MV_CC_GetOptimalPacketSize")]
    private static extern int MV_CC_GetOptimalPacketSize(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "MV_GIGE_SetGevSCPSPacketSize")]
    private static extern int MV_GIGE_SetGevSCPSPacketSize(IntPtr handle, uint value);

    [DllImport(LibraryName, EntryPoint = "MV_CC_OpenDevice")]
    private static extern int MV_CC_OpenDevice(IntPtr handle, uint accessMode, ushort switchoverKey);

    [DllImport(LibraryName, EntryPoint = "MV_CC_CloseDevice")]
    private static extern int MV_CC_CloseDevice(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "MV_CC_DestroyHandle")]
    private static extern int MV_CC_DestroyHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvCcDeviceInfoList
    {
        public uint DeviceCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxDeviceCount)]
        public IntPtr[] DeviceInfoPointers;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvCcDeviceInfo
    {
        public ushort MajorVersion;
        public ushort MinorVersion;
        public uint MacAddressHigh;
        public uint MacAddressLow;
        public uint TransportLayerType;
        public uint DeviceTypeInfo;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public uint[] Reserved;

        public MvCcDeviceSpecialInfo SpecialInfo;
    }

    [StructLayout(LayoutKind.Explicit, Size = MaxSpecialInfoSize)]
    internal struct MvCcDeviceSpecialInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = GigESpecialInfoSize)]
        [FieldOffset(0)]
        public byte[] GigEInfo;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxSpecialInfoSize)]
        [FieldOffset(0)]
        public byte[] Usb3VInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvGigEDeviceInfo
    {
        public uint IpConfigOption;
        public uint IpConfigCurrent;
        public uint CurrentIp;
        public uint CurrentSubnetMask;
        public uint DefaultGateway;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ManufacturerName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ModelName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] DeviceVersion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)]
        public byte[] ManufacturerSpecificInfo;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] SerialNumber;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] UserDefinedName;

        public uint NetExport;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvUsb3DeviceInfo
    {
        public byte ControlInEndPoint;
        public byte ControlOutEndPoint;
        public byte StreamEndPoint;
        public byte EventEndPoint;
        public ushort VendorId;
        public ushort ProductId;
        public uint DeviceNumber;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = InfoMaxBufferSize)]
        public byte[] DeviceGuid;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = InfoMaxBufferSize)]
        public byte[] VendorName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = InfoMaxBufferSize)]
        public byte[] ModelName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = InfoMaxBufferSize)]
        public byte[] FamilyName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = InfoMaxBufferSize)]
        public byte[] DeviceVersion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = InfoMaxBufferSize)]
        public byte[] ManufacturerName;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = InfoMaxBufferSize)]
        public byte[] SerialNumber;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = InfoMaxBufferSize)]
        public byte[] UserDefinedName;

        public uint BcdUsb;
        public uint DeviceAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvccIntValueEx
    {
        public long CurrentValue;
        public long Max;
        public long Min;
        public long Increment;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvccEnumValue
    {
        public uint CurrentValue;
        public uint SupportedCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public uint[] SupportedValues;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvccEnumEntry
    {
        public uint Value;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxSymbolicBufferSize)]
        public byte[] Symbolic;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvccFloatValue
    {
        public float CurrentValue;
        public float Max;
        public float Min;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvFrameOutInfoEx
    {
        public ushort Width;
        public ushort Height;
        public uint PixelType;
        public uint FrameNumber;
        public uint DeviceTimestampHigh;
        public uint DeviceTimestampLow;
        public uint Reserved0;
        public long HostTimestamp;
        public uint FrameLength;
        public uint SecondCount;
        public uint CycleCount;
        public uint CycleOffset;
        public float Gain;
        public float ExposureTime;
        public uint AverageBrightness;
        public uint Red;
        public uint Green;
        public uint Blue;
        public uint FrameCounter;
        public uint TriggerIndex;
        public uint Input;
        public uint Output;
        public ushort OffsetX;
        public ushort OffsetY;
        public ushort ChunkWidth;
        public ushort ChunkHeight;
        public uint LostPacket;
        public uint UnparsedChunkCount;
        public IntPtr UnparsedChunkContent;
        public uint ExtendedWidth;
        public uint ExtendedHeight;
        public ulong FrameLengthEx;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvFrameOut
    {
        public IntPtr BufferAddress;
        public MvFrameOutInfoEx FrameInfo;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public uint[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MvCcPixelConvertParamEx
    {
        public uint Width;
        public uint Height;
        public uint SourcePixelType;
        public IntPtr SourceData;
        public uint SourceDataLength;
        public uint DestinationPixelType;
        public IntPtr DestinationBuffer;
        public uint DestinationLength;
        public uint DestinationBufferSize;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public uint[] Reserved;
    }

    public static T ByteArrayToStruct<T>(byte[] bytes) where T : struct
    {
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
        }
        finally
        {
            handle.Free();
        }
    }
}

internal sealed record HikNativeDeviceInfo(
    IntPtr DeviceInfoPointer,
    uint TransportLayerType,
    string ManufacturerName,
    string ModelName,
    string SerialNumber,
    string UserDefinedName,
    string? IpAddress,
    bool IsAccessible)
{
    public string DisplayName
    {
        get
        {
            var label = !string.IsNullOrWhiteSpace(UserDefinedName)
                ? UserDefinedName
                : !string.IsNullOrWhiteSpace(SerialNumber)
                    ? SerialNumber
                    : ModelName;

            if (string.IsNullOrWhiteSpace(label))
            {
                label = "Unknown Hik camera";
            }

            return !string.IsNullOrWhiteSpace(ModelName) &&
                   !string.Equals(label, ModelName, StringComparison.OrdinalIgnoreCase)
                ? $"{ModelName} ({label})"
                : label;
        }
    }
}
