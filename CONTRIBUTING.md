# Contributing

Thank you for your interest in contributing to HVOv9! This document outlines the workflow, standards, and expectations for contributions.

> **Note**: Most active development now happens in dedicated repos ([HVO.SkyMonitor](https://github.com/RoySalisbury/HVO.SkyMonitor), [HVO.RoofController](https://github.com/RoySalisbury/HVO.RoofController), [HVO.WebSite](https://github.com/RoySalisbury/HVO.WebSite), [HVO.SDK](https://github.com/RoySalisbury/HVO.SDK)). This repo contains SkyMonitorV5 (production), Playground (web experiments), and legacy reference code.

## Table of Contents

- [Getting Started](#getting-started)
- [Branch Naming](#branch-naming)
- [Development Workflow](#development-workflow)
- [Coding Standards](#coding-standards)
- [Running Tests Locally](#running-tests-locally)
- [Pull Request Process](#pull-request-process)
- [PR Checklist](#pr-checklist)
- [Issue & PR Labels](#issue--pr-labels)

---

## Getting Started

1. Clone the repository and open in VS Code Dev Container or GitHub Codespaces.
2. Verify the build: `dotnet build` from `src/` — expect **zero warnings, zero errors**.
3. Run automated tests: `dotnet test` from `src/`.

---

## Branch Naming

| Pattern | Use For |
|---------|---------|
| `feature/<issue#>-<short-desc>` | New features and enhancements (e.g., `feature/34-star-detection-threshold`) |
| `fix/<issue#>-<short-desc>` | Bug fixes and corrective changes (e.g., `fix/42-null-ref-on-empty-frame`) |

Always branch from `main`. Use the `feature/` or `fix/` pattern for all work, including documentation-only and refactor-only changes.

---

## Development Workflow

1. **Create a feature branch** from `main`:
   ```bash
   git checkout main && git pull origin main
   git checkout -b feature/{issue-number}-{short-description}
   ```

2. **Make incremental commits** with [Conventional Commits](https://www.conventionalcommits.org/):
   ```
   feat: add cloud coverage threshold config (#34)
   fix: handle null frame in star detection (#42)
   docs: update SkyMonitorV5 configuration reference (#50)
   test: add star detection edge case tests (#38)
   refactor: extract image processing pipeline (#45)
   ```

3. **Run build and tests** before pushing:
   ```bash
   cd src
   dotnet build
   dotnet test
   ```

4. **Push and create a PR** targeting `main`.

---

## Coding Standards

- **Language**: C# / .NET 9
- **Style**: Follow existing conventions in the codebase and `.github/copilot-instructions.md`
- **Warnings**: Build must produce **zero warnings and zero errors**
- **Tests**: All new logic must have unit tests
- **Documentation**: Update docs if the change adds or modifies features, configuration, or API endpoints

---

## Running Tests Locally

```bash
cd src

# All tests
dotnet test

# Specific project
dotnet test HVO.SkyMonitorV5/HVO.SkyMonitorV5.RPi.Tests
dotnet test HVO.WebSite/HVO.WebSite.Playground.Tests
```

---

## Pull Request Process

1. **Verify** the build and tests pass with zero warnings.
2. **Create the PR** with a clear title and description:
   - Summary of what the PR does
   - Which issue it resolves (`Resolves #N`)
   - Key implementation details
   - Files changed
3. **Address all review comments** — fix code, respond, or discuss.
4. **Re-run tests** after any review-driven changes.
5. PRs are **squash-merged** into `main`.

---

## PR Checklist

Before requesting review, verify:

- [ ] Feature branch created from `main` with correct naming
- [ ] All new logic has unit tests
- [ ] `dotnet build` — 0 errors, 0 warnings
- [ ] `dotnet test` — all automated tests pass
- [ ] Documentation updated (if applicable)
- [ ] Issue linked in PR description (`Resolves #N`)

---

## Issue & PR Labels

| Label | Description |
|-------|-------------|
| `bug` | Something isn't working |
| `enhancement` | New feature or improvement |
| `documentation` | Documentation changes only |
| `refactor` | Code refactoring with no behavior change |
| `in-progress` | Work is actively being done |
| `help wanted` | Looking for contributors |
| `good first issue` | Good for newcomers |
