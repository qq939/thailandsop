namespace VideoInferenceDemo;

public sealed class CameraSessionLifecycleCoordinator
{
    private readonly WorkspaceRunCoordinator _workspaceRunCoordinator;

    public CameraSessionLifecycleCoordinator(WorkspaceRunCoordinator workspaceRunCoordinator)
    {
        _workspaceRunCoordinator = workspaceRunCoordinator ?? throw new ArgumentNullException(nameof(workspaceRunCoordinator));
    }

    public CameraSessionPropertyChangeEffects HandlePropertyChanged(
        CameraSessionViewModel session,
        bool isSelectedSession,
        string? propertyName,
        PersonnelOptionItem? selectedPersonnel)
    {
        ArgumentNullException.ThrowIfNull(session);

        var refreshSelectedWorkspaceState = isSelectedSession;
        if (propertyName == nameof(CameraSessionViewModel.IsRunning))
        {
            var refreshPersonnelDisplay = session.IsRunning
                ? _workspaceRunCoordinator.TryBindSessionRun(session, selectedPersonnel)
                : _workspaceRunCoordinator.FinalizeSessionRun(session);

            return new CameraSessionPropertyChangeEffects(
                refreshSelectedWorkspaceState,
                RefreshWorkspaceProjection: true,
                RefreshModelCommands: true,
                RefreshControlCommands: true,
                RefreshPersonnelDisplay: refreshPersonnelDisplay);
        }

        if (propertyName == nameof(CameraSessionViewModel.ProductionOkCount) ||
            propertyName == nameof(CameraSessionViewModel.ProductionNgCount))
        {
            _workspaceRunCoordinator.PersistSessionProductionStats(session);
            return new CameraSessionPropertyChangeEffects(
                refreshSelectedWorkspaceState,
                RefreshWorkspaceProjection: isSelectedSession,
                RefreshModelCommands: false,
                RefreshControlCommands: false,
                RefreshPersonnelDisplay: false);
        }

        return new CameraSessionPropertyChangeEffects(
            refreshSelectedWorkspaceState,
            RefreshWorkspaceProjection: false,
            RefreshModelCommands: false,
            RefreshControlCommands: false,
            RefreshPersonnelDisplay: false);
    }
}
