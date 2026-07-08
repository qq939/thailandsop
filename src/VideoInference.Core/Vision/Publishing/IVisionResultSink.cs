namespace VideoInferenceDemo;

public interface IVisionResultSink
{
    bool TryPublish(VisionFrameResult result);
}
