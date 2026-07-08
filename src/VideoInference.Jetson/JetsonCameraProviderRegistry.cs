namespace VideoInferenceDemo;

public static class JetsonCameraProviderRegistry
{
    public static CameraProviderRegistry CreateDefault()
    {
        return new CameraProviderRegistry(new[]
        {
            new CameraProviderRegistration(
                CameraProviderIds.OpenCv,
                "OpenCV Camera",
                static () => new OpenCvCameraProvider()),
            new CameraProviderRegistration(
                CameraProviderIds.HikRobot,
                "HikRobot SDK",
                static () => new HikCameraProvider())
        });
    }
}
