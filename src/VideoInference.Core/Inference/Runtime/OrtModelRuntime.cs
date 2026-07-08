using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;

namespace VideoInferenceDemo;

public sealed class OrtModelRuntime : IModelRuntime
{
    private readonly string _modelPath;
    private readonly OrtSessionFactoryOptions _sessionFactoryOptions;
    private InferenceSession _session;
    private InferenceDeviceKind _deviceKind;
    private OrtExecutionProviderKind _provider;

    public OrtModelRuntime(
        string modelPath,
        InferenceDeviceKind deviceKind,
        string? preferredInputName = null,
        string? preferredOutputName = null,
        OrtExecutionProviderKind? forcedProvider = null)
        : this(
            modelPath,
            BuildDefaultOptions(deviceKind),
            preferredInputName,
            preferredOutputName,
            forcedProvider)
    {
    }

    public OrtModelRuntime(
        string modelPath,
        OrtSessionFactoryOptions sessionFactoryOptions,
        string? preferredInputName = null,
        string? preferredOutputName = null,
        OrtExecutionProviderKind? forcedProvider = null)
    {
        _modelPath = modelPath;
        _sessionFactoryOptions = sessionFactoryOptions ?? throw new ArgumentNullException(nameof(sessionFactoryOptions));
        _deviceKind = _sessionFactoryOptions.DeviceKind;
        PreferredInputName = preferredInputName;
        PreferredOutputName = preferredOutputName;

        _session = CreateSession(_deviceKind, forcedProvider, out var activeProvider);
        _provider = activeProvider;
        RefreshModelMetadata();
    }

    public string? ActiveDeviceLabel { get; private set; }
    public string InputName { get; private set; } = string.Empty;
    public string OutputName { get; private set; } = string.Empty;
    public IReadOnlyList<int> InputDimensions { get; private set; } = Array.Empty<int>();
    public IReadOnlyList<int> OutputDimensions { get; private set; } = Array.Empty<int>();
    public OrtExecutionProviderKind CurrentProvider => _provider;
    public string? PreferredInputName { get; }
    public string? PreferredOutputName { get; }

    public ModelOutput Run(ModelInput input)
    {
        return new ModelOutput(_session.Run(input.Inputs));
    }

    public bool TryFallbackToCpu(out string message)
    {
        message = string.Empty;

        try
        {
            if (_provider != OrtExecutionProviderKind.Cpu && TrySwapSession(InferenceDeviceKind.Cpu, OrtExecutionProviderKind.Cpu))
            {
                message = "GPU inference failed. Automatically switched to CPU.";
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            message = $"Inference fallback failed: {ex.Message}";
            return false;
        }
    }

    public void Dispose()
    {
        _session.Dispose();
    }

    private bool TrySwapSession(InferenceDeviceKind deviceKind, OrtExecutionProviderKind forcedProvider)
    {
        var newSession = CreateSession(deviceKind, forcedProvider, out var activeProvider);
        var old = _session;
        _session = newSession;
        _provider = activeProvider;
        _deviceKind = deviceKind;
        RefreshModelMetadata();
        old.Dispose();
        return true;
    }

    private InferenceSession CreateSession(InferenceDeviceKind deviceKind, OrtExecutionProviderKind? forcedProvider, out OrtExecutionProviderKind activeProvider)
    {
        var providerOrder = forcedProvider.HasValue ? new[] { forcedProvider.Value } : null;
        var options = CloneOptions(_sessionFactoryOptions, deviceKind, providerOrder);
        var bundle = OrtSessionFactory.Create(_modelPath, options);

        activeProvider = bundle.SelectedProvider;
        return bundle.Session;
    }

    private static OrtSessionFactoryOptions BuildDefaultOptions(InferenceDeviceKind deviceKind)
    {
        return new OrtSessionFactoryOptions
        {
            DeviceKind = deviceKind,
            InterOpNumThreads = 1,
            IntraOpNumThreads = 1
        };
    }

    private static OrtSessionFactoryOptions CloneOptions(
        OrtSessionFactoryOptions source,
        InferenceDeviceKind deviceKind,
        IReadOnlyList<OrtExecutionProviderKind>? providerOrder)
    {
        return new OrtSessionFactoryOptions
        {
            DeviceKind = deviceKind,
            ProviderOrder = providerOrder ?? source.ProviderOrder,
            NativeLibraryPath = source.NativeLibraryPath,
            DeviceId = source.DeviceId,
            TensorRtFp16 = source.TensorRtFp16,
            TensorRtEngineCache = source.TensorRtEngineCache,
            TensorRtEngineCachePath = source.TensorRtEngineCachePath,
            InterOpNumThreads = source.InterOpNumThreads,
            IntraOpNumThreads = source.IntraOpNumThreads,
            GraphOptimizationLevel = source.GraphOptimizationLevel,
            ExecutionMode = source.ExecutionMode,
            EnableCpuMemArena = source.EnableCpuMemArena,
            EnableMemoryPattern = source.EnableMemoryPattern
        };
    }

    private void RefreshModelMetadata()
    {
        var input = SelectName(_session.InputMetadata.Keys, PreferredInputName);
        var output = SelectName(_session.OutputMetadata.Keys, PreferredOutputName);

        InputName = input;
        OutputName = output;
        InputDimensions = _session.InputMetadata[input].Dimensions.ToArray();
        OutputDimensions = _session.OutputMetadata[output].Dimensions.ToArray();
        ActiveDeviceLabel = OrtSessionFactory.DescribeProvider(_provider);
    }

    private static string SelectName(IEnumerable<string> candidates, string? preferred)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var match = candidates.FirstOrDefault(name => string.Equals(name, preferred, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return candidates.First();
    }
}
