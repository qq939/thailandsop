using System;
using System.Collections.Generic;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public sealed class InspectionActionRegistry
{
    private readonly Dictionary<string, Func<IInspectionAction>> _factories = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string actionType, Func<IInspectionAction> factory)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            throw new ArgumentException("Action type is required.", nameof(actionType));
        }

        _factories[actionType.Trim()] = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public IInspectionAction Resolve(string actionType)
    {
        var key = string.IsNullOrWhiteSpace(actionType) ? InspectionActionTypes.RoiInspection : actionType.Trim();
        if (_factories.TryGetValue(key, out var factory))
        {
            return factory();
        }

        throw new InvalidOperationException($"Inspection action '{key}' is not registered.");
    }

    public static InspectionActionRegistry CreateDefault(string recipeCatalogPath, string modelConfigPath)
    {
        var registry = new InspectionActionRegistry();
        var modelRuntimeRegistry = new InspectionModelRuntimeRegistry(modelConfigPath);
        registry.Register(
            InspectionActionTypes.RoiInspection,
            () => new RoiInferenceInspectionAction(
                new InspectionRecipeCatalogProvider(recipeCatalogPath),
                new PassThroughModelReferenceResolver(),
                modelRuntimeRegistry));
        return registry;
    }
}
