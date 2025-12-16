# OpenAPI Documentation Tools

PowerShell scripts for generating and verifying OpenAPI documentation in CI/CD pipelines.

## Overview

These tools convert OpenAPI 3.x specifications (JSON) into GitHub-friendly Markdown documentation. The generated Markdown includes collapsible sections, code samples, and schema documentation.

**Supported OpenAPI versions:** 3.0, 3.1, 3.2

## Scripts

### generate-api-docs.ps1

Converts an existing OpenAPI JSON file to Markdown documentation.

**Use case:** Run this after your OpenAPI JSON has been generated (e.g., by MSBuild or at runtime) to create human-readable documentation.

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `-DocsDirectory` | No | `<Repo>/docs/api` | Directory containing `openapi.json` where `README.md` will be written |
| `-CodeSamples` | No | `shell,csharp` | Comma-separated list of languages for code samples |

> **Note:** Relative paths are resolved from the repository root. Absolute paths are used as-is.

**Examples:**

```powershell
# Use defaults (<Repo>/docs/api)
pwsh ./build.net/OpenApi/generate-api-docs.ps1

# Custom directory
pwsh ./build.net/OpenApi/generate-api-docs.ps1 -DocsDirectory docs/api-reference

# Include more code sample languages
pwsh ./build.net/OpenApi/generate-api-docs.ps1 -CodeSamples "shell,csharp,javascript,python"
```

### verify-api-docs.ps1

Verifies that committed documentation matches the current API surface. Designed for CI pipelines to catch outdated documentation.

**How it works:**
1. Builds your API project with OpenAPI generation enabled
2. Generates fresh Markdown in a temporary directory
3. Compares the fresh output against committed files
4. Exits with code 1 if differences are found

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `-ApiProject` | **Yes** | — | Path to the API project file (.csproj) |
| `-DocsDirectory` | No | `<Repo>/docs/api` | Directory containing committed `openapi.json` and `README.md` |
| `-CodeSamples` | No | `shell,csharp` | Comma-separated list of languages for code samples |

> **Note:** Relative paths are resolved from the repository root. Absolute paths are used as-is.

**Examples:**

```powershell
# Basic usage
pwsh ./build.net/OpenApi/verify-api-docs.ps1 `
    -ApiProject src/MyApi/MyApi.csproj

# Custom documentation directory
pwsh ./build.net/OpenApi/verify-api-docs.ps1 `
    -ApiProject src/MyApi/MyApi.csproj `
    -DocsDirectory docs/api-reference
```

**CI Integration (GitHub Actions):**

```yaml
- name: Verify API Documentation
  run: pwsh -File ./build.net/OpenApi/verify-api-docs.ps1 -ApiProject src/MyApi/MyApi.csproj
```

## Requirements

- .NET SDK 8.0 or later
- PowerShell Core 7.0+ (`pwsh`)
- API project must have `Microsoft.Extensions.ApiDescription.Server` configured for build-time OpenAPI generation

## How It Works

The scripts use **LumaCore.OpenApiGen**, a .NET tool located in `../BuildTools/`, to parse OpenAPI JSON and generate Markdown. The tool is built on-demand when the scripts run.

For build-time OpenAPI generation, the verify script sets `ASPNETCORE_ENVIRONMENT=Development` because production configurations often require secrets that aren't available in CI environments.
