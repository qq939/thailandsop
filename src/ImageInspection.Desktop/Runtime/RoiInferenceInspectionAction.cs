using System.Diagnostics;
using OpenCvSharp;

namespace VideoInferenceDemo.ImageInspection.Runtime;

public interface IWarmupInspectionAction
{
    void Warmup(InspectionRecipeKey recipeKey);
}

public sealed class RoiInferenceInspectionAction : IInspectionAction, IWarmupInspectionAction
{
    private readonly IInspectionRecipeProvider _recipeProvider;
    private readonly IModelReferenceResolver _modelReferenceResolver;
    private readonly IInspectionModelRuntime _modelRuntimeRegistry;

    public RoiInferenceInspectionAction(
        IInspectionRecipeProvider recipeProvider,
        IModelReferenceResolver modelReferenceResolver,
        IInspectionModelRuntime modelRuntimeRegistry)
    {
        _recipeProvider = recipeProvider;
        _modelReferenceResolver = modelReferenceResolver;
        _modelRuntimeRegistry = modelRuntimeRegistry;
    }

    public void Warmup(InspectionRecipeKey recipeKey)
    {
        var recipe = _recipeProvider.Get(recipeKey);
        foreach (var modelId in recipe.Rois
                     .Where(roi => roi.Enabled && !string.IsNullOrWhiteSpace(roi.ModelId))
                     .Select(roi => roi.ModelId!)
                     .Concat(recipe.AlignmentByCameraId.Values
                         .Where(alignment => alignment.Enabled && !string.IsNullOrWhiteSpace(alignment.LocatorModelId))
                         .Select(alignment => alignment.LocatorModelId))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _modelRuntimeRegistry.Warmup(modelId);
        }
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

        IReadOnlyList<RoiDefinition> activeRois = enabledRois;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var alignmentSummary = string.Empty;
        if (TryResolveAlignment(recipe, request, enabledRois, out var alignment))
        {
            if (!alignment.Success)
            {
                var failedResults = enabledRois
                    .Select(roi => CreateFailureResult(roi, "global-alignment-failed", alignment.Message))
                    .ToArray();
                foreach (var item in alignment.Metadata)
                {
                    metadata[item.Key] = item.Value;
                }

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
                    Decision = InspectionCycleDecision.Ng,
                    SummaryMessage = $"Recipe '{recipe.Key.ProductModel}/{recipe.Key.TaskId}/{recipe.Key.PositionNo}' global alignment failed. {alignment.Message}",
                    Calibration = recipe.Calibration,
                    ResolvedModels = resolvedModels,
                    ResolvedRois = enabledRois,
                    RoiResults = failedResults,
                    Metadata = metadata
                };
            }

            activeRois = alignment.Rois;
            alignmentSummary = $" Alignment score={alignment.Score:0.###}, dx={alignment.Dx:0.0}px, dy={alignment.Dy:0.0}px, angle={alignment.AngleDeg:0.#}.";
            foreach (var item in alignment.Metadata)
            {
                metadata[item.Key] = item.Value;
            }
        }

