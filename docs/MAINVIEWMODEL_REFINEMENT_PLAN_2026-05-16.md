# MainViewModel Refinement Plan (2026-05-16)

## Purpose

This document narrows the next `MainViewModel` cleanup to a small, high-value scope.

It answers one question:

> If we continue refining `MainViewModel`, what should we change next without over-designing the desktop layer?

## Current Assessment

Status update:

- Step 1 completed on 2026-05-16
- Step 2 completed on 2026-05-16
- Step 3 completed on 2026-05-16
- this refinement line is complete

Current file:

- [MainViewModel.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Shell/MainViewModel.cs)

The file is no longer the old orchestration monolith, but it is still the largest remaining desktop composition surface.

What is already in a good place:

- workspace materialization routes through application coordinators
- camera/session rebuild and startup no longer live as the main branching hotspot
- pipeline execution and worker runtime logic are already outside the view model

What still creates friction:

- refresh behavior is cleaner than before, but still centered in one large shell-facing type
- the file is still a sizable desktop composition surface even after the focused cleanup

## The Core Problem

The main problem is not "too much business logic in `MainViewModel`".

The main problem is:

- too many implicit refresh rules
- too much coupling between state meaning and state wording
- too many places where one local change forces several manual UI refresh calls

That means the next cleanup should focus on:

1. state semantics
2. refresh structure
3. config/FSM side-effect organization

It should not focus on inventing new general frameworks.

## What Should Stay In MainViewModel

These responsibilities still belong here:

- current desktop selections
- user-facing commands
- top-level desktop presentation state
- calling coordinators and applying their results
- building the final projection used by the shell

This is important because the goal is not to hollow the file out artificially.

## What Should Not Stay As-Is

### 1. Status should not be derived from text

Current issue:

- `BuildStatusSnapshot()` still maps strings such as `"Model Selected"`, `"No Model"`, `"Error"`, `"No Session"`, and `"No Camera"` back into run-state semantics

Why this is high value:

- wording changes can accidentally change behavior
- localization becomes risky
- tests must reason through text instead of explicit state

Recommended fix:

- introduce one small explicit top-level state model for desktop status
- keep display text as a projection of that state, not the source of truth

Do not:

- introduce a generic reducer/store framework
- introduce a full-blown state-machine library

Target shape:

- one explicit desktop/workspace status value
- one optional blocking/error detail
- formatting methods that produce `StatusText`, `InferenceStatus`, and related display strings

### 2. Refresh rules should not stay scattered

Current issue:

- `NotifyWorkspaceProjectionChanged()`
- `NotifyControlCommandsChanged()`
- `NotifyModelCommandsChanged()`
- many direct `OnPropertyChanged(...)` calls

These are spread across:

- property change partial methods
- interactive flow methods
- session lifecycle callbacks
- config reload paths

Why this is high value:

- it is hard to know which refreshes are required for any given state change
- regression risk grows when new commands or projection fields are added

Recommended fix:

- consolidate refresh behavior into a few explicit local refresh entry points

Suggested minimum set:

- `RefreshWorkspacePresentation()`
- `RefreshSelectedSessionPresentation()`
- `RefreshCommandStates()`
- `RefreshPersonnelPresentation()`

These may remain private `MainViewModel` methods.

Do not:

- build a general event/effect system
- create many tiny coordinator classes just to proxy `OnPropertyChanged`

### 3. Config/FSM reload should become one narrow application-facing operation

Current issue:

- `ReloadAppConfigurationState()` still combines config loading, analysis-engine update, FSM snapshot rebuilding, session FSM fanout, and UI re-application

Why this matters:

- it is one of the clearest remaining cross-layer side-effect bundles
- future task/runtime expansion will likely need to plug into the same path

Recommended fix:

- move this chain behind one narrow application-facing operation
- either extend an existing coordinator or add one small config-focused coordinator

Preferred shape:

- load config
- produce a result object with ordered FSM definitions and derived desktop/session effects
- let `MainViewModel` apply the result

Do not:

- split this into multiple generic services unless new use cases truly appear

## Recommended Refactor Order

### Step 1. Replace string-derived status with explicit desktop state

Status:

- completed on 2026-05-16

Scope:

- [MainViewModel.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Shell/MainViewModel.cs)

Needed outcome:

- `BuildStatusSnapshot()` no longer relies on display strings as behavioral inputs
- display strings are generated from explicit state

Why first:

- this gives the biggest clarity gain with the smallest architectural footprint

### Step 2. Consolidate refresh entry points inside MainViewModel

Status:

- completed on 2026-05-16

Scope:

- [MainViewModel.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Shell/MainViewModel.cs)
- optionally [WorkspaceProjectionBuilder.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Application/WorkspaceProjectionBuilder.cs) if projection wiring needs cleanup

Needed outcome:

- fewer ad hoc refresh calls
- easier reasoning about command/presentation invalidation

Why second:

- once state semantics are explicit, refresh grouping becomes much easier to organize cleanly

### Step 3. Narrow config/FSM reload into one small application-facing operation

Status:

- completed on 2026-05-16

Scope:

- [MainViewModel.cs](/C:/Users/ljia/source/repos/VideoInferenceDemo/src/VideoInference.Desktop/Shell/MainViewModel.cs)
- one existing or new narrow coordinator under `src/VideoInference.Desktop/Application`

Needed outcome:

- config reload no longer looks like a local side-effect script
- future task/runtime additions have a cleaner extension point

Why third:

- it is useful, but it depends on the state/refresh cleanup being clearer first

## Guardrails Against Over-Design

These guardrails are part of the plan:

- do not introduce a desktop-wide event bus
- do not introduce a general effect system
- do not introduce a general reducer/store pattern
- do not split every helper into its own service
- do not move pure view-model presentation concerns into application coordinators unless they are reused or clearly cross-layer

The preferred outcome is:

- fewer implicit rules
- fewer status strings used as logic inputs
- fewer refresh call sites

Not:

- more layers
- more abstractions
- more framework-like plumbing

## Concrete Signs The Work Is Successful

The cleanup is successful when:

- `BuildStatusSnapshot()` no longer depends on `StatusText` wording
- changing one top-level state usually requires one refresh call path, not several scattered ones
- config reload has one clear application-facing entry point
- `MainViewModel` is easier to test without becoming thinner only on paper

## Recommended Next Move

This refinement line is complete.

If `MainViewModel` is revisited again, it should be because a concrete new feature exposes a new presentation hotspot, not because this cleanup still lacks a missing structural step.
