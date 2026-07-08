namespace VideoInferenceDemo;

public interface IDesktopPipelineSupportFactory
{
    TcnOnnxInferenceEngine? TryCreateTcnEngine();

    IVisionResultSink BuildCompatibilityVisionResultSink(
        SqliteResultWriter resultWriter,
        AnalysisEngine? analysisEngine,
        TcnFeatureWriter? featureWriter,
        TcnOnnxInferenceEngine? tcnEngine);
}
