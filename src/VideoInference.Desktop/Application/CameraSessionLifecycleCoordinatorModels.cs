namespace VideoInferenceDemo;

public sealed record CameraSessionPropertyChangeEffects(
    bool RefreshSelectedWorkspaceState,
    bool RefreshWorkspaceProjection,
    bool RefreshModelCommands,
    bool RefreshControlCommands,
    bool RefreshPersonnelDisplay);