        var roiResults = new List<InspectionRoiResult>(activeRois.Count);
        foreach (var roi in activeRois)
        {
            roiResults.Add(ExecuteRoi(request.OriginalImage, roi));
        }

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
            Decision = roiResults.Any(item => item.Decision == InspectionCycleDecision.Ng)
                ? InspectionCycleDecision.Ng
                : InspectionCycleDecision.Ok,
            SummaryMessage = $"Recipe '{recipe.Key.ProductModel}/{recipe.Key.TaskId}/{recipe.Key.PositionNo}' resolved. ROI={roiResults.Count}; Models={resolvedModels.Length}.{alignmentSummary}",
            Calibration = recipe.Calibration,
            ResolvedModels = resolvedModels,
            ResolvedRois = activeRois,
            RoiResults = roiResults,
            Metadata = metadata
        };
    }

    private bool TryResolveAlignment(
        InspectionRecipe recipe,
        InspectionRequest request,
        IReadOnlyList<RoiDefinition> enabledRois,
        out CameraAlignmentRuntimeResult result)
    {
        result = CameraAlignmentRuntimeResult.NotConfigured(enabledRois);
        if (string.IsNullOrWhiteSpace(request.CameraId) ||
            !recipe.AlignmentByCameraId.TryGetValue(request.CameraId, out var config) ||
            config is not { Enabled: true })
        {
            return false;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["alignment.enabled"] = "true",
            ["alignment.modelId"] = config.LocatorModelId,
            ["alignment.classId"] = config.LocatorClassId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["alignment.className"] = config.LocatorClassName
        };

        if (string.IsNullOrWhiteSpace(config.LocatorModelId))
        {
            result = CameraAlignmentRuntimeResult.Failed(enabledRois, "Global alignment locator model is not configured.", metadata);
            return true;
        }

        try
        {
            var execution = _modelRuntimeRegistry.Execute(config.LocatorModelId, request.OriginalImage);
            foreach (var item in execution.Metrics)
            {
                metadata[$"alignment.{item.Key}"] = item.Value;
            }

            if (execution.Payload is not ObbDetectionPayload payload)
            {
                result = CameraAlignmentRuntimeResult.Failed(
                    enabledRois,
                    $"Locator model '{config.LocatorModelId}' is not an OBB detection model.",
                    metadata);
                return true;
            }

            if (!TryReadLocatorMinScore(execution.Metrics, out var minScore))
            {
                result = CameraAlignmentRuntimeResult.Failed(
                    enabledRois,
                    $"Locator model '{config.LocatorModelId}' must declare locator.minScore in model.json.",
                    metadata);
                return true;
            }

            var best = payload.Detections
                .Where(det => MatchesLocatorClass(det, config))
                .OrderByDescending(det => det.Score)
                .FirstOrDefault();
            if (best == null)
            {
                result = CameraAlignmentRuntimeResult.Failed(
                    enabledRois,
                    $"Locator model '{config.LocatorModelId}' returned no matching OBB result.",
                    metadata);
                return true;
            }

            metadata["alignment.score"] = best.Score.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            metadata["alignment.minScore"] = minScore.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            metadata["alignment.detectedClassId"] = best.ClassId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["alignment.detectedClassName"] = best.ClassName;
            if (best.Score < minScore)
            {
                result = CameraAlignmentRuntimeResult.Failed(
                    enabledRois,
                    $"Locator score {best.Score:0.###} is below minScore {minScore:0.###}.",
                    metadata);
                return true;
            }

            result = BuildAlignedRois(request.OriginalImage, enabledRois, config, best, metadata);
            return true;
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("inspection-action", $"Global alignment failed for camera '{request.CameraId}'.", ex);
            result = CameraAlignmentRuntimeResult.Failed(enabledRois, ex.Message, metadata);
            return true;
        }
    }

    private InspectionRoiResult ExecuteRoi(Mat source, RoiDefinition roi)
    {
        if (string.IsNullOrWhiteSpace(roi.ModelId))
        {
            return new InspectionRoiResult
            {
                RoiId = roi.Id,
                RoiName = roi.Name,
                ModelId = roi.ModelId,
                Decision = InspectionCycleDecision.Unknown,
                Findings =
                [
                    new InspectionFinding
                    {
                        Code = "model-unassigned",
                        Message = "ROI has no model assigned.",
                        Severity = InspectionCycleDecision.Unknown
                    }
                ]
            };
        }

        try
        {
            var totalWatch = Stopwatch.StartNew();
            var cropWatch = Stopwatch.StartNew();
            using var roiImage = ExtractHorizontalRoi(source, roi);
            cropWatch.Stop();

            if (roiImage.Empty())
            {
                return CreateFailureResult(roi, "roi-empty", "ROI image is empty.");
            }

            var modelWatch = Stopwatch.StartNew();
            var execution = _modelRuntimeRegistry.Execute(roi.ModelId, roiImage);
            modelWatch.Stop();

            var convertWatch = Stopwatch.StartNew();

            // OCR 文本识别 — 不走 detection/NG 判定，直接返回识别文本
            if (execution.Payload is OcrPayload ocr)
            {
                convertWatch.Stop();
                totalWatch.Stop();

                var ocrText = ocr.Text.Trim();
                CameraDiagnostics.Info("ocr", $"ROI '{roi.Name}' \"{ocrText}\" len={ocrText.Length}");

                return new InspectionRoiResult
                {
                    RoiId = roi.Id,
                    RoiName = roi.Name,
                    ModelId = roi.ModelId,
                    Decision = InspectionCycleDecision.Ok,
                    Findings = string.IsNullOrWhiteSpace(ocr.Text)
                        ? Array.Empty<InspectionFinding>()
                        : new[]
                        {
                            new InspectionFinding
                            {
                                Code = "ocr",
                                Message = ocr.Text.Trim(),
                                Severity = InspectionCycleDecision.Ok
                            }
                        },
                    Metrics = new Dictionary<string, string>
                    {
                        ["device"] = execution.DeviceLabel ?? string.Empty,
                        ["taskKind"] = execution.TaskKind.ToString(),
                        ["cropMs"] = cropWatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                        ["modelMs"] = modelWatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                        ["convertMs"] = convertWatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                        ["roiTotalMs"] = totalWatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    }
                    .Concat(execution.Metrics)
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase)
                };
            }

            if (execution.Payload is UnetSegmentationPayload unet)
            {
                convertWatch.Stop();
                totalWatch.Stop();
                return CreateUnetRoiResult(
                    roi,
                    execution,
                    unet,
                    cropWatch.Elapsed.TotalMilliseconds,
                    modelWatch.Elapsed.TotalMilliseconds,
                    convertWatch.Elapsed.TotalMilliseconds,
                    totalWatch.Elapsed.TotalMilliseconds);
            }

            var detections = ExtractDetections(execution.Payload);
            TryBuildSegmentationMask(execution.Payload, out var segmentationMask, out var segmentationMaskWidth, out var segmentationMaskHeight);
            convertWatch.Stop();
            totalWatch.Stop();

            var hasNg = detections.Count > 0;
            return new InspectionRoiResult
            {
                RoiId = roi.Id,
                RoiName = roi.Name,
                ModelId = roi.ModelId,
                Decision = hasNg ? InspectionCycleDecision.Ng : InspectionCycleDecision.Ok,
                Score = detections.Count == 0 ? null : detections.Max(item => item.Score),
                Findings = detections
                    .Select(det => new InspectionFinding
                    {
                        Code = det.ClassName ?? $"class-{det.ClassId}",
                        Message = $"{det.ClassName ?? det.ClassId.ToString(System.Globalization.CultureInfo.InvariantCulture)} {det.Score:0.###}",
                        Severity = InspectionCycleDecision.Ng
                    })
                    .ToArray(),
                Metrics = new Dictionary<string, string>
                {
                    ["detections"] = detections.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["device"] = execution.DeviceLabel ?? string.Empty,
                    ["taskKind"] = execution.TaskKind.ToString(),
                    ["cropMs"] = cropWatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                    ["modelMs"] = modelWatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                    ["convertMs"] = convertWatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
                    ["roiTotalMs"] = totalWatch.Elapsed.TotalMilliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                }
                .Concat(execution.Metrics)
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
                SegmentationMask = segmentationMask,
                SegmentationMaskWidth = segmentationMaskWidth,
                SegmentationMaskHeight = segmentationMaskHeight
            };
        }
        catch (Exception ex)
        {
            CameraDiagnostics.Error("inspection-action", $"ROI '{roi.Name}' inference failed.", ex);
            return CreateFailureResult(roi, "inference-error", ex.Message);
        }
    }

    private static bool MatchesLocatorClass(YoloObbDetection detection, CameraAlignmentDefinition config)
    {
        if (config.LocatorClassId >= 0 && detection.ClassId == config.LocatorClassId)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(config.LocatorClassName) &&
               string.Equals(detection.ClassName, config.LocatorClassName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadLocatorMinScore(IReadOnlyDictionary<string, string> metrics, out float minScore)
    {
        minScore = 0f;
        return metrics.TryGetValue("locator.minScore", out var raw) &&
               float.TryParse(
                   raw,
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out minScore) &&
               minScore > 0f &&
               minScore < 1f;
    }

    private static CameraAlignmentRuntimeResult BuildAlignedRois(
        Mat image,
        IReadOnlyList<RoiDefinition> rois,
        CameraAlignmentDefinition config,
        YoloObbDetection detection,
        Dictionary<string, string> metadata)
    {
        var imageWidth = Math.Max(1, image.Width);
        var imageHeight = Math.Max(1, image.Height);
        var referenceCenter = new Point2d(config.CenterX * imageWidth, config.CenterY * imageHeight);
        var detectedCenter = new Point2d(detection.CenterX, detection.CenterY);
        var delta = detectedCenter - referenceCenter;
        var detectedAngle = ResolveDetectedLocatorAngle(detection, config);
        var detectedSize = ResolveDetectedLocatorSize(detection, detectedAngle);
        var deltaAngle = NormalizeAngle(detectedAngle - config.AngleDeg);
        var radians = deltaAngle * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        var aligned = rois
            .Select(roi =>
            {
                var roiCenter = new Point2d(roi.CenterX * imageWidth, roi.CenterY * imageHeight);
                var relative = roiCenter - referenceCenter;
                var rotated = new Point2d(
                    (relative.X * cos) - (relative.Y * sin),
                    (relative.X * sin) + (relative.Y * cos));
                var transformed = referenceCenter + delta + rotated;
                return roi with
                {
                    CenterX = transformed.X / imageWidth,
                    CenterY = transformed.Y / imageHeight,
                    AngleDeg = NormalizeAngle(roi.AngleDeg + deltaAngle)
                };
            })
            .ToArray();

        metadata["alignment.dxPx"] = delta.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.dyPx"] = delta.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.angleDeg"] = deltaAngle.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.referenceCenterX"] = referenceCenter.X.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.referenceCenterY"] = referenceCenter.Y.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.referenceWidth"] = (config.Width * imageWidth).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.referenceHeight"] = (config.Height * imageHeight).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.referenceAngleDeg"] = config.AngleDeg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.detectedRawAngleDeg"] = detection.AngleDeg.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.detectedResolvedAngleDeg"] = detectedAngle.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.detectedCenterX"] = detection.CenterX.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.detectedCenterY"] = detection.CenterY.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.detectedWidth"] = detection.Width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.detectedHeight"] = detection.Height.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.detectedResolvedWidth"] = detectedSize.Width.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        metadata["alignment.detectedResolvedHeight"] = detectedSize.Height.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        return CameraAlignmentRuntimeResult.Aligned(aligned, detection.Score, delta.X, delta.Y, deltaAngle, metadata);
    }

    private static (double Width, double Height) ResolveDetectedLocatorSize(
        YoloObbDetection detection,
        double resolvedAngle)
    {
        var angleShift = Math.Abs(NormalizeAngle(resolvedAngle - detection.AngleDeg));
        return Math.Abs(angleShift - 90) < 0.001
            ? (detection.Height, detection.Width)
            : (detection.Width, detection.Height);
    }

    private static double ResolveDetectedLocatorAngle(YoloObbDetection detection, CameraAlignmentDefinition config)
    {
        var candidates = new List<double>
        {
            detection.AngleDeg,
            detection.AngleDeg - 180,
            detection.AngleDeg + 180
        };

        if (HasDifferentAspectDirection(detection, config))
        {
            candidates.Add(detection.AngleDeg - 90);
            candidates.Add(detection.AngleDeg + 90);
            candidates.Add(detection.AngleDeg - 270);
            candidates.Add(detection.AngleDeg + 270);
        }

        return candidates
            .Select(NormalizeAngle)
            .OrderBy(angle => Math.Abs(NormalizeAngle(angle - config.AngleDeg)))
            .First();
    }

    private static bool HasDifferentAspectDirection(YoloObbDetection detection, CameraAlignmentDefinition config)
    {
        if (config.Width <= 0 || config.Height <= 0 || detection.Width <= 0 || detection.Height <= 0)
        {
            return false;
        }

        const double epsilon = 0.001;
        var referenceWide = config.Width >= config.Height;
        var detectionWide = detection.Width >= detection.Height;
        var referenceNearlySquare = Math.Abs(config.Width - config.Height) <= epsilon;
        var detectionNearlySquare = Math.Abs(detection.Width - detection.Height) <= epsilon;
        return !referenceNearlySquare && !detectionNearlySquare && referenceWide != detectionWide;
    }

    private static double NormalizeAngle(double angle)
    {
        while (angle <= -180)
        {
            angle += 360;
        }

        while (angle > 180)
        {
            angle -= 360;
        }

        return angle;
    }

    private static IReadOnlyList<DetectionEntity> ExtractDetections(VisionTaskPayload payload)
    {
        return payload switch
        {
            DetectionPayload detection => detection.Detections,
            SegmentationPayload segmentation => segmentation.Detections,
            UnetSegmentationPayload unet => unet.Detections,
            SequenceBandsPayload sequence => sequence.Detections,
            ObbDetectionPayload obb => obb.Detections
                .Select(det => new DetectionEntity
                {
                    ClassId = det.ClassId,
                    ClassName = det.ClassName,
                    Score = det.Score,
                    X1 = det.X1,
                    Y1 = det.Y1,
                    X2 = det.X2,
                    Y2 = det.Y2
                })
                .ToArray(),
            _ => Array.Empty<DetectionEntity>()
        };
    }

    private static InspectionRoiResult CreateUnetRoiResult(
        RoiDefinition roi,
        InspectionModelExecutionResult execution,
        UnetSegmentationPayload payload,
        double cropMs,
        double modelMs,
        double convertMs,
        double roiTotalMs)
    {
        var result = payload.Result;
        var hasNg = result.HasDefect;
        var findings = result.Components
            .Select(component => new InspectionFinding
            {
                Code = "scratch",
                Message = $"scratch #{component.Index}: area={component.AreaPx}, perimeter={component.PerimeterPx:0.#}, ratio={component.AreaPerimeterRatio:0.###}, maxProb={component.MaxProbability:0.###}",
                Severity = InspectionCycleDecision.Ng
            })
            .ToArray();

        var metrics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["detections"] = result.AcceptedComponentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["defect.rawComponentCount"] = result.RawComponentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["defect.componentCount"] = result.AcceptedComponentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["defect.maxAreaPx"] = result.MaxAreaPx.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            ["defect.maxPerimeterPx"] = result.MaxPerimeterPx.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
            ["defect.maxAreaPerimeterRatio"] = result.MaxAreaPerimeterRatio.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            ["defect.summaryText"] = result.SummaryText,
            ["device"] = execution.DeviceLabel ?? string.Empty,
            ["taskKind"] = execution.TaskKind.ToString(),
            ["cropMs"] = cropMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            ["modelMs"] = modelMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            ["convertMs"] = convertMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            ["roiTotalMs"] = roiTotalMs.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(result.ComponentsText))
        {
            metrics["defect.componentsText"] = result.ComponentsText;
        }

        foreach (var item in execution.Metrics)
        {
            metrics[item.Key] = item.Value;
        }

        return new InspectionRoiResult
        {
            RoiId = roi.Id,
            RoiName = roi.Name,
            ModelId = roi.ModelId,
            Decision = hasNg ? InspectionCycleDecision.Ng : InspectionCycleDecision.Ok,
            Score = hasNg ? result.MaxProbability : null,
            Findings = findings,
            Metrics = metrics,
            DefectComponentCount = result.AcceptedComponentCount,
            DefectMaxAreaPx = result.MaxAreaPx,
            DefectMaxPerimeterPx = result.MaxPerimeterPx,
            DefectMaxAreaPerimeterRatio = result.MaxAreaPerimeterRatio,
            DefectSummaryText = result.SummaryText,
            DefectComponentsText = result.ComponentsText,
            SegmentationMask = result.Mask,
            SegmentationMaskWidth = result.MaskWidth,
            SegmentationMaskHeight = result.MaskHeight
        };
    }

    private static bool TryBuildSegmentationMask(
        VisionTaskPayload payload,
        out byte[]? mask,
        out int maskWidth,
        out int maskHeight)
    {
        mask = null;
        maskWidth = 0;
        maskHeight = 0;
        if (payload is not SegmentationPayload segmentation || segmentation.Segmentations.Count == 0)
        {
            return false;
        }

        var first = segmentation.Segmentations.FirstOrDefault(item =>
            item.Mask.Length > 0 &&
            item.MaskWidth > 0 &&
            item.MaskHeight > 0);
        if (first == null)
        {
            return false;
        }

        maskWidth = first.MaskWidth;
        maskHeight = first.MaskHeight;
        mask = new byte[checked(maskWidth * maskHeight)];
        foreach (var result in segmentation.Segmentations)
        {
            if (result.Mask.Length != mask.Length ||
                result.MaskWidth != maskWidth ||
                result.MaskHeight != maskHeight)
            {
                continue;
            }

            for (var i = 0; i < mask.Length; i++)
            {
                if (result.Mask[i] != 0)
                {
                    mask[i] = 255;
                }
            }
        }

        return mask.Any(value => value != 0);
    }

    private static InspectionRoiResult CreateFailureResult(RoiDefinition roi, string code, string message)
    {
        return new InspectionRoiResult
        {
            RoiId = roi.Id,
            RoiName = roi.Name,
            ModelId = roi.ModelId,
            Decision = InspectionCycleDecision.Ng,
            Findings =
            [
                new InspectionFinding
                {
                    Code = code,
                    Message = message,
                    Severity = InspectionCycleDecision.Ng
                }
            ]
        };
    }

    private static Mat ExtractHorizontalRoi(Mat image, RoiDefinition roi)
    {
        var centerX = roi.CenterX * image.Width;
        var centerY = roi.CenterY * image.Height;
        var width = Math.Max(1, (int)Math.Round(roi.Width * image.Width));
        var height = Math.Max(1, (int)Math.Round(roi.Height * image.Height));

        if (Math.Abs(roi.AngleDeg) < 0.001)
        {
            var left = (int)Math.Round(centerX - (width / 2.0));
            var top = (int)Math.Round(centerY - (height / 2.0));
            if (left >= 0 && top >= 0 && left + width <= image.Width && top + height <= image.Height)
            {
                using var view = new Mat(image, new OpenCvSharp.Rect(left, top, width, height));
                return view.Clone();
            }

            var patch = new Mat();
            Cv2.GetRectSubPix(image, new OpenCvSharp.Size(width, height), new Point2f((float)centerX, (float)centerY), patch);
            return patch;
        }

        var angleRad = roi.AngleDeg * Math.PI / 180.0;
        var cos = Math.Cos(angleRad);
        var sin = Math.Sin(angleRad);
        var halfWidth = width / 2.0;
        var halfHeight = height / 2.0;
        var axisX = new Point2d(cos, sin);
        var axisY = new Point2d(-sin, cos);
        var center = new Point2d(centerX, centerY);

        var source = new[]
        {
            ToPoint2f(center - (axisX * halfWidth) - (axisY * halfHeight)),
            ToPoint2f(center + (axisX * halfWidth) - (axisY * halfHeight)),
            ToPoint2f(center - (axisX * halfWidth) + (axisY * halfHeight))
        };
        var destination = new[]
        {
            new Point2f(0, 0),
            new Point2f(width - 1, 0),
            new Point2f(0, height - 1)
        };

        using var affine = Cv2.GetAffineTransform(source, destination);
        var output = new Mat();
        Cv2.WarpAffine(
            image,
            output,
            affine,
            new OpenCvSharp.Size(width, height),
            InterpolationFlags.Linear,
            BorderTypes.Replicate);
        return output;
    }

    private static Point2f ToPoint2f(Point2d point)
    {
        return new Point2f((float)point.X, (float)point.Y);
    }

    private sealed record CameraAlignmentRuntimeResult(
        bool Success,
        IReadOnlyList<RoiDefinition> Rois,
        string Message,
        double Score,
        double Dx,
        double Dy,
        double AngleDeg,
        IReadOnlyDictionary<string, string> Metadata)
    {
        public static CameraAlignmentRuntimeResult NotConfigured(IReadOnlyList<RoiDefinition> rois) =>
            new(true, rois, string.Empty, 0, 0, 0, 0, new Dictionary<string, string>());

        public static CameraAlignmentRuntimeResult Failed(
            IReadOnlyList<RoiDefinition> rois,
            string message,
            IReadOnlyDictionary<string, string> metadata) =>
            new(false, rois, message, 0, 0, 0, 0, metadata);

        public static CameraAlignmentRuntimeResult Aligned(
            IReadOnlyList<RoiDefinition> rois,
            double score,
            double dx,
            double dy,
            double angleDeg,
            IReadOnlyDictionary<string, string> metadata) =>
            new(true, rois, string.Empty, score, dx, dy, angleDeg, metadata);
    }
}
