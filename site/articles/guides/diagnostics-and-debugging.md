---
title: "Diagnostics and Debugging"
---

# Diagnostics and Debugging

The library exposes diagnostics in the public control and workflow surfaces so consumers can build their own inspector UI.

## Playback diagnostics

`GpuMediaPlayer` exposes:

- decode API
- render path
- backend profile name
- active native provider
- fallback reason
- audio capabilities
- backend capability table
- last error string

## Workflow diagnostics

`IMediaWorkflowProviderDiagnostics` reports the configured provider mode, active provider kind, and fallback reason selected by `AddMediaPlayerWorkflows(...)`.

## Demo reference

The demo's movie inspector window is the canonical example of how to surface provider and rendering details to users without coupling application UI to backend-specific types.
