# Framework Decoupling Refactor Plan

## Goal

This plan tracks the architectural cleanup needed to keep the desktop app, session runtime, and task/runtime layers understandable as the system grows.

The long-term direction is:

- `VisionTask` is the primary application concept
- `Model` is one possible source of a task binding
- session startup and task attachment are explicit
- UI state, orchestration, execution, and publishing stay in separate layers
- additional runtimes such as OCR can be added without repeating the same coupling pattern

## Current Snapshot (2026-05-16)

The codebase has moved well past the original starting point of this document.

Already completed:

- workspace selection is task-first at the desktop layer
- `PrimaryVisionTaskBinding` is the single execution-facing binding concept
- model-backed and task-only startup converge on one materialization flow
- `MainViewModel` no longer owns the old dual-track model/task apply flow
- camera/session orchestration has moved into dedicated application coordinators
- `VideoPipeline` has been reduced to an execution-oriented shell over smaller coordinators
- `NamedPipeVisionWorkerHost` is now a thin facade
- worker runtime responsibilities are split across state tracking, request execution, lifecycle, and policy helpers
- a dedicated test project now covers the first extracted desktop and worker-runtime boundaries
- `ModelWorkspaceService` now reads as preferred-source and activated-source catalog support instead of selection-flow ownership
- pipeline publishing now exposes a generic `IVisionResultSink` surface, while legacy detection compatibility is assembled explicitly above the pipeline boundary

Because of that, the old framing of "first extract a few services" is no longer accurate.

The architectural center of gravity is now:

1. keep the task-first workspace shape clean
2. keep shrinking desktop orchestration hot spots
3. finish the remaining compatibility cleanup around pipeline publishing and model-centric core helpers
4. add verification and documentation so the new boundaries stay stable

## What Is No Longer The Main Problem

### `VideoPipeline` as a monolith

This is no longer the main blocker.

Current file:

- [VideoPipeline.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.cs)

The file is still important, but it is no longer the old all-in-one shell. Lifecycle, packet queues, capture loops, infer/render loop shells, and helper types have already been extracted.

Future cleanup here should focus on:

- compatibility publishing semantics
- task/runtime attachment semantics
- clearer result dispatch boundaries

Not on more shell extraction for its own sake.

### Worker host collapse

This is also no longer blocked on the original host class.

Current files:

- [NamedPipeVisionWorkerHost.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Vision/Workers/NamedPipeVisionWorkerHost.cs)
- [NamedPipeWorkerRuntimeCoordinator.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Vision/Workers/NamedPipeWorkerRuntimeCoordinator.cs)

The host facade is already thin. The remaining value is in tests, status-surface cleanup, and preparing the same boundary for future runtimes.

## Main Remaining Coupling Problems

### 1. `MainViewModel` is still too large

Current file:

- [MainViewModel.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Shell/MainViewModel.cs)

The worst orchestration branches have been moved out, but the file is still large and still mixes:

- UI selections and presentation state
- command exposure
- workspace projection notifications
- config reload effects
- FSM display synchronization
- status text mapping

Impact:

- the file is harder to audit than it should be
- refresh behavior is clearer than before, but still concentrated in one large shell-facing type
- behavior is correct but not yet especially cheap to test

Recommended refinement approach:

- the focused refinement line around explicit status state, refresh consolidation, and config/FSM reload organization is now complete
- avoid introducing generic desktop state frameworks or coordinator overgrowth
- see [MAINVIEWMODEL_REFINEMENT_PLAN_2026-05-16.md](/C:/Users/ljia/source/repos/VideoInferenceDemo/docs/MAINVIEWMODEL_REFINEMENT_PLAN_2026-05-16.md)

### 2. Test coverage now exists, but is still only first-wave coverage

Current repo state:

- a dedicated test project is present in the solution
- the first coverage wave locks `WorkspaceSelectionCoordinator`, `CameraSessionWorkspaceCoordinator`, and worker-runtime helpers

Impact:

- the latest refactor boundaries are no longer protected only by successful builds
- broader regression coverage is still missing below this first stabilization line

## Current Layer Map

### UI layer

Main responsibilities:

- `MainViewModel`
- XAML cards and desktop-facing state projection

Expected role:

- selections
- commands
- user-visible status
- projection formatting

### Application orchestration layer

Main responsibilities:

- [SessionTaskOrchestrator.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/SessionTaskOrchestrator.cs)
- [WorkspaceRunCoordinator.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/WorkspaceRunCoordinator.cs)
- [CameraSessionWorkspaceCoordinator.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/CameraSessionWorkspaceCoordinator.cs)
- [CameraSessionLifecycleCoordinator.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/CameraSessionLifecycleCoordinator.cs)
- [WorkspaceSelectionCoordinator.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/WorkspaceSelectionCoordinator.cs)
- [WorkspaceProjectionBuilder.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/WorkspaceProjectionBuilder.cs)

Expected role:

- workspace materialization
- session rebuild/startup support
- run/personnel coordination
- desktop projection aggregation

### Session execution layer

Main responsibilities:

