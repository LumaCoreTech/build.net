# Copyright (c) 2025 LumaCoreTech
# SPDX-License-Identifier: MIT
# Project: https://github.com/LumaCoreTech/build.net

<#
.SYNOPSIS
    Verifies that the committed API documentation is up-to-date (CI).

.DESCRIPTION
    This script verifies that the committed API documentation matches the current
    API surface by generating fresh documentation in a temporary directory and
    comparing it with the checked-in version.
    
    The OpenAPI JSON is generated at build-time (no application startup required).
    Supports OpenAPI 3.0, 3.1, and 3.2 specifications.
    Automatically verifies all versioned API documents (v1.json, v2.json, etc.).

    Exit codes:
      0 - Documentation is up-to-date
      1 - Documentation is outdated or verification failed

.PARAMETER ApiProject
    Path to the API project file (.csproj). Required.

.PARAMETER DocsDirectory
    Path to the directory containing the committed documentation.
    Defaults to '<Repo>/docs/api'.

.PARAMETER CodeSamples
    Languages for code samples (comma-separated). Defaults to 'shell,csharp'.

.EXAMPLE
    .\build.net\OpenApi\verify-api-docs.ps1 -ApiProject src/MyApi/MyApi.csproj

.EXAMPLE
    .\build.net\OpenApi\verify-api-docs.ps1 -ApiProject src/MyApi/MyApi.csproj -DocsDirectory docs/api

.NOTES
    If verification fails, regenerate the documentation and commit the changes.
#>

#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, HelpMessage = "Path to the API project file (.csproj)")]
    [string]$ApiProject,

    [Parameter(HelpMessage = "Path to the documentation directory")]
    [string]$DocsDirectory = "docs/api",

    [Parameter(HelpMessage = "Languages for code samples (comma-separated)")]
    [string]$CodeSamples = "shell,csharp"
)

$ErrorActionPreference = 'Stop'

# ============================================================================
# Configuration
# ============================================================================

$ScriptDir = $PSScriptRoot
$RepoRoot = (Resolve-Path "$ScriptDir/../..").Path

# Resolve paths relative to repo root if not absolute
if (![System.IO.Path]::IsPathRooted($ApiProject)) {
    $ApiProject = Join-Path $RepoRoot $ApiProject
}
if (![System.IO.Path]::IsPathRooted($DocsDirectory)) {
    $DocsDirectory = Join-Path $RepoRoot $DocsDirectory
}

$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) "openapi-verify-$(Get-Random)"

# Note: Individual file paths are determined dynamically per version

# Build tool configuration
$ToolProject = "$ScriptDir/../BuildTools/src/LumaCore.OpenApiGen/LumaCore.OpenApiGen.csproj"

