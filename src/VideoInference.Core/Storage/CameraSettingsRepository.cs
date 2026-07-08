namespace VideoInferenceDemo;

public sealed class CameraSettingsRepository
{
    private const string SelectedCameraKey = "selected_camera_id";

    public CameraSettingsRepository(string dbPath)
    {
    }

    public bool HasConfiguration()
    {
        return DbSession.ConfigDb.Queryable<CameraProfileEntity>().Any();
    }

    public CameraSettings Load()
    {
        var cameras = LoadCameras();
        var sopProfiles = LoadSopProfiles();
        var normalizedSopProfiles = sopProfiles
            .Where(profile => profile != null)
            .Select(NormalizeSopProfile)
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id) && profile.Steps.Count > 0)
            .ToList();
        if (SopProfilesChanged(sopProfiles, normalizedSopProfiles))
        {
            DbSession.ConfigDb.Ado.UseTran(() => SaveSopProfileEntities(normalizedSopProfiles));
        }

        var selectedCameraId = LoadState(SelectedCameraKey);

        if (cameras.Count == 0)
            return CameraSettings.CreateDefault();

        return new CameraSettings
        {
            Cameras = cameras,
            SopProfiles = normalizedSopProfiles,
            SelectedCameraId = selectedCameraId
        };
    }

    public void Save(CameraSettings settings)
    {
        var normalized = Normalize(settings);
        DbSession.ConfigDb.Ado.UseTran(() =>
        {
            SaveCameras(normalized.Cameras);
            SaveSopProfileEntities(normalized.SopProfiles);
            SaveState(SelectedCameraKey, normalized.SelectedCameraId);
        });
    }

    public void SaveSopProfiles(IReadOnlyList<CameraSopProfile> profiles)
    {
        var normalized = Normalize(new CameraSettings
        {
            Cameras = new List<CameraProfile> { CameraProfile.CreateDefault(1) },
            SopProfiles = (profiles ?? Array.Empty<CameraSopProfile>()).ToList()
        }).SopProfiles;

        DbSession.ConfigDb.Ado.UseTran(() => SaveSopProfileEntities(normalized));
    }

    public void ImportFromFileIfEmpty(string cameraConfigPath)
    {
        if (HasConfiguration() || string.IsNullOrWhiteSpace(cameraConfigPath) || !File.Exists(cameraConfigPath))
            return;

        Save(CameraSettingsStorage.Load(cameraConfigPath));
    }

    private static CameraSettings Normalize(CameraSettings? settings)
    {
        settings ??= new CameraSettings();
        var cameras = (settings.Cameras ?? new List<CameraProfile>())
            .Select((camera, index) => (camera ?? CameraProfile.CreateDefault(index + 1)).Normalize(index + 1))
            .ToList();
        if (cameras.Count == 0)
            cameras.Add(CameraProfile.CreateDefault(1));

        var sopProfiles = (settings.SopProfiles ?? new List<CameraSopProfile>())
            .Where(profile => profile != null)
            .Select(NormalizeSopProfile)
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Id) && profile.Steps.Count > 0)
            .ToList();

        var selectedId = settings.SelectedCameraId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selectedId) ||
            cameras.All(camera => !string.Equals(camera.Id, selectedId, StringComparison.OrdinalIgnoreCase)))
            selectedId = cameras[0].Id;

        return new CameraSettings
        {
            Cameras = cameras,
            SopProfiles = sopProfiles,
            SelectedCameraId = selectedId
        };
    }

    private static CameraSopProfile NormalizeSopProfile(CameraSopProfile profile)
    {
        var id = string.IsNullOrWhiteSpace(profile.Id) ? Guid.NewGuid().ToString("N") : profile.Id.Trim();
        var name = string.IsNullOrWhiteSpace(profile.Name) ? id : profile.Name.Trim();
        var isSop1 = IsSop1Profile(name);
        var isSop2 = IsSop2Profile(name);
        var strategy = ResolveSopStrategy(name, profile.Strategy);
        var stepLimit = isSop1
            ? 5
            : isSop2
                ? 6
                : int.MaxValue;
        var steps = (profile.Steps ?? new List<CameraSopStep>())
            .Where(step => step != null)
            .OrderBy(step => step.Step)
            .Take(stepLimit)
            .Select((step, index) => new CameraSopStep
            {
                Step = index + 1,
                Name = step.Name?.Trim() ?? string.Empty,
                ActionCode = string.IsNullOrWhiteSpace(step.ActionCode) ? null : step.ActionCode.Trim(),
                TcnLabel = string.IsNullOrWhiteSpace(step.TcnLabel) ? null : step.TcnLabel.Trim(),
                ExpectedStateCode = string.IsNullOrWhiteSpace(step.ExpectedStateCode) ? null : step.ExpectedStateCode.Trim()
            })
            .Where(step => !string.IsNullOrWhiteSpace(step.Name))
            .ToList();

        return new CameraSopProfile
        {
            Id = id,
            Name = name,
            Strategy = strategy,
            FingerprintModuleId = string.IsNullOrWhiteSpace(profile.FingerprintModuleId) ? string.Empty : profile.FingerprintModuleId.Trim(),
            Steps = steps
        };
    }

    private static bool IsSop2Profile(string name)
    {
        return string.Equals(name?.Trim(), "sop2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSop1Profile(string name)
    {
        return string.Equals(name?.Trim(), "sop1", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSopStrategy(string name, string? configuredStrategy)
    {
        if (IsSop1Profile(name))
        {
            return AnalysisStrategyNames.Sop1;
        }

        if (IsSop2Profile(name))
        {
            return AnalysisStrategyNames.Sop2;
        }

        return string.IsNullOrWhiteSpace(configuredStrategy)
            ? AnalysisStrategyNames.SopRules
            : configuredStrategy.Trim();
    }

    private static bool SopProfilesChanged(
        IReadOnlyList<CameraSopProfile> current,
        IReadOnlyList<CameraSopProfile> normalized)
    {
        if (current.Count != normalized.Count)
        {
            return true;
        }

        for (var i = 0; i < current.Count; i++)
        {
            var left = current[i];
            var right = normalized[i];
            if (!string.Equals(left.Id, right.Id, StringComparison.Ordinal) ||
                !string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
                !string.Equals(left.Strategy, right.Strategy, StringComparison.Ordinal) ||
                !string.Equals(left.FingerprintModuleId, right.FingerprintModuleId, StringComparison.Ordinal) ||
                left.Steps.Count != right.Steps.Count)
            {
                return true;
            }

            for (var j = 0; j < left.Steps.Count; j++)
            {
                if (left.Steps[j].Step != right.Steps[j].Step ||
                    !string.Equals(left.Steps[j].Name, right.Steps[j].Name, StringComparison.Ordinal) ||
                    !string.Equals(left.Steps[j].ActionCode, right.Steps[j].ActionCode, StringComparison.Ordinal) ||
                    !string.Equals(left.Steps[j].TcnLabel, right.Steps[j].TcnLabel, StringComparison.Ordinal) ||
                    !string.Equals(left.Steps[j].ExpectedStateCode, right.Steps[j].ExpectedStateCode, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private List<CameraProfile> LoadCameras()
    {
        return DbSession.ConfigDb.Queryable<CameraProfileEntity>()
            .OrderBy("sort_order, name")
            .ToList()
            .Select(e => new CameraProfile
            {
                Id = e.Id,
                Name = e.Name,
                Enabled = e.Enabled != 0,
                AutoStart = e.AutoStart != 0,
                ProviderId = e.ProviderId,
                CameraIndex = e.CameraIndex,
                DeviceId = e.DeviceId,
                OpenCvSource = e.OpenCvSource,
                OpenCvBackend = e.OpenCvBackend,
                TriggerMode = Enum.TryParse<CameraTriggerMode>(e.TriggerMode, out var triggerMode) ? triggerMode : CameraTriggerMode.Software,
                Rotation = Enum.TryParse<CameraRotation>(e.Rotation, out var rotation) ? rotation : CameraRotation.None,
                MirrorMode = Enum.TryParse<CameraMirrorMode>(e.MirrorMode, out var mirrorMode) ? mirrorMode : CameraMirrorMode.None,
                TargetFps = e.TargetFps,
                UseSourcePtsForVideo = e.UseSourcePtsForVideo != 0,
                PrimaryTaskId = e.PrimaryTaskId,
                SelectedSopProfileId = e.SelectedSopProfileId,
                EnableSopAnalysis = e.EnableSopAnalysis != 0,
                AnalysisFrameWindowSize = e.AnalysisFrameWindowSize,
                AnalysisStateWindowSize = e.AnalysisStateWindowSize,
                AnalysisHoldFrames = e.AnalysisHoldFrames,
                SopWindowMs = e.SopWindowMs,
                SopMinScoreQ1000 = e.SopMinScoreQ1000,
                SopMinVisibleRatioQ1000 = e.SopMinVisibleRatioQ1000,
                OcrEnabled = e.OcrEnabled != 0,
                OcrRoiX = e.OcrRoiX,
                OcrRoiY = e.OcrRoiY,
                OcrRoiWidth = e.OcrRoiWidth,
                OcrRoiHeight = e.OcrRoiHeight,
                EnableCameraRecording = e.EnableCameraRecording != 0,
                RecordingRootDirectory = e.RecordingRootDirectory,
                RecordingSegmentMinutes = e.RecordingSegmentMinutes,
                RecordingContainerFormat = e.RecordingContainerFormat,
                RecordingVideoEncoder = e.RecordingVideoEncoder,
                RecordingCodecFourcc = e.RecordingCodecFourcc,
                RecordingQueueCapacity = e.RecordingQueueCapacity,
                RecordingBitrateMbps = e.RecordingBitrateMbps,
                RecordingFps = e.RecordingFps
            }.Normalize(0))
            .ToList();
    }

    private List<CameraSopProfile> LoadSopProfiles()
    {
        var profiles = DbSession.ConfigDb.Queryable<SopProfileEntity>()
            .OrderBy("sort_order, name")
            .ToList()
            .Select(e => new CameraSopProfile
            {
                Id = e.Id,
                Name = e.Name,
                Strategy = e.Strategy,
                FingerprintModuleId = e.FingerprintModuleId
            })
            .ToList();

        foreach (var profile in profiles)
            profile.Steps = LoadSopSteps(profile.Id);

        return profiles;
    }

    private List<CameraSopStep> LoadSopSteps(string profileId)
    {
        return DbSession.ConfigDb.Queryable<SopStepEntity>()
            .Where(e => e.ProfileId == profileId)
            .OrderBy("step")
            .ToList()
            .Select(e => new CameraSopStep
            {
                Step = e.Step,
                Name = e.Name,
                ActionCode = e.ActionCode,
                TcnLabel = e.TcnLabel,
                ExpectedStateCode = e.ExpectedStateCode
            })
            .ToList();
    }

    private void SaveCameras(IReadOnlyList<CameraProfile> cameras)
    {
        DbSession.ConfigDb.Deleteable<CameraProfileEntity>().ExecuteCommand();
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var i = 0; i < cameras.Count; i++)
        {
            var camera = cameras[i].Normalize(i + 1);
            DbSession.ConfigDb.Insertable(new CameraProfileEntity
            {
                Id = camera.Id,
                Name = camera.Name,
                Enabled = camera.Enabled ? 1 : 0,
                AutoStart = camera.AutoStart ? 1 : 0,
                ProviderId = camera.ProviderId,
                CameraIndex = camera.CameraIndex,
                DeviceId = camera.DeviceId,
                OpenCvSource = camera.OpenCvSource,
                OpenCvBackend = camera.OpenCvBackend,
                TriggerMode = camera.TriggerMode.ToString(),
                Rotation = camera.Rotation.ToString(),
                MirrorMode = camera.MirrorMode.ToString(),
                TargetFps = camera.TargetFps,
                UseSourcePtsForVideo = camera.UseSourcePtsForVideo ? 1 : 0,
                PrimaryTaskId = camera.PrimaryTaskId,
                SelectedSopProfileId = camera.SelectedSopProfileId,
                EnableSopAnalysis = camera.EnableSopAnalysis ? 1 : 0,
                AnalysisFrameWindowSize = camera.AnalysisFrameWindowSize,
                AnalysisStateWindowSize = camera.AnalysisStateWindowSize,
                AnalysisHoldFrames = camera.AnalysisHoldFrames,
                SopWindowMs = camera.SopWindowMs,
                SopMinScoreQ1000 = camera.SopMinScoreQ1000,
                SopMinVisibleRatioQ1000 = camera.SopMinVisibleRatioQ1000,
                OcrEnabled = camera.OcrEnabled ? 1 : 0,
                OcrRoiX = camera.OcrRoiX,
                OcrRoiY = camera.OcrRoiY,
                OcrRoiWidth = camera.OcrRoiWidth,
                OcrRoiHeight = camera.OcrRoiHeight,
                EnableCameraRecording = camera.EnableCameraRecording ? 1 : 0,
                RecordingRootDirectory = camera.RecordingRootDirectory,
                RecordingSegmentMinutes = camera.RecordingSegmentMinutes,
                RecordingContainerFormat = camera.RecordingContainerFormat,
                RecordingVideoEncoder = camera.RecordingVideoEncoder,
                RecordingCodecFourcc = camera.RecordingCodecFourcc,
                RecordingQueueCapacity = camera.RecordingQueueCapacity,
                RecordingBitrateMbps = camera.RecordingBitrateMbps,
                RecordingFps = camera.RecordingFps,
                SortOrder = i,
                CreatedUtcMs = nowUtcMs,
                UpdatedUtcMs = nowUtcMs
            }).ExecuteCommand();
        }
    }

    private void SaveSopProfileEntities(IReadOnlyList<CameraSopProfile> profiles)
    {
        DbSession.ConfigDb.Deleteable<SopStepEntity>().ExecuteCommand();
        DbSession.ConfigDb.Deleteable<SopProfileEntity>().ExecuteCommand();
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var i = 0; i < profiles.Count; i++)
        {
            var profile = NormalizeSopProfile(profiles[i]);
            DbSession.ConfigDb.Insertable(new SopProfileEntity
            {
                Id = profile.Id,
                Name = profile.Name,
                Strategy = profile.Strategy,
                FingerprintModuleId = profile.FingerprintModuleId,
                SortOrder = i,
                CreatedUtcMs = nowUtcMs,
                UpdatedUtcMs = nowUtcMs
            }).ExecuteCommand();

            foreach (var step in profile.Steps)
            {
                DbSession.ConfigDb.Insertable(new SopStepEntity
                {
                    ProfileId = profile.Id,
                    Step = step.Step,
                    Name = step.Name,
                    ActionCode = step.ActionCode,
                    TcnLabel = step.TcnLabel,
                    ExpectedStateCode = step.ExpectedStateCode,
                    CreatedUtcMs = nowUtcMs,
                    UpdatedUtcMs = nowUtcMs
                }).ExecuteCommand();
            }
        }
    }

    private static string LoadState(string key)
    {
        return DbSession.ConfigDb.Queryable<CameraSettingsStateEntity>()
            .Where(e => e.Key == key)
            .First()?.Value ?? string.Empty;
    }

    private static void SaveState(string key, string value)
    {
        var nowUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        DbSession.ConfigDb.Storageable(new CameraSettingsStateEntity
        {
            Key = key,
            Value = value ?? string.Empty,
            UpdatedUtcMs = nowUtcMs
        }).ExecuteCommand();
    }
}
