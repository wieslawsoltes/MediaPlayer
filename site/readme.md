---
title: "MediaPlayer for Avalonia"
layout: simple
og_type: website
---

# MediaPlayer for Avalonia

GPU-accelerated media playback for Avalonia with no airspace gap, native platform backends, FFmpeg and LibVLC fallback modes, and reusable media workflow services for trim, transform, export, and recording operations.

<div class="d-flex flex-wrap gap-3 mt-4 mb-4">
  <a class="btn btn-primary btn-lg" href="articles/getting-started/overview"><i class="bi bi-rocket-takeoff" aria-hidden="true"></i> Start Here</a>
  <a class="btn btn-outline-secondary btn-lg" href="api"><i class="bi bi-braces" aria-hidden="true"></i> Browse API</a>
  <a class="btn btn-outline-secondary btn-lg" href="https://github.com/wieslawsoltes/MediaPlayer"><i class="bi bi-github" aria-hidden="true"></i> Repository</a>
</div>

## What This Project Ships

<div class="row row-cols-1 row-cols-md-2 row-cols-xl-3 g-4 mb-4">
  <div class="col"><div class="card h-100"><div class="card-body"><h3 class="h5">No-airspace video rendering</h3><p class="mb-0">`GpuMediaPlayer` keeps video inside the Avalonia compositor and avoids `NativeControlHost` gaps by rendering through the GPU path.</p></div></div></div>
  <div class="col"><div class="card h-100"><div class="card-body"><h3 class="h5">Backend selection</h3><p class="mb-0">macOS and Windows can prefer native playback providers while FFmpeg and LibVLC remain available as compatibility and diagnostics paths.</p></div></div></div>
  <div class="col"><div class="card h-100"><div class="card-body"><h3 class="h5">Workflow services</h3><p class="mb-0">The workflow layer exposes trim, split, combine, export, transform, remove-audio/remove-video, and recording operations behind a reusable service interface.</p></div></div></div>
</div>

## Documentation Map

<div class="row row-cols-1 row-cols-md-2 g-4">
  <div class="col"><div class="card h-100"><div class="card-body"><h2 class="h4">Getting Started</h2><p>Install the packages, put `GpuMediaPlayer` into a window, and register workflow services.</p><a href="articles/getting-started" class="btn btn-sm btn-primary">Open section</a></div></div></div>
  <div class="col"><div class="card h-100"><div class="card-body"><h2 class="h4">Concepts</h2><p>Understand layering, backend selection, rendering, native provider modes, and audio/video sync responsibilities.</p><a href="articles/concepts" class="btn btn-sm btn-primary">Open section</a></div></div></div>
  <div class="col"><div class="card h-100"><div class="card-body"><h2 class="h4">Guides</h2><p>Apply QuickTime-style shell patterns, diagnostics, backend switching, and workflow composition in your own app.</p><a href="articles/guides" class="btn btn-sm btn-primary">Open section</a></div></div></div>
  <div class="col"><div class="card h-100"><div class="card-body"><h2 class="h4">Reference</h2><p>Package map, native provider modes, docs pipeline details, and repository-specific reference material.</p><a href="articles/reference" class="btn btn-sm btn-primary">Open section</a></div></div></div>
</div>

## Primary Packages

| Package | Purpose |
| --- | --- |
| `MediaPlayer.Controls` | Avalonia control layer with `GpuMediaPlayer`, audio/device APIs, GPU rendering integration, and workflow services. |
| `MediaPlayer.Native.Abstractions` | Provider selection contracts, diagnostics, environment knobs, and backend-neutral models. |
| `MediaPlayer.Native.Interop` | Managed interop catalogs used to discover and select runtime playback and workflow providers. |

## Start With These Pages

- [Getting Started Overview](articles/getting-started/overview)
- [Install Packages](articles/getting-started/installation)
- [Quickstart: Player Control](articles/getting-started/quickstart-player)
- [Quickstart: Workflow Services](articles/getting-started/quickstart-workflows)
- [Backend Selection Model](articles/concepts/backend-selection)
- [No-Airspace Rendering Model](articles/concepts/rendering-no-airspace)

## Repository

- Source code and issues: [github.com/wieslawsoltes/MediaPlayer](https://github.com/wieslawsoltes/MediaPlayer)
- Published packages: [nuget.org/packages/MediaPlayer.Controls](https://www.nuget.org/packages/MediaPlayer.Controls)
