---
title: "Getting Started Overview"
---

# Getting Started Overview

MediaPlayer is split into three shippable libraries and one demo application.

| Project | Use it when |
| --- | --- |
| `MediaPlayer.Controls` | You want the playback control, GPU renderer integration, audio/device APIs, and workflow surface. |
| `MediaPlayer.Native.Abstractions` | You need to reason about provider selection or expose native-provider knobs in your own composition root. |
| `MediaPlayer.Native.Interop` | You want access to the interop provider catalogs that the control and workflow layers consult at runtime. |
| `MediaPlayer.Demo` | You want a full reference application for QuickTime-style playback UX and menu integration. |

A minimal integration usually needs only `MediaPlayer.Controls`.

Next steps:

1. [Install the packages](installation).
2. [Add `GpuMediaPlayer` to a window](quickstart-player).
3. [Register workflow services](quickstart-workflows) if you need export, trim, transform, or recording support.