# ============================================================================
# Helper Functions
# ============================================================================

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Write-Err {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

function Remove-TempDirectory {
    if (Test-Path $TempDir) {
        Remove-Item -Path $TempDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Ensure cleanup on script exit
trap {
    Remove-TempDirectory
    throw
}

# ============================================================================
# Pre-flight Checks
# ============================================================================

Write-Info "Verifying API documentation is up-to-date..."

# Check if any committed versioned docs exist
$CommittedJsonFiles = Get-ChildItem -Path $DocsDirectory -Filter "v*.json" -File -ErrorAction SilentlyContinue

if ($CommittedJsonFiles.Count -eq 0) {
    Write-Err "No committed OpenAPI specs found (v1.json, v2.json, etc.) in: $DocsDirectory"
    Write-Err "Please generate the API documentation first."
    exit 1
}

Write-Info "Found $($CommittedJsonFiles.Count) committed OpenAPI spec(s)"

# Check if .NET is installed
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Err ".NET SDK not found. Please install .NET 8.0 SDK or later."
    exit 1
}

# Verify .NET version (need at least 8.0 for the tool)
$dotnetVersion = dotnet --version
$majorVersion = [int]($dotnetVersion -split '\.')[0]
if ($majorVersion -lt 8) {
    Write-Err ".NET 8.0 or later required. Found: $dotnetVersion"
    exit 1
}

# Check if API project exists
if (!(Test-Path $ApiProject)) {
    Write-Err "API project not found at: $ApiProject"
    exit 1
}

# Check if tool project exists
if (!(Test-Path $ToolProject)) {
    Write-Err "OpenApiGen tool not found at: $ToolProject"
    exit 1
}

# Create temp directory
New-Item -ItemType Directory -Force -Path $TempDir | Out-Null

# ============================================================================
# Build OpenApiGen Tool
# ============================================================================

Write-Info "Building OpenApiGen tool..."

dotnet build $ToolProject `
    --configuration Release `
    --verbosity quiet `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Err "Failed to build OpenApiGen tool"
    Remove-TempDirectory
    exit 1
}

Write-Info "Tool built successfully"

# ============================================================================
# Build Application with OpenAPI Generation
# ============================================================================

Write-Info "Building API with OpenAPI document generation..."

# IMPORTANT: Development environment required because Production mode
# may expect secrets that aren't available during build/CI
$env:ASPNETCORE_ENVIRONMENT = "Development"

# Generate OpenAPI JSON for each committed version
foreach ($CommittedJson in $CommittedJsonFiles) {
    $Version = $CommittedJson.BaseName  # e.g., "v1" from "v1.json"
    Write-Info "Generating $Version.json..."

    dotnet build $ApiProject `
        --configuration Release `
        --no-incremental `
        /p:UseSharedCompilation=false `
        /p:OpenApiGenerateDocuments=true `
        "/p:OpenApiDocumentsDirectory=$TempDir" `
        "/p:OpenApiGenerateDocumentsOptions=--document-name $Version --file-name $Version" `
        --verbosity quiet `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        Write-Err "Build failed for $Version"
        Remove-TempDirectory
        exit 1
    }

    # Microsoft's naming is inconsistent: v1 → v1.json, but v2 → v2_v2.json
    # Check for the quirky name and rename if needed
    $ExpectedFile = Join-Path $TempDir "$Version.json"
    $QuirkyFile = Join-Path $TempDir "$Version`_$Version.json"
    if ((Test-Path $QuirkyFile) -and !(Test-Path $ExpectedFile)) {
        Move-Item $QuirkyFile $ExpectedFile -Force
    }
}

Write-Info "Build completed successfully"

# ============================================================================
# Verify OpenAPI JSON was generated
# ============================================================================

$TempJsonFiles = Get-ChildItem -Path $TempDir -Filter "v*.json" -File -ErrorAction SilentlyContinue

if ($TempJsonFiles.Count -eq 0) {
    Write-Err "No OpenAPI JSON files generated in: $TempDir"
    Write-Err "Expected output from Microsoft.Extensions.ApiDescription.Server"
    Remove-TempDirectory
    exit 1
}

Write-Info "Generated $($TempJsonFiles.Count) OpenAPI spec(s)"

# ============================================================================
# Generate Fresh Documentation & Compare (for each version)
# ============================================================================

Write-Info "Generating fresh Markdown documentation and comparing..."

foreach ($TempJson in $TempJsonFiles) {
    $Version = $TempJson.BaseName  # e.g., "v1" from "v1.json"
    $TempMd = Join-Path $TempDir "$Version.md"
    $CommittedJson = Join-Path $DocsDirectory "$Version.json"
    $CommittedMd = Join-Path $DocsDirectory "$Version.md"

    Write-Info "Processing $Version..."

    # Check if committed version exists
    if (!(Test-Path $CommittedJson)) {
        Write-Err "Committed OpenAPI spec not found: $CommittedJson"
        Write-Err "New API version detected. Please commit the documentation."
        Remove-TempDirectory
        exit 1
    }

    if (!(Test-Path $CommittedMd)) {
        Write-Err "Committed Markdown not found: $CommittedMd"
        Write-Err "Please regenerate the API documentation."
        Remove-TempDirectory
        exit 1
    }

    # Generate Markdown
    dotnet run --project $ToolProject `
        --configuration Release `
        --no-build `
        -- `
        --input $TempJson.FullName `
        --output $TempMd `
        --code-samples $CodeSamples

    if ($LASTEXITCODE -ne 0) {
        Write-Err "Failed to generate Markdown documentation for $Version"
        Remove-TempDirectory
        exit 1
    }

    if (!(Test-Path $TempMd)) {
        Write-Err "Markdown file not generated: $TempMd"
        Remove-TempDirectory
        exit 1
    }

    # Compare JSON specs
    $jsonDiff = Compare-Object -ReferenceObject (Get-Content $CommittedJson) -DifferenceObject (Get-Content $TempJson.FullName)

    if ($jsonDiff) {
        Write-Err "OpenAPI specification $Version.json is outdated!"
        Write-Host ""
        Write-Host "Differences in $Version.json:"
        $jsonDiff | Format-Table -AutoSize
        Write-Host ""
        Write-Err "Please regenerate the API documentation."
        Remove-TempDirectory
        exit 1
    }

    # Compare Markdown docs (ignore generated timestamp line)
    $committedContent = Get-Content $CommittedMd | Where-Object { $_ -notmatch '^\*Generated by .+OpenApiGen' }
    $freshContent = Get-Content $TempMd | Where-Object { $_ -notmatch '^\*Generated by .+OpenApiGen' }

    $mdDiff = Compare-Object -ReferenceObject $committedContent -DifferenceObject $freshContent

    if ($mdDiff) {
        Write-Err "API documentation $Version.md is outdated!"
        Write-Host ""
        Write-Host "Differences in $Version.md:"
        $mdDiff | Format-Table -AutoSize
        Write-Host ""
        Write-Err "Please regenerate the API documentation."
        Remove-TempDirectory
        exit 1
    }

    Write-Info "$Version documentation is up-to-date"
}

# ============================================================================
# Cleanup
# ============================================================================

Remove-TempDirectory

# ============================================================================
# Success
# ============================================================================

Write-Host ""
Write-Info "API documentation is up-to-date!"
Write-Host ""
Write-Info "All $($CommittedJsonFiles.Count) API version(s) verified successfully."
