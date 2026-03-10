---
title: "Quickstart: Player Control"
---

# Quickstart: Player Control

Add the control to a window and bind the media source from your view model.

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
      LayoutMode="Fit"
      Volume="85"
      PreferDirectGpuTextureUpload="True" />
</Window>
```

Programmatic control:

```csharp
using System;
using MediaPlayer.Controls;

GpuMediaPlayer player = new();
player.Source = new Uri("file:///path/to/media.mp4");
player.Play();
player.Seek(TimeSpan.FromSeconds(30));
player.PlaybackRate = 1.0;
player.IsLooping = false;
```

Useful runtime diagnostics exposed by the control:

- `ActiveDecodeApi`
- `ActiveRenderPath`
- `ActiveProfileName`
- `ConfiguredNativeProviderMode`
- `ActiveNativePlaybackProvider`
- `NativePlaybackFallbackReason`
- `AudioCapabilities`
