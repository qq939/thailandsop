namespace VideoInferenceDemo;

internal static class FingerprintModuleRegisters
{
    public const ushort StartEnrollment = 0x0000;
    public const ushort CancelOperation = 0x0001;
    public const ushort DeleteFingerprint = 0x0002;
    public const ushort ClearDatabase = 0x0003;
    public const ushort LightRing = 0x0004;
    public const ushort RecognitionResult = 0x0010;
    public const ushort MatchScore = 0x0011;
    public const ushort TemplateCount = 0x0012;
    public const ushort ModuleStatus = 0x0013;
    public const ushort SlaveAddress = 0x1000;

    public const ushort CancelValue = 0x0001;
    public const ushort ClearDatabaseValue = 0x0001;
    public const ushort NoResultValue = 0xFFFF;
}
