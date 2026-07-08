namespace VideoInferenceDemo;

public sealed class WorkspaceRunCoordinator
{
    private readonly PersonnelRepository _personnelRepository;
    private readonly RunOperatorAssignmentRepository _runOperatorAssignmentRepository;
    private readonly RunProductionStatsRepository _runProductionStatsRepository;
    private readonly Dictionary<string, SessionRunBindingInfo> _activeRunBindings = new(StringComparer.OrdinalIgnoreCase);

    public WorkspaceRunCoordinator(
        PersonnelRepository personnelRepository,
        RunOperatorAssignmentRepository runOperatorAssignmentRepository,
        RunProductionStatsRepository runProductionStatsRepository)
    {
        _personnelRepository = personnelRepository ?? throw new ArgumentNullException(nameof(personnelRepository));
        _runOperatorAssignmentRepository = runOperatorAssignmentRepository ?? throw new ArgumentNullException(nameof(runOperatorAssignmentRepository));
        _runProductionStatsRepository = runProductionStatsRepository ?? throw new ArgumentNullException(nameof(runProductionStatsRepository));
    }

    public PersonnelOptionsLoadResult LoadPersonnelOptions(string? selectedCode)
    {
        var options = _personnelRepository.List(includeInactive: false)
            .Select(item => new PersonnelOptionItem(item.EmployeeCode, item.EmployeeName, item.Team))
            .ToList();

        var selectedPersonnel = selectedCode != null
            ? options.FirstOrDefault(item => string.Equals(item.EmployeeCode, selectedCode, StringComparison.OrdinalIgnoreCase))
            : null;

        return new PersonnelOptionsLoadResult(options, selectedPersonnel);
    }

    public PersonnelSelectionStatus EnsurePersonnelSelected(PersonnelOptionItem? selectedPersonnel)
    {
        return selectedPersonnel != null
            ? PersonnelSelectionStatus.Success
            : new PersonnelSelectionStatus(
                false,
                "No Operator",
                "\u8bf7\u5148\u5728\u4eba\u5458\u7ba1\u7406\u4e2d\u7ef4\u62a4\u5458\u5de5\uff0c\u5e76\u9009\u62e9\u5f53\u524d\u5458\u5de5\u3002",
                "\u5f53\u524d\u672a\u9009\u62e9\u5458\u5de5\uff0c\u65e0\u6cd5\u542f\u52a8\u65b0\u7684\u4f5c\u4e1a\u3002");
    }

    public void ResetSessionBindings()
    {
        _activeRunBindings.Clear();
    }

    public bool TryBindSessionRun(CameraSessionViewModel session, PersonnelOptionItem? selectedPersonnel)
    {
        ArgumentNullException.ThrowIfNull(session);

        var runUuid = session.CurrentRunUuid;
        if (string.IsNullOrWhiteSpace(runUuid))
        {
            return false;
        }

        var employeeCode = selectedPersonnel?.EmployeeCode;
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return false;
        }

        var employeeName = selectedPersonnel?.EmployeeName ?? employeeCode;
        var assignedUtcMs = session.CurrentRunStartedUtcMs > 0
            ? session.CurrentRunStartedUtcMs
            : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        _runOperatorAssignmentRepository.Upsert(
            runUuid,
            employeeCode,
            employeeName,
            selectedPersonnel?.Team,
            session.Name,
            session.Id,
            assignedUtcMs);
        _runProductionStatsRepository.Upsert(runUuid, session.ProductionOkCount, session.ProductionNgCount);
        _activeRunBindings[session.Id] = new SessionRunBindingInfo(runUuid, employeeCode, employeeName);
        return true;
    }

    public bool PersistSessionProductionStats(CameraSessionViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_activeRunBindings.TryGetValue(session.Id, out var binding))
        {
            return false;
        }

        _runProductionStatsRepository.Upsert(binding.RunUuid, session.ProductionOkCount, session.ProductionNgCount);
        return true;
    }

    public bool FinalizeSessionRun(CameraSessionViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!_activeRunBindings.TryGetValue(session.Id, out var binding))
        {
            return false;
        }

        _runOperatorAssignmentRepository.MarkReleased(binding.RunUuid);
        _runProductionStatsRepository.Upsert(binding.RunUuid, session.ProductionOkCount, session.ProductionNgCount);
        _activeRunBindings.Remove(session.Id);
        return true;
    }

    public bool TryGetActiveBinding(string sessionId, out SessionRunBindingInfo binding)
    {
        return _activeRunBindings.TryGetValue(sessionId, out binding!);
    }
}
