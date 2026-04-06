# Elsa Packages Overview

## Core Installation

The foundational `Elsa` package bundles five essential components: Elsa.Api.Common, Elsa.Mediator, Elsa.Workflows.Core, Elsa.Workflows.Management, Elsa.Workflows.Runtime.

```bash
dotnet add package Elsa
```

## Distribution Channels

**Stable Releases** arrive through NuGet.org for production-ready versions.

**Release Candidates** also appear on NuGet.org, offering early access to upcoming functionality. May still undergo changes before final release.

**Preview Versions** deploy automatically to Feedz whenever code reaches the v3 branch. May introduce breaking changes.

## Version Numbering

- Released: Major.Minor.Revision (e.g., 3.0.1)
- Release Candidates: Major.Minor.Revision-rcX (e.g., 3.0.2-rc1)
- Preview: Major.Minor.Revision-preview.X (e.g., 3.0.2-preview.128)
