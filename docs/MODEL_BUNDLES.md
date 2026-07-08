# Model Bundles

## Goal

The application now treats each inference model as a self-contained bundle under `DL/<model_name>/`.

This separates:

- model discovery
- model-specific config
- model task type and metadata
- future source-to-model routing

## Directory layout

Recommended structure:

```text
DL/
  sequence_bands/
    model.json
    sequence_model.onnx
  yolo_shell/
    model.json
    best.onnx
```

Notes:

- The runtime scans direct child folders under `DL`.
- If no child bundle is found, it falls back to the legacy `DL` root layout.
- `model.json` is now the preferred single source of truth.
- Legacy `classes.json` and `*.meta.json` are still tolerated as fallback, but they are no longer the recommended layout.
- Ready-to-copy templates live under `docs/templates/` and should not be placed under `DL/` until the real model file is ready.

## Manifest

Each bundle can define `model.json`:

```json
{
  "id": "sequence-bands",
  "displayName": "Sequence Bands",
  "description": "Vertical layer sequence model with band overlay rendering.",
  "taskType": "sequence_bands",
  "modelFile": "sequence_model.onnx",
  "classes": ["shell", "metal_plate", "spacer", "hand"],
  "boxColor": ["#00FF00", "#00B4FF", "#FFB000", "#00B000"],
  "boxThickness": 4,
  "labelFontScale": 3,
  "sequence": {
    "input_name": "input",
    "output_name": "logits"
  }
}
```

Supported fields:

- `id`: stable identifier persisted across restarts
- `displayName`: UI name
- `description`: UI helper text
- `taskType`: `sequence_bands` or `detection`
- `modelFile`: relative path to `.onnx`
- `classes`, `boxColor`, `boxThickness`, `labelFontScale`: draw config, previously stored in `classes.json`
- `sequence`: sequence-model metadata, previously stored in `*.meta.json`

If `model.json` is missing, the app tries to infer the bundle from the folder contents.

## Templates

Use these files as starting points when creating a new bundle:

- `docs/templates/model.detection.template.json`
- `docs/templates/model.sequence_bands.template.json`

Recommended workflow:

1. Copy the matching template into `DL/<bundle_name>/model.json`.
2. Update `id`, `displayName`, `description`, and `modelFile`.
3. Place the real `.onnx` file in the same bundle directory.
4. Adjust `classes`, colors, and sequence metadata if needed.

Do not leave placeholder manifests under `DL/`, because the runtime will treat them as real bundles during discovery.

## Current behavior

- The selected bundle is persisted in `model_selection.json`.
- The status panel lets the operator refresh bundle discovery and switch the active model.
- Before any video or camera run starts, the pipeline verifies that the selected bundle is the one currently loaded.

## Extension path

This layout is intended to support the next phase:

- multiple video sources
- per-source model selection
- model/task-specific processing branches

At that point, a source can bind to bundle `id` instead of hardcoded model paths.

## Runtime note

- Detection bundles are now expected to provide `.onnx` models.
- TensorRT acceleration, when needed, should be provided through ONNX Runtime execution providers such as `TensorRT -> CUDA -> CPU`.
- Direct `.engine` bundle loading is no longer part of the shared `Core` runtime path.
