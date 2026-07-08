using System;
using System.Collections.Generic;
using System.IO;

namespace VideoInferenceDemo;

public sealed class DesktopPipelineSupportFactory : IDesktopPipelineSupportFactory
{
    private static readonly string TcnInferenceConfigPath = Path.Combine(AppContext.BaseDirectory, "tcn_infer_config.json");
    private static readonly string TcnFeatureConfigPath = Path.Combine(AppContext.BaseDirectory, "tcn_feature_config.json");
    private static readonly string TcnFeatureInitLogPath = Path.Combine(AppContext.BaseDirectory, "tcn_feature_init.log");
    private static readonly string TcnInferenceInitLogPath = Path.Combine(AppContext.BaseDirectory, "tcn_infer_init.log");

    public TcnOnnxInferenceEngine? TryCreateTcnEngine()
    {
        try
        {
            var config = TcnInferenceConfig.Load(TcnInferenceConfigPath);
            if (!config.IsUsable)
            {
                return null;
            }

            var featureConfig = TcnFeatureConfig.Load(TcnFeatureConfigPath);
            if (!featureConfig.IsUsable)
            {
                return null;
            }

            return new TcnOnnxInferenceEngine(config, featureConfig.FeatureDim);
        }
        catch (Exception ex)
        {
            TryAppendLog(TcnInferenceInitLogPath, ex);
            return null;
        }
    }

    public IVisionResultSink BuildCompatibilityVisionResultSink(
        SqliteResultWriter resultWriter,
        AnalysisEngine? analysisEngine,
        TcnFeatureWriter? featureWriter,
        TcnOnnxInferenceEngine? tcnEngine)
    {
        ArgumentNullException.ThrowIfNull(resultWriter);

        var legacyDetectionSinks = new List<ILegacyDetectionResultSink> { resultWriter };
        if (analysisEngine != null)
        {
            legacyDetectionSinks.Add(analysisEngine);
        }

        try
        {
            if (featureWriter == null && tcnEngine == null)
            {
                return new LegacyDetectionCompatibilityVisionResultSink(
                    new CompositeLegacyDetectionResultSink(legacyDetectionSinks));
            }

            var config = TcnFeatureConfig.Load(TcnFeatureConfigPath);
            if (config.IsUsable)
            {
                var preprocessor = new TopKDetectionsPreprocessor(config);
                var consumers = new List<ITcnFeatureConsumer>();
                if (featureWriter != null)
                {
                    var version = config.ToVersion();
                    featureWriter.RegisterVersion(version);
                    consumers.Add(new TcnFeatureWriterConsumer(featureWriter, version));
                }

                if (tcnEngine != null)
                {
                    consumers.Add(new TcnInferenceConsumer(tcnEngine));
                }

                if (consumers.Count > 0)
                {
                    legacyDetectionSinks.Add(new TcnFeatureFanoutLegacyDetectionSink(preprocessor, consumers));
                }
            }
        }
        catch (Exception ex)
        {
            TryAppendLog(TcnFeatureInitLogPath, ex);
        }

        return new LegacyDetectionCompatibilityVisionResultSink(
            new CompositeLegacyDetectionResultSink(legacyDetectionSinks));
    }

    private static void TryAppendLog(string path, Exception ex)
    {
        try
        {
            File.AppendAllText(
                path,
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {ex}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
