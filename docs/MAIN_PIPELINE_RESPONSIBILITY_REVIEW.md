# Main Pipeline Responsibility Review

## Purpose

This document captures the current answer to an older architectural question:

> Should the desktop main window still own an independent `_pipeline`, or should execution belong to session-owned pipelines?

As of 2026-05-16, the answer is clear:

- the old desktop-level independent pipeline path is no longer the intended architecture
- interactive video and camera flow already route through the selected session
- the remaining work is about trimming `MainViewModel`, not restoring a separate main-window execution lane

## Current State

### What `MainViewModel` should own

- current workspace selection
- current selected session
- user-facing command surface
- workspace projection and status formatting
- configuration entry points

### What `MainViewModel` should not own

- direct per-session startup policy
- session rebuild recovery logic
- task materialization branching
- worker runtime logic
- an independent execution pipeline separate from session-owned pipelines

## Current Session Boundary

Primary session execution responsibilities belong with:

- [CameraSessionViewModel.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Camera/CameraSessionViewModel.cs)
- [PipelineSessionController.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/PipelineSessionController.cs)
- [VideoPipeline.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Core/Pipeline/VideoPipeline.cs)

Application-layer orchestration around sessions belongs with:

- [CameraSessionWorkspaceCoordinator.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/CameraSessionWorkspaceCoordinator.cs)
- [CameraSessionLifecycleCoordinator.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/CameraSessionLifecycleCoordinator.cs)
- [WorkspaceSelectionCoordinator.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/WorkspaceSelectionCoordinator.cs)

## What Has Already Been Cleaned Up

- camera config load/save, session rebuild, and auto-start orchestration moved out of `MainViewModel`
- interactive video/camera launch now routes through coordinator-owned session resolution helpers
- startup precheck/materialization now routes through coordinator-owned preparation flow
- FSM-definition fanout no longer lives as an ad hoc loop in the view model

## What Still Remains

The remaining `MainViewModel` problem is not "too much pipeline code".
It is "too much UI orchestration glue".

Current remaining pressure areas:

- command refresh boilerplate
- workspace projection refresh wiring
- config reload side effects
- large amounts of status text and display-state mapping

## Recommendation

Do not re-open the old question of giving the main window its own independent execution pipeline.

The correct next direction is:

1. keep execution session-owned
2. keep orchestration in dedicated application coordinators
3. continue shrinking `MainViewModel` toward a projection/composition shell
