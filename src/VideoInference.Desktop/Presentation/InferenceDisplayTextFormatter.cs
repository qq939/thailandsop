namespace VideoInferenceDemo;

public static class InferenceDisplayTextFormatter
{
    public static string GetStatusText(ModelActivationAttemptResult attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (attempt.Activation != null)
        {
            return GetStatusText(attempt.Activation.Binding);
        }

        return attempt.State switch
        {
            ModelActivationState.NoModel => "DL 鐩綍涓嬫湭鍙戠幇鍙敤妯″瀷銆?",
            _ => "妯″瀷鍔犺浇澶辫触"
        };
    }

    public static string GetDeviceText(ModelActivationAttemptResult attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return attempt.Activation != null ? GetDeviceText(attempt.Activation.Binding) : "-";
    }

    private static string GetStatusText(PrimaryVisionTaskBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return binding.Definition.TaskKind switch
        {
            VisionTaskKind.UnetSegmentation => $"{binding.Definition.DisplayName} / U-Net Segmentation ONNX",
            VisionTaskKind.PresenceClassification => $"{binding.Definition.DisplayName} / 产品有无分类 ONNX",
            VisionTaskKind.Segmentation => $"{binding.Definition.DisplayName} / Segmentation ONNX",
            VisionTaskKind.SequenceBands => $"{binding.Definition.DisplayName} / Sequence ONNX",
            _ => $"{binding.Definition.DisplayName} / Detection ONNX"
        };
    }

    private static string GetDeviceText(PrimaryVisionTaskBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return binding.Context.OnnxDeviceKind switch
        {
            InferenceDeviceKind.Cpu => "OnnxRuntime / CPU",
            _ => "OnnxRuntime / GPU (Auto)"
        };
    }
}
