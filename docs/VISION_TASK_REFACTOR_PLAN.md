# Vision Task Refactor Plan

## Purpose

This document records the task/runtime architecture direction after the desktop workspace and worker-runtime cleanup completed on 2026-05-16.

It is no longer a speculative plan for introducing `VisionTask`.
That part is already in the normal application flow.

## Current State

The codebase already has these core concepts in active use:

- `VisionTaskDefinition`
- `PrimaryVisionTaskBinding`
- `IVisionTask`
- task factory registration
- ONNX-backed tasks and MediaPipe-backed tasks in the same broad architecture

Desktop/workspace behavior is now task-first:

- primary task selection is the top-level user flow
- model-backed activation is one way of producing a primary task binding
- session startup consumes binding-oriented results instead of separate model/task modes

## What Is Already Solved

### 1. Task-first desktop/workspace flow

Solved by:

- `VisionWorkspaceService`
- `WorkspaceSelectionCoordinator`
- `MainViewModel` primary-task UI flow

Outcome:

- the user-facing flow is no longer "pick model or pick task"
- the user-facing flow is "pick/apply primary task"

### 2. Binding as the execution boundary

Solved by:

- `PrimaryVisionTaskBinding`
- session rebuild/startup integration

Outcome:

- sessions do not care whether a binding came from an ONNX model source or a task-only path

### 3. Runtime coexistence is structurally possible

Solved enough to proceed by:

- worker host/runtime cleanup
- factory-driven task creation

Outcome:

- ONNX and MediaPipe no longer force the same old model-only application path

## What Still Needs To Happen

### 1. Narrow the remaining model-centric core helper

Current file:

- [ModelWorkspaceService.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Inference/Workspace/ModelWorkspaceService.cs)

Needed direction:

- keep discovery and activation support
- stop treating selected-model semantics as a conceptual center

### 2. Clarify the next assignment model beyond one primary task

The current architecture now supports clean primary-task binding, but the next real product question is:

- when do we introduce sidecar tasks formally?
- how should session-level task assignment be represented?

The likely next abstraction is a session task assignment/profile layer, not another model-selection workflow.

### 3. Finish generic result publishing

Task/runtime growth now depends less on selection flow and more on result semantics.

Needed direction:

- keep legacy detection compatibility where necessary
- avoid treating compatibility sinks as the permanent universal result model

### 4. Add verification

There is still no dedicated test project for:

- workspace materialization
- task binding application
- worker runtime helpers

That is now the biggest structural weakness around this refactor line.

## Recommended Next Vision/Task Milestones

### Milestone A. Stabilize current task-first behavior

- add tests for workspace selection and binding application
- narrow `ModelWorkspaceService`

### Milestone B. Clean publishing semantics

- isolate generic task publishing from legacy detection compatibility

### Milestone C. Prepare next runtime/task expansion

- define session task assignment shape
- decide how sidecar tasks will be represented
- use that shape as the base for OCR or other future runtimes

## Recommendation

The task refactor should now be treated as an active foundation, not as a future migration.

The right next step is to reinforce it:

1. test it
2. simplify the old core helper beneath it
3. finish the publishing side before adding the next major runtime
