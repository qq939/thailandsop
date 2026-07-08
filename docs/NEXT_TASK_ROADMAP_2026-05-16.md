# Next Task Roadmap (2026-05-16)

## Purpose

This document lists the next macro tasks after the completion of:

- task-first workspace cleanup
- `MainViewModel` orchestration extraction round
- `VideoPipeline` shell decomposition round
- worker runtime boundary cleanup

It is intended to answer one question:

> What should we do next from a whole-system point of view?

## Current Architectural Baseline

By the end of the current refactor round:

- desktop workspace selection is task-first
- session startup is binding-oriented
- `MainViewModel` no longer owns the old highest-risk orchestration branches
- `VideoPipeline` is no longer the main architectural bottleneck
- worker runtime boundaries are split into facade, coordinator, lifecycle, request executor, policy, and state tracker

This means the next tasks should not simply continue the same cleanup line out of habit.

## Recommended Next Macro Tasks

### Task 1. Add tests for the extracted boundaries

Status:

- completed on 2026-05-16

Why now:

- the architecture is cleaner than before, but still protected mostly by successful builds
- tests will lock in the value of the refactor before the next feature wave

Suggested first coverage:

- `WorkspaceSelectionCoordinator`
- `CameraSessionWorkspaceCoordinator`
- worker runtime helpers

Completion signal:

- a dedicated test project exists
- the current refactor boundaries can be verified without manual desktop flows

### Task 2. Narrow `ModelWorkspaceService`

Status:

- completed on 2026-05-16

Why now:

- the desktop layer is already task-first
- this helper is now the most visible remaining model-centric holdout

Target file:

- [ModelWorkspaceService.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Workspace/ModelWorkspaceService.cs)

Completion signal:

- this helper reads as catalog/activation support, not as the conceptual center of selection flow

### Task 3. Clean up result publishing and compatibility flow

Status:

- completed on 2026-05-16

Why now:

- pipeline shell extraction is no longer the best use of effort
- the remaining complexity is about result semantics, not loop shape

Likely focus areas:

- generic task publishing
- legacy detection compatibility sinks
- analysis/persistence entry points

Completion signal:

- the publishing path for generic tasks is explicit
- legacy compatibility is clearly marked as compatibility, not the main model

### Task 4. Prepare the next feature-expansion boundary

Why now:

- the architecture is finally clean enough to decide the next real product-facing expansion

Decision candidates:

- OCR runtime
- sidecar task assignment
- richer generic payload publishing

Completion signal:

- the next feature line starts from the cleaned abstractions, not from historical compatibility paths

Current note:

- the focused `MainViewModel` refinement line is complete
- see [MAINVIEWMODEL_REFINEMENT_PLAN_2026-05-16.md](/C:/Users/ljia/source/repos/VideoInferenceDemo/docs/MAINVIEWMODEL_REFINEMENT_PLAN_2026-05-16.md)

### Task 5. Consolidate documentation and remove stale planning drift

Why now:

- the current code has already outrun several older planning notes
- cleaner documentation reduces future rework and re-analysis

Completion signal:

- framework, vision-task, and main-pipeline planning docs all reflect the same current architecture

## Suggested Execution Order

### Phase A. Stabilization

1. add a test project
2. cover desktop coordinators
3. cover worker runtime helpers

### Phase B. Remaining conceptual cleanup

1. narrow `ModelWorkspaceService`
2. remove any leftover model-centric assumptions below the desktop layer

### Phase C. Execution/publishing cleanup

1. isolate compatibility publishing further
2. make generic task result flow the default architectural path

### Phase D. New feature runway

1. choose OCR or sidecar-task expansion
2. define the needed task-assignment/result shape
3. build on top of the cleaned foundation

## What Should Not Be The Next Task

These are no longer the best next investments:

- more shell-only decomposition of `VideoPipeline`
- re-opening the old model-vs-task desktop selection problem
- rebuilding worker host boundaries from scratch again

Those lines have already delivered most of their current value.

## Final Recommendation

Now that Tasks 1, 2, and 3 are complete, the next single architecture task should be:

1. prepare the next feature-expansion boundary

If one additional task is chosen after that, it should be:

2. deepen test coverage around publishing/runtime expansion

That combination gives the best balance of stability, clarity, and readiness for the next runtime/task feature wave.
