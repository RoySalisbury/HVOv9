# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Repository documentation standardization (LICENSE, CONTRIBUTING, CHANGELOG, templates)
- `.editorconfig` for consistent formatting
- GitHub issue templates (bug report, feature request)
- GitHub pull request template

### Changed

- Rewritten `README.md` to reflect current repository state after project extractions
- Updated `.github/copilot-instructions.md` to remove references to extracted projects
- Cleaned up references to deleted workflows and projects

### Removed

- References to extracted projects no longer in this repo

### Migration History

The following projects were extracted from this monorepo into dedicated repositories:

| Extracted Project | Destination Repo | Notes |
|-------------------|-----------------|-------|
| `HVO.Iot.Devices` | [HVO.SDK](https://github.com/RoySalisbury/HVO.SDK) | IoT device abstractions and GPIO control |
| `HVO.Astronomy.CFITSIO` | [HVO.SDK](https://github.com/RoySalisbury/HVO.SDK) | FITS file I/O |
| `HVO.ZWOOptical.ASISDK` | [HVO.SDK](https://github.com/RoySalisbury/HVO.SDK) | ZWO camera SDK wrapper |
| `HVO.RoofControllerV4` | [HVO.RoofController](https://github.com/RoySalisbury/HVO.RoofController) | Observatory roof automation |
| `HVO.iOS` (iPad app) | [HVO.RoofController](https://github.com/RoySalisbury/HVO.RoofController) | .NET MAUI iPad app |
| `HVO.WebSite.v9` | [HVO.WebSite](https://github.com/RoySalisbury/HVO.WebSite) | Main observatory dashboard |
| `HVO.WebSite.Themes` | Copied to other repos | Shared UI themes |
| `HVO/` (core library) | [HVO.Core NuGet](https://github.com/RoySalisbury/HVO.SDK) | Replaced by NuGet package |
| `HVO.SourceGenerators` | [HVO.Core.SourceGenerators NuGet](https://github.com/RoySalisbury/HVO.SDK) | Replaced by NuGet package |
| `HVO.TheSkyX` | Deleted | Was a stub/placeholder |
