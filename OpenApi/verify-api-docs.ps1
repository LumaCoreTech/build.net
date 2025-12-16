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

$CommittedDocs = Join-Path $DocsDirectory "README.md"
$CommittedJson = Join-Path $DocsDirectory "openapi.json"
$TempDir = Join-Path ([System.IO.Path]::GetTempPath()) "openapi-verify-$(Get-Random)"
$TempJson = "$TempDir/openapi.json"
$TempMd = "$TempDir/README.md"

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

# Check if committed docs exist
if (!(Test-Path $CommittedDocs)) {
    Write-Err "Committed documentation not found: $CommittedDocs"
    Write-Err "Please generate the API documentation first."
    exit 1
}

if (!(Test-Path $CommittedJson)) {
    Write-Err "Committed OpenAPI spec not found: $CommittedJson"
    Write-Err "Please generate the API documentation first."
    exit 1
}

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

dotnet build $ApiProject `
    --configuration Release `
    /p:GenerateOpenApi=true `
    /p:OpenApiDocumentsDirectory=$TempDir `
    --verbosity quiet `
    --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Err "Build failed"
    Remove-TempDirectory
    exit 1
}

Write-Info "Build completed successfully"

# ============================================================================
# Verify OpenAPI JSON was generated
# ============================================================================

if (!(Test-Path $TempJson)) {
    Write-Err "OpenAPI JSON not generated at: $TempJson"
    Write-Err "Expected output from Microsoft.Extensions.ApiDescription.Server"
    Remove-TempDirectory
    exit 1
}

# ============================================================================
# Generate Fresh Documentation
# ============================================================================

Write-Info "Generating fresh Markdown documentation..."

dotnet run --project $ToolProject `
    --configuration Release `
    --no-build `
    -- `
    --input $TempJson `
    --output $TempMd `
    --code-samples $CodeSamples

if ($LASTEXITCODE -ne 0) {
    Write-Err "Failed to generate Markdown documentation"
    Remove-TempDirectory
    exit 1
}

if (!(Test-Path $TempMd)) {
    Write-Err "Markdown file not generated: $TempMd"
    Remove-TempDirectory
    exit 1
}

# ============================================================================
# Compare Committed vs. Fresh Documentation
# ============================================================================

Write-Info "Comparing committed documentation with fresh generation..."

# Compare JSON specs
$jsonDiff = Compare-Object -ReferenceObject (Get-Content $CommittedJson) -DifferenceObject (Get-Content $TempJson)

if ($jsonDiff) {
    Write-Err "OpenAPI specification is outdated!"
    Write-Host ""
    Write-Host "Differences in openapi.json:"
    $jsonDiff | Format-Table -AutoSize
    Write-Host ""
    Write-Err "Please regenerate the API documentation."
    Remove-TempDirectory
    exit 1
}

# Compare Markdown docs (ignore generated timestamp line)
$committedContent = Get-Content $CommittedDocs | Where-Object { $_ -notmatch '^\*Generated by .+OpenApiGen' }
$freshContent = Get-Content $TempMd | Where-Object { $_ -notmatch '^\*Generated by .+OpenApiGen' }

$mdDiff = Compare-Object -ReferenceObject $committedContent -DifferenceObject $freshContent

if ($mdDiff) {
    Write-Err "API documentation is outdated!"
    Write-Host ""
    Write-Host "Differences in README.md:"
    $mdDiff | Format-Table -AutoSize
    Write-Host ""
    Write-Err "Please regenerate the API documentation."
    Remove-TempDirectory
    exit 1
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
Write-Info "Both openapi.json and README.md match the current API surface."
