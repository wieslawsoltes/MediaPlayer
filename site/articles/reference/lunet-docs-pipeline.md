---
title: "Lunet Docs Pipeline"
---

# Lunet Docs Pipeline

This repository uses the same style of Lunet-powered docs site as the TreeDataGrid project, adapted to the MediaPlayer package surface.

## Files

- `.config/dotnet-tools.json`: pins the local `lunet` tool
- `build-docs.sh`: restores the tool and builds the site
- `site/config.scriban`: site identity, theme wiring, and `api.dotnet` configuration
- `site/menu.yml`: top-level navigation
- `site/articles/**`: curated docs content for MediaPlayer
- `site/.lunet/includes/_builtins/bundle.sbn-html`: local bundle-link override so API pages emit valid CSS and JS URLs
- `.github/workflows/docs.yml`: publishes the generated site to GitHub Pages
- `.github/workflows/ci.yml`: validates that docs still build on CI

## Local build

```bash
dotnet tool restore
./build-docs.sh
```

The generated site is written under `site/.lunet/build/www`.
