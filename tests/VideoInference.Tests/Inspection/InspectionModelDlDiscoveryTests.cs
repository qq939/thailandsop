using VideoInferenceDemo.ImageInspection;
using VideoInferenceDemo.ImageInspection.Runtime;
using VideoInferenceDemo.ImageInspection.Tasks;

namespace VideoInferenceDemo.Tests.Inspection;

public sealed class InspectionModelDlDiscoveryTests
{
    [Fact]
    public void Load_CreatesModelsFromDlWhenConfigIsMissing()
    {
        var root = CreateTempRoot();
        try
        {
            WriteModelBundle(root, "model-a", "Model A", "detection", "a.onnx", ["ok", "ng"], 640, 480);

            var settings = InspectionModelSettingsStorage.Load(Path.Combine(root, "inspection_model_config.json"), root);

            var model = Assert.Single(settings.Models);
            Assert.Equal("model-a", model.Id);
            Assert.Equal("Model A", model.Name);
            Assert.Equal(Path.Combine(root, "DL", "model-a", "a.onnx"), model.ModelPath);
            Assert.Equal(["ok", "ng"], model.Classes);
            Assert.Equal(640, model.InputWidth);
            Assert.Equal(480, model.InputHeight);
            Assert.Equal(YoloOutputLayout.ChannelsFirst, model.Yolo.OutputLayout);
            Assert.Equal(YoloScoreMode.ClassOnly, model.Yolo.ScoreMode);
            Assert.Equal(2, model.Yolo.ClassCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_MergesDiscoveredModelsAndPreservesEditablePreferences()
    {
        var root = CreateTempRoot();
        try
        {
            var configPath = Path.Combine(root, "inspection_model_config.json");
            WriteModelBundle(root, "model-a", "Model A Fresh", "segmentation", "fresh.onnx", ["fresh-a", "fresh-b", "fresh-c"], 800, 600);
            InspectionModelSettingsStorage.Save(
                configPath,
                new InspectionModelSettings
                {
                    Models =
                    [
                        new InspectionModelConfig
                        {
                            Id = "model-a",
                            Name = "Old Model A",
                            ModelPath = @"C:\old\a.onnx",
                            TaskType = ModelTaskType.Detection,
                            DeviceKind = InferenceDeviceKind.Cpu,
                            Enabled = false,
                            SharedRuntime = false,
                            Classes = ["old"]
                        }
                    ]
                });

            var settings = InspectionModelSettingsStorage.Load(configPath, root);

            var model = Assert.Single(settings.Models);
            Assert.Equal("Old Model A", model.Name);
            Assert.Equal(ModelTaskType.Segmentation, model.TaskType);
            Assert.Equal(Path.Combine(root, "DL", "model-a", "fresh.onnx"), model.ModelPath);
            Assert.Equal(["fresh-a", "fresh-b", "fresh-c"], model.Classes);
            Assert.Equal(800, model.InputWidth);
            Assert.Equal(600, model.InputHeight);
            Assert.Equal(InferenceDeviceKind.GpuCuda, model.DeviceKind);
            Assert.False(model.Enabled);
            Assert.False(model.SharedRuntime);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsClassesJsonArrayWhenManifestReferencesClassesFile()
    {
        var root = CreateTempRoot();
        try
        {
            WriteModelBundle(root, "model-a", "Model A", "detection", "a.onnx", [], 0, 0, includeClassesInManifest: false);
            File.WriteAllText(
                Path.Combine(root, "DL", "model-a", "classes.json"),
                """
                ["scratch", "dent"]
                """);

            var settings = InspectionModelSettingsStorage.Load(Path.Combine(root, "inspection_model_config.json"), root);

            var model = Assert.Single(settings.Models);
            Assert.Equal(["scratch", "dent"], model.Classes);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsSequenceMetadataFromDlManifest()
    {
        var root = CreateTempRoot();
        try
        {
            WriteSequenceModelBundle(root);

            var settings = InspectionModelSettingsStorage.Load(Path.Combine(root, "inspection_model_config.json"), root);

            var model = Assert.Single(settings.Models);
            Assert.Equal("sequence-bands", model.Id);
            Assert.Equal(ModelTaskType.SequenceBands, model.TaskType);
            Assert.Equal(["background", "A", "B", "C"], model.Classes);
            Assert.Equal(768, model.InputWidth);
            Assert.Equal(512, model.InputHeight);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsObbLocatorMetadataFromDlManifest()
    {
        var root = CreateTempRoot();
        try
        {
            WriteModelBundle(root, "locator", "Locator", "yolo_obb", "locator.onnx", ["datum"], 640, 640, locatorMinScore: 0.72f);

            var settings = InspectionModelSettingsStorage.Load(Path.Combine(root, "inspection_model_config.json"), root);

            var model = Assert.Single(settings.Models);
            Assert.Equal(ModelTaskType.ObbDetection, model.TaskType);
            Assert.Equal(["datum"], model.Classes);
            Assert.Equal(0.72f, model.Yolo.LocatorMinScore, precision: 3);
            Assert.Contains(model.Parameters, parameter =>
                parameter.Name == "locator.minScore" &&
                parameter.Value == "0.72");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsUnetSegmentationMetadataFromDlManifest()
    {
        var root = CreateTempRoot();
        try
        {
            WriteUnetModelBundle(root);

            var settings = InspectionModelSettingsStorage.Load(Path.Combine(root, "inspection_model_config.json"), root);

            var model = Assert.Single(settings.Models);
            Assert.Equal("unet-scratch-t15-640", model.Id);
            Assert.Equal("U-Net Scratch Segmentation", model.Name);
            Assert.Equal(ModelTaskType.UnetSegmentation, model.TaskType);
            Assert.Equal(Path.Combine(root, "DL", "unet_scratch_t15_640", "model.onnx"), model.ModelPath);
            Assert.Equal(640, model.InputWidth);
            Assert.Equal(640, model.InputHeight);
            Assert.Equal(["scratch"], model.Classes);
            Assert.Equal(["#EF5350"], model.ClassColors);
            Assert.Equal(0.7f, model.Unet.ProbabilityThreshold, precision: 3);
            Assert.Equal(30, model.Unet.MinComponentArea);
            Assert.Equal(5f, model.Unet.MinComponentPerimeter, precision: 3);
            Assert.Equal(0.1f, model.Unet.MinAreaPerimeterRatio, precision: 3);
            Assert.Equal(9f, model.Unet.MaxAreaPerimeterRatio, precision: 3);
            Assert.Contains(model.Parameters, parameter =>
                parameter.Name == "unet.probabilityThreshold" &&
                parameter.Value == "0.7");

            var viewModel = new InspectionModelConfigViewModel(model);
            Assert.True(viewModel.IsUnetSegmentation);
            Assert.Contains(viewModel.MetadataRows, row => row.Label == "Probability Threshold" && row.Value == "0.7");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsPresenceClassificationMetadataFromDlManifest()
    {
        var root = CreateTempRoot();
        try
        {
            WritePresenceClassificationBundle(root, "presence_classification", 0.55f);

            var settings = InspectionModelSettingsStorage.Load(Path.Combine(root, "inspection_model_config.json"), root);

            var model = Assert.Single(settings.Models);
            Assert.Equal("classifier-shufflenet", model.Id);
            Assert.Equal(ModelTaskType.PresenceClassification, model.TaskType);
            Assert.Equal(224, model.InputWidth);
            Assert.Equal(224, model.InputHeight);
            Assert.Equal(["OK", "NG"], model.Classes);
            Assert.Equal("OK", model.Classification.PresentClass);
            Assert.Equal("NG", model.Classification.AbsentClass);
            Assert.Equal(0.55f, model.Classification.ProbabilityThreshold, precision: 3);
            Assert.Contains(model.Parameters, parameter =>
                parameter.Name == "classification.probabilityThreshold" &&
                parameter.Value == "0.55");

            var viewModel = new InspectionModelConfigViewModel(model);
            Assert.Contains(viewModel.MetadataRows, row => row.Label == "Present Class" && row.Value == "OK");
            Assert.Contains(viewModel.MetadataRows, row => row.Label == "Absent Class" && row.Value == "NG");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_TreatsLegacyClassificationManifestAsPresenceClassification()
    {
        var root = CreateTempRoot();
        try
        {
            WritePresenceClassificationBundle(root, "classification", 0);

            var settings = InspectionModelSettingsStorage.Load(Path.Combine(root, "inspection_model_config.json"), root);

            var model = Assert.Single(settings.Models);
            Assert.Equal(ModelTaskType.PresenceClassification, model.TaskType);
            Assert.Equal(0.5f, model.Classification.ProbabilityThreshold, precision: 3);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Load_ReadsOcrPipelineMetadataFromDlManifest()
    {
        var root = CreateTempRoot();
        try
        {
            WriteOcrPipelineBundle(root, includeRec: true);

            var settings = InspectionModelSettingsStorage.Load(Path.Combine(root, "inspection_model_config.json"), root);

            var model = Assert.Single(settings.Models);
            var directory = Path.Combine(root, "DL", "ppocr_en_pipeline");
            Assert.Equal("ppocr-en-pipeline", model.Id);
            Assert.Equal(ModelTaskType.OcrPipeline, model.TaskType);
            Assert.Equal(VisionRuntimeKind.OcrRuntime, model.Runtime);
            Assert.Equal(Path.Combine(directory, "det.onnx"), model.ModelPath);
            Assert.Equal(Path.Combine(directory, "det.onnx"), model.Ocr.DetPath);
            Assert.Equal(Path.Combine(directory, "cls.onnx"), model.Ocr.ClsPath);
            Assert.Equal(Path.Combine(directory, "rec.onnx"), model.Ocr.RecPath);
            Assert.Equal(Path.Combine(directory, "en_dict.txt"), model.Ocr.DictPath);
            Assert.False(model.Ocr.DoAngle);
            Assert.True(model.Ocr.ReturnWordBox);

            var viewModel = new InspectionModelConfigViewModel(model);
            Assert.Contains(viewModel.MetadataRows, row => row.Label == "OCR Det" && row.Value == "det.onnx");
            Assert.Contains(viewModel.MetadataRows, row => row.Label == "OCR ReturnWordBox" && row.Value == "true");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Probe_OcrPipelineMissingAuxFileFailsWithFileName()
    {
        var root = CreateTempRoot();
        try
        {
            WriteOcrPipelineBundle(root, includeRec: false);
            var settings = InspectionModelSettingsStorage.Load(Path.Combine(root, "inspection_model_config.json"), root);
            var model = Assert.Single(settings.Models);
            var registry = new InspectionModelRuntimeRegistry(Path.Combine(root, "inspection_model_config.json"));

            var result = registry.Probe(model);

            Assert.False(result.Success);
            Assert.Contains("rec.onnx", result.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public void InspectionModelConfigViewModel_DoesNotExposeBrowseOnnxCommand()
    {
        var type = typeof(InspectionModelConfigViewModel);

        Assert.Null(type.GetProperty("BrowseOnnxCommand"));
        Assert.Null(type.GetMethod("BrowseOnnx", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic));
    }

    [Fact]
    public void InspectionModelConfigViewModel_BuildOnlyAppliesEditableFields()
    {
        var model = new InspectionModelConfig
        {
            Id = "model-a",
            Name = "Model A",
            ModelPath = @"C:\models\a.onnx",
            TaskType = ModelTaskType.Detection,
            DeviceKind = InferenceDeviceKind.Cpu,
            Enabled = true,
            InputWidth = 640,
            InputHeight = 480,
            Classes = ["ok", "ng"],
            Yolo = new InspectionYoloMetadataConfig
            {
                OutputLayout = YoloOutputLayout.ChannelsFirst,
                ScoreMode = YoloScoreMode.ClassOnly,
                ClassCount = 2,
                TensorRtCacheKey = "model-a"
            },
            Parameters =
            [
                new InspectionModelParameter { Name = "outputLayout", Value = "ChannelsFirst" }
            ]
        };

        var viewModel = new InspectionModelConfigViewModel(model)
        {
            Name = "Renamed",
            Enabled = false,
            InputWidth = 123,
            ClassesText = "changed",
            DeviceKind = InferenceDeviceKind.GpuRt
        };

        var built = viewModel.Build();

        Assert.Equal("Renamed", built.Name);
        Assert.False(built.Enabled);
        Assert.Equal(InferenceDeviceKind.GpuCuda, built.DeviceKind);
        Assert.Equal(640, built.InputWidth);
        Assert.Equal(480, built.InputHeight);
        Assert.Equal(["ok", "ng"], built.Classes);
        Assert.Equal(YoloOutputLayout.ChannelsFirst, built.Yolo.OutputLayout);
        Assert.Contains(viewModel.MetadataRows, row => row.Label == "YOLO OutputLayout" && row.Value == "ChannelsFirst");
        Assert.Equal(2, viewModel.ClassRows.Count);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteModelBundle(
        string root,
        string id,
        string displayName,
        string taskType,
        string modelFile,
        IReadOnlyList<string> classes,
        int inputWidth,
        int inputHeight,
        bool includeClassesInManifest = true,
        float locatorMinScore = 0)
    {
        var directory = Path.Combine(root, "DL", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, modelFile), string.Empty);
        var classesJson = includeClassesInManifest
            ? $"""
              "classes": [{string.Join(", ", classes.Select(item => $"\"{item}\""))}],
              """
            : """
              "classesFile": "classes.json",
              """;
        File.WriteAllText(
            Path.Combine(directory, "model.json"),
            $$"""
            {
              "id": "{{id}}",
              "displayName": "{{displayName}}",
              "taskType": "{{taskType}}",
              "modelFile": "{{modelFile}}",
              {{classesJson}}
              "inputWidth": {{inputWidth}},
              "inputHeight": {{inputHeight}},
              "yolo": {
                "outputLayout": "channels_first",
                "scoreMode": "class_only",
                "classCount": {{classes.Count}},
                "detectionOutputName": "output0",
                "prototypeOutputName": "output1",
                "maskThreshold": 0.6,
                "locatorMinScore": {{locatorMinScore.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
              }
            }
            """);
    }

    private static void WriteSequenceModelBundle(string root)
    {
        var directory = Path.Combine(root, "DL", "sequence_bands");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "sequence_model.onnx"), string.Empty);
        File.WriteAllText(
            Path.Combine(directory, "model.json"),
            """
            {
              "id": "sequence-bands",
              "displayName": "Sequence Bands",
              "taskType": "sequence_bands",
              "modelFile": "sequence_model.onnx",
              "inputWidth": 768,
              "inputHeight": 512,
              "sequence": {
                "input_name": "input",
                "output_name": "logits",
                "input_shape": [1, 3, 512, 768],
                "output_shape": [1, 4, 128],
                "class_names": ["background", "A", "B", "C"],
                "preprocess": {
                  "resize_height": 512,
                  "resize_width": 768
                }
              }
            }
            """);
    }

    private static void WriteUnetModelBundle(string root)
    {
        var directory = Path.Combine(root, "DL", "unet_scratch_t15_640");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "model.onnx"), string.Empty);
        File.WriteAllText(
            Path.Combine(directory, "model.json"),
            """
            {
              "id": "unet-scratch-t15-640",
              "displayName": "U-Net Scratch Segmentation",
              "description": "U-Net resnet34 binary scratch segmentation model.",
              "taskType": "unet_segmentation",
              "modelFile": "model.onnx",
              "inputWidth": 640,
              "inputHeight": 640,
              "classes": ["scratch"],
              "boxColors": ["#EF5350"],
              "unet": {
                "probabilityThreshold": 0.7,
                "minComponentArea": 30,
                "minComponentPerimeter": 5,
                "minAreaPerimeterRatio": 0.1,
                "maxAreaPerimeterRatio": 9
              }
            }
            """);
    }

    private static void WritePresenceClassificationBundle(string root, string taskType, float threshold)
    {
        var directory = Path.Combine(root, "DL", "classifier_shufflenet");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "model.onnx"), string.Empty);
        var classificationJson = threshold > 0
            ? $$"""
                "classification": {
                  "presentClass": "OK",
                  "absentClass": "NG",
                  "probabilityThreshold": {{threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}}
                },
              """
            : string.Empty;
        File.WriteAllText(
            Path.Combine(directory, "model.json"),
            $$"""
            {
              "id": "classifier-shufflenet",
              "displayName": "Product Presence Classifier (ShuffleNet)",
              "description": "ShuffleNetV2_x0.5 product presence classifier.",
              "taskType": "{{taskType}}",
              "modelFile": "model.onnx",
              "inputWidth": 224,
              "inputHeight": 224,
              "classes": ["OK", "NG"],
              "boxColors": ["#4CAF50", "#EF5350"],
              {{classificationJson}}
              "unused": true
            }
            """);
    }

    private static void WriteOcrPipelineBundle(string root, bool includeRec)
    {
        var directory = Path.Combine(root, "DL", "ppocr_en_pipeline");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "det.onnx"), string.Empty);
        File.WriteAllText(Path.Combine(directory, "cls.onnx"), string.Empty);
        if (includeRec)
        {
            File.WriteAllText(Path.Combine(directory, "rec.onnx"), string.Empty);
        }

        File.WriteAllText(Path.Combine(directory, "en_dict.txt"), "A");
        File.WriteAllText(
            Path.Combine(directory, "model.json"),
            """
            {
              "id": "ppocr-en-pipeline",
              "displayName": "英文 OCR 管线",
              "taskType": "ocr_pipeline",
              "detFile": "det.onnx",
              "clsFile": "cls.onnx",
              "recFile": "rec.onnx",
              "dictFile": "en_dict.txt",
              "ocr": {
                "doAngle": false,
                "returnWordBox": true
              }
            }
            """);
    }
}