- [CameraSessionViewModel.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Camera/CameraSessionViewModel.cs)
- [PipelineSessionController.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/PipelineSessionController.cs)

Expected role:

- one session's state
- one session's primary task binding
- one session's source control and runtime status

### Task/runtime layer

Main responsibilities:

- `VisionTaskDefinition`
- `IVisionTask`
- task factories
- worker runtime helpers

Expected role:

- create task instances
- manage runtime-specific state and transport
- expose uniform execution surfaces

### Publishing/persistence layer

Main responsibilities:

- result dispatch coordinators
- sinks and compatibility adapters
- analysis and persistence dependencies

Expected role:

- explicit publishing direction
- clear separation between generic task payloads and legacy compatibility storage

## Completed Refactor Packages

### PR1. Remove model-first UI residue from `MainViewModel`

Status:

- completed on 2026-05-16

Outcome:

- desktop apply flow is primary-task oriented
- model information is presentation metadata, not a parallel workflow

### PR2. Reduce `VisionWorkspaceService` dependence on model-first core semantics

Status:

- completed on 2026-05-16

Outcome:

- `VisionWorkspaceService` keeps primary-task and model-source selection state explicitly
- model activation resolves from primary-task source metadata instead of an old selected-model path

### PR3. Converge workspace materialization on one binding flow

Status:

- completed on 2026-05-16

Outcome:

- task-only and model-backed startup now converge on one binding-oriented result
- binding application is owned by the workspace selection coordinator

### PR4. Thin `MainViewModel` further after task-first closure

Status:

- completed on 2026-05-16

Outcome:

- camera config load/save, session rebuild, auto-start, interactive launch, session start preparation, and FSM fanout now route through coordinators
- `MainViewModel` is still large, but it is no longer the old orchestration hotspot

### PR5. Continue worker runtime boundary cleanup

Status:

- completed on 2026-05-16

Outcome:

- state/diagnostics tracking is isolated
- request execution and ping/degradation handling are isolated
- startup/stop/unexpected-exit handling is isolated
- degradation and recovery transitions are isolated behind runtime policy

### PR6. Narrow `ModelWorkspaceService`

Status:

- completed on 2026-05-16

Outcome:

- `ModelWorkspaceService` now owns only catalog discovery, preferred model-source persistence, and activated model-source tracking
- selected-model readiness semantics were removed from the core helper
- desktop/application callers now consume preferred/activated model-source terminology instead of the old selected/active model pair where the core helper is involved

### PR7. Clean up result publishing and compatibility flow

Status:

- completed on 2026-05-16

Outcome:

- `VideoPipeline` now publishes through a single generic `IVisionResultSink` surface
- legacy detection compatibility is adapted explicitly through `LegacyDetectionCompatibilityVisionResultSink`
- desktop pipeline support now builds a compatibility vision sink instead of injecting legacy result sinks directly into pipeline execution APIs

## Remaining Macro Work

### 1. Add more tests beyond the first stabilization wave

Target areas:

- desktop application coordinators
- worker runtime helpers
- workspace materialization flow
- publishing/runtime expansion boundaries

Needed outcome:

- refactoring confidence comes from tests, not only successful builds

### 2. Decide the next task/runtime expansion shape

Candidate lines:

- OCR runtime integration
- sidecar task assignment
- broader task payload publishing

Needed outcome:

- future feature work happens on top of the cleaned task/runtime architecture, not around it

## Recommended File-Level Focus Now

### Highest priority

- [MainViewModel.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Shell/MainViewModel.cs)
- deeper test coverage on top of the existing test project
- OCR/sidecar runtime expansion planning and payload shape design

### Medium priority

- worker status projection surfaces
- document consolidation
- feature-expansion implementation prep

## Documentation Status

This document is now the authoritative high-level refactor status for the current cleanup round.

The older documents below were useful historically, but had drifted or become unreadable and should now be treated as refreshed companion documents instead of untouched archives:

- [VISION_TASK_REFACTOR_PLAN.md](/C:/Users/ljia/source/repos/VideoInferenceDemo/docs/VISION_TASK_REFACTOR_PLAN.md)
- [MAIN_PIPELINE_RESPONSIBILITY_REVIEW.md](/C:/Users/ljia/source/repos/VideoInferenceDemo/docs/MAIN_PIPELINE_RESPONSIBILITY_REVIEW.md)

For the concrete next-stage worklist, see:

- [NEXT_TASK_ROADMAP_2026-05-16.md](/C:/Users/ljia/source/repos/VideoInferenceDemo/docs/NEXT_TASK_ROADMAP_2026-05-16.md)

## Final Recommendation

The current refactor lines are no longer blocked by the original desktop/workspace or worker-runtime monoliths.

The best next move is not to reopen those completed lines. The best next move is:

1. extend coverage beyond the first stabilization wave
2. choose the next feature-expansion boundary on top of the cleaned task/runtime surface
3. keep shrinking remaining UI composition hot spots only where they materially help that next feature wave

That keeps the momentum from this refactor round while reducing the chance that future OCR or sidecar work reintroduces the same coupling in a new place.
