# MediaPlayer

[![CI](https://github.com/wieslawsoltes/MediaPlayer/actions/workflows/ci.yml/badge.svg)](https://github.com/wieslawsoltes/MediaPlayer/actions/workflows/ci.yml)
[![Docs](https://img.shields.io/badge/docs-github%20pages-0f766e)](https://wieslawsoltes.github.io/MediaPlayer/)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![Avalonia 12.2](https://img.shields.io/badge/Avalonia-12.2-0F172A)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

GPU-accelerated media playback for Avalonia with no airspace gap, native platform backends, FFmpeg and LibVLC fallback paths, and a reusable QuickTime-inspired application layer.

Documentation site: [wieslawsoltes.github.io/MediaPlayer](https://wieslawsoltes.github.io/MediaPlayer/)

<img width="3510" height="2034" alt="image" src="https://github.com/user-attachments/assets/75d0bfb4-f85e-4c40-a5a1-b98e9e9e8a43" />

## NuGet Packages

### Primary Packages

| Package | Purpose |
| --- | --- |
| `MediaPlayer.Controls` | Shippable Avalonia media playback control library with GPU compositing, backend selection, transport control APIs, and reusable media workflow services. |
| `MediaPlayer.Native.Abstractions` | Contracts, diagnostics models, backend/provider enums, and capability models shared across native playback and workflow implementations. |
| `MediaPlayer.Native.Interop` | Managed interop provider catalog and runtime selection helpers for native playback/workflow integrations. |

## Features

- No-airspace video composition inside Avalonia using GPU rendering paths instead of `NativeControlHost`.
- QuickTime-style playback shell patterns: floating HUD controls, auto-hide behavior, native macOS menu integration, and window chrome integration.
- Native backend selection for macOS and Windows, with FFmpeg and LibVLC fallback modes kept available as user-selectable options.
- Direct GPU texture upload mode with a compatibility copy-upload fallback, both exposed through the demo menu.
- Audio track, subtitle track, input/output device, and route management APIs on the reusable control surface.
- Reusable media workflow layer for trim, split, combine, export, transform, recording, and audio/video removal operations.
- Cross-platform demo app showing how to wire playback UX, workflow dialogs, diagnostics, and backend controls together.

## Architecture

| Layer | Responsibility |
| --- | --- |
| `MediaPlayer.Controls` | Public Avalonia control layer, GPU renderer integration, backend orchestration, audio/device APIs, workflow services, and reusable playback shell components. |
| `MediaPlayer.Native.Abstractions` | Backend-neutral contracts and capability models used by the control layer and native/interop implementations. |
| `MediaPlayer.Native.Interop` | Interop provider catalogs and runtime helpers for selecting native-backed playback and workflow providers. |
| `MediaPlayer.Demo` | Reference application showing QuickTime-style UX, native menu wiring, diagnostics, workflow commands, and backend selection. |

## Usage

### 1. Install the main control package

```bash
dotnet add package MediaPlayer.Controls
```

If you want direct access to backend/provider contracts in your own composition layer:

```bash
dotnet add package MediaPlayer.Native.Abstractions
dotnet add package MediaPlayer.Native.Interop
```

### 2. Use `GpuMediaPlayer` in XAML

```xml
<Window
    xmlns="https://github.com/avaloniaui"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="using:Sample.ViewModels"
    xmlns:media="clr-namespace:MediaPlayer.Controls;assembly=MediaPlayer.Controls"
    x:Class="Sample.MainWindow"
    x:DataType="vm:MainWindowViewModel">
  <media:GpuMediaPlayer
      Source="{Binding CurrentSource}"
      AutoPlay="True"
      LayoutMode="Uniform"
      Volume="100"
      PreferDirectGpuTextureUpload="True" />
</Window>
```

### 3. Drive playback from your ViewModel or service layer

```csharp
using System;
using MediaPlayer.Controls;

GpuMediaPlayer player = new();
player.Source = new Uri("file:///path/to/media.mp4");
player.Play();
player.Seek(TimeSpan.FromSeconds(30));
```

### 4. Register reusable workflow services

```csharp
using MediaPlayer.Controls.Workflows;
using Microsoft.Extensions.DependencyInjection;

ServiceCollection services = new();
services.AddMediaPlayerWorkflows(options =>
{
    options.PreferNativePlatformServices = true;
});
```

The workflow service layer exposes trim, split, combine, export, transform, recording, and media stream removal operations through `IMediaWorkflowService`.

## Backend Model

`GpuMediaPlayer` selects the strongest available playback backend for the current platform and configuration while preserving a single Avalonia control surface:

- macOS: native AVFoundation-based helper paths and interop-driven providers.
- Windows: native Media Foundation-based helper paths and interop-driven providers.
- Cross-platform fallback: FFmpeg and LibVLC-backed decoding modes remain available for compatibility and diagnostics.
- Rendering: Avalonia-hosted GPU composition with direct texture upload enabled by default, with a compatibility path available when drivers require it.

This design keeps video inside the Avalonia compositor and avoids the classic airspace problems that appear when embedding separate native child windows.

## Demo

The demo app shows the intended production UX patterns:

- QuickTime-style floating transport HUD and centered playback controls.
- Native macOS application menus and platform window integration.
- Backend diagnostics, provider selection, texture upload mode switching, and audio route inspection.
- Reusable workflow entry points for export, trim, transform, record, and related media operations.

Run the demo:

```bash
dotnet run --project src/MediaPlayer.Demo/MediaPlayer.Demo.csproj
```

## Building

### Prerequisites

- .NET 10 SDK or later
- Platform-native media dependencies required by the backend modes you enable at runtime

### Build, test, and pack

```bash
dotnet build MediaPlayer.sln -warnaserror
dotnet test MediaPlayer.sln
dotnet pack src/MediaPlayer.Controls/MediaPlayer.Controls.csproj -c Release
dotnet pack src/MediaPlayer.Native.Abstractions/MediaPlayer.Native.Abstractions.csproj -c Release
dotnet pack src/MediaPlayer.Native.Interop/MediaPlayer.Native.Interop.csproj -c Release
```

## CI and Release

The repository includes GitHub Actions workflows modeled on the RoyalTerminal repository.

- `ci.yml` restores, builds, and tests across platforms and creates preview packages.
- `release.yml` validates and packs the solution. A release tag publishes the packages and symbols to NuGet.org, then creates a GitHub release. A manual run on a branch creates preview artifacts.

### NuGet Trusted Publishing

The NuGet publishing job uses the `nuget` GitHub environment. The environment accepts only `v*` tags and its `NUGET_USER` variable contains the NuGet.org username. The job exchanges its GitHub OIDC token for a temporary NuGet API key.

Use these values when you create the NuGet.org policy. [NuGet.org Trusted Publishing](https://www.nuget.org/account/trustedpublishing)

| Field | Value |
| --- | --- |
| Policy Name | `MediaPlayer releases` |
| Package Owner | `wieslawsoltes` |
| CI/CD Provider | GitHub Actions |
| Repository Owner | `wieslawsoltes` |
| Repository | `MediaPlayer` |
| Workflow File | `release.yml` |
| Environment | `nuget` |
| Scopes | Push only new package versions |

Enter `MediaPlayer.Controls`, `MediaPlayer.Native.Abstractions`, and `MediaPlayer.Native.Interop` on separate lines in **Glob Patterns and Packages**. Once the policy is active, push a version tag such as `v12.2.0` from `main`.

## Project Structure

```text
src/
  MediaPlayer.Controls/
  MediaPlayer.Native.Abstractions/
  MediaPlayer.Native.Interop/
  MediaPlayer.Demo/
  MediaPlayer.Controls.Tests/
  MediaPlayer.Demo.Tests/
plan/
```

## License

MIT. See [LICENSE](https://github.com/wieslawsoltes/MediaPlayer/blob/main/LICENSE).
