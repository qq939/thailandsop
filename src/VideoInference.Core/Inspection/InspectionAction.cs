namespace VideoInferenceDemo;

public sealed class InspectionAction : IInspectionAction
{
    private readonly IInspectionRecipeProvider _recipeProvider;
    private readonly IModelReferenceResolver _modelReferenceResolver;

    public InspectionAction(
        IInspectionRecipeProvider recipeProvider,
        IModelReferenceResolver modelReferenceResolver)
    {
        _recipeProvider = recipeProvider;
        _modelReferenceResolver = modelReferenceResolver;
    }

    public InspectionCycleResult Execute(InspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.OriginalImage);

        var recipe = _recipeProvider.Get(request.RecipeKey);
        var resolvedModels = _modelReferenceResolver.Resolve(recipe)
            .OrderBy(reference => reference.Sequence)
            .ToArray();
        var enabledRois = recipe.Rois
            .Where(roi => roi.Enabled &&
                          (string.IsNullOrWhiteSpace(roi.CameraId) ||
                           string.Equals(roi.CameraId, request.CameraId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(roi => roi.SortOrder)
            .ToArray();
        var roiResults = enabledRois
            .Select(roi => new InspectionRoiResult
            {
                RoiId = roi.Id,
                RoiName = roi.Name,
                ModelId = roi.ModelId,
                Decision = InspectionCycleDecision.Unknown
            })
            .ToArray();

        return new InspectionCycleResult
        {
            RecipeKey = recipe.Key,
            StationId = request.StationId,
            TaskInstanceId = request.TaskInstanceId,
            CameraId = request.CameraId,
            ActionType = request.ActionType,
            TriggerId = request.TriggerId,
            TriggerTime = request.TriggerTime,
            Operator = request.Operator,
            Decision = AggregateDecision(roiResults.Select(item => item.Decision)),
            SummaryMessage = $"Recipe '{recipe.Key.ProductModel}/{recipe.Key.TaskId}/{recipe.Key.PositionNo}' resolved.",
            Calibration = recipe.Calibration,
            ResolvedModels = resolvedModels,
            ResolvedRois = enabledRois,
            RoiResults = roiResults
        };
    }

    private static InspectionCycleDecision AggregateDecision(IEnumerable<InspectionCycleDecision> decisions)
    {
        var values = decisions.ToArray();
        if (values.Any(decision => decision == InspectionCycleDecision.Ng))
        {
            return InspectionCycleDecision.Ng;
        }

        if (values.Any(decision => decision == InspectionCycleDecision.Warning))
        {
            return InspectionCycleDecision.Warning;
        }

        if (values.Any(decision => decision == InspectionCycleDecision.Ok))
        {
            return InspectionCycleDecision.Ok;
        }

        return InspectionCycleDecision.Unknown;
    }
}
