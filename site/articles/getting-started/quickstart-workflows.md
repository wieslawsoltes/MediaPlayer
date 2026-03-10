---
title: "Quickstart: Workflow Services"
---

# Quickstart: Workflow Services

Register the reusable workflow surface in your composition root.

```csharp
using MediaPlayer.Controls.Workflows;
using Microsoft.Extensions.DependencyInjection;

ServiceCollection services = new();
services.AddMediaPlayerWorkflows(options =>
{
    options.PreferNativePlatformServices = true;
});
```

Resolve and use `IMediaWorkflowService`:

```csharp
IMediaWorkflowService workflows = serviceProvider.GetRequiredService<IMediaWorkflowService>();
MediaWorkflowResult result = await workflows.ExportAsync(
    new Uri(sourcePath),
    outputPath,
    MediaExportPreset.Video1080p,
    MediaWorkflowQualityProfile.Quality);
```

The same service surface covers:

- `TrimAsync`
- `SplitAsync`
- `CombineAsync`
- `RemoveAudioAsync`
- `RemoveVideoAsync`
- `TransformAsync`
- `ExportAsync`
- `RecordAsync`

Use `IMediaWorkflowProviderDiagnostics` when you want to show which provider path was selected for the workflow layer.
