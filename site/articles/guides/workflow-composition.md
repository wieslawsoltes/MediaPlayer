---
title: "Workflow Composition"
---

# Workflow Composition

The workflow layer is intended for application features such as export dialogs, batch transforms, and lightweight editing operations.

## Typical flow

1. Resolve `IMediaWorkflowService` from DI.
2. Build preset or options objects.
3. Execute the workflow method.
4. Surface `MediaWorkflowResult.Success` and `MediaWorkflowResult.ErrorMessage` in the UI.

## Export example

```csharp
MediaExportOptions options = new(
    QualityProfile: MediaWorkflowQualityProfile.Quality,
    AudioCodec: "aac",
    AudioBitrateKbps: 256,
    AudioFormat: default,
    NormalizeLoudness: true);

MediaWorkflowResult result = await workflows.ExportAsync(
    source,
    outputPath,
    MediaExportPreset.Video1080p,
    options);
```

## Recording example

Use `MediaRecordingPreset` and `MediaRecordingOptions` to pick audio-only, movie, or screen-oriented capture behavior while still delegating provider selection to the workflow layer.
