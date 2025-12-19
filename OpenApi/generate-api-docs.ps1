# Copyright (c) 2025 LumaCoreTech
# SPDX-License-Identifier: MIT
# Project: https://github.com/LumaCoreTech/build.net

<#
.SYNOPSIS
    Generates GitHub-friendly Markdown API documentation from OpenAPI 3.x specification.

.DESCRIPTION
    This script generates static, GitHub-friendly Markdown documentation by:
      1. Building the OpenApiGen tool
      2. Converting OpenAPI JSON files to Markdown (with code samples)

    Supports OpenAPI 3.0, 3.1, and 3.2 specifications.
    Automatically processes all versioned API documents (v1.json, v2.json, etc.).

.PARAMETER DocsDirectory
    Directory containing the OpenAPI JSON files and where the Markdown will be written.
    Expects 'v1.json' (and optionally v2.json, etc.) and writes 'v1.md', 'v2.md', etc.
    Defaults to '<Repo>/docs/api'.

.PARAMETER CodeSamples
    Languages for code samples (comma-separated). Defaults to 'shell,csharp'.

.EXAMPLE
    .\build.net\OpenApi\generate-api-docs.ps1

.EXAMPLE
    .\build.net\OpenApi\generate-api-docs.ps1 -DocsDirectory docs/api-reference

.NOTES
    Requires: .NET SDK 8.0+, OpenAPI 3.x JSON file(s)
#>

#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(HelpMessage = "Directory containing v1.json, v2.json, etc. and where Markdown will be written")]
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

# Resolve path relative to repo root if not absolute
if (![System.IO.Path]::IsPathRooted($DocsDirectory)) {
    $DocsDirectory = Join-Path $RepoRoot $DocsDirectory
}

# Note: Input/output paths are determined dynamically per version in the generation loop below

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

# ============================================================================
# Pre-flight Checks
# ============================================================================

Write-Info "Checking prerequisites..."

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

Write-Info ".NET SDK version: $dotnetVersion"

# Check if tool project exists
if (!(Test-Path $ToolProject)) {
    Write-Err "OpenApiGen tool not found at: $ToolProject"
    exit 1
}

# Ensure output directory exists
New-Item -ItemType Directory -Force -Path $DocsDirectory | Out-Null

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
    exit 1
}

Write-Info "Tool built successfully"

# ============================================================================
# Find OpenAPI JSON Files
# ============================================================================

Write-Info "Scanning for OpenAPI specifications..."

$JsonFiles = Get-ChildItem -Path $DocsDirectory -Filter "v*.json" -File | Sort-Object Name

if ($JsonFiles.Count -eq 0) {
    Write-Err "No OpenAPI JSON files found (v1.json, v2.json, etc.) in: $DocsDirectory"
    Write-Err "Run 'dotnet build /p:GenerateOpenApi=true' first to generate the OpenAPI specs."
    exit 1
}

Write-Info "Found $($JsonFiles.Count) OpenAPI specification(s):"
foreach ($file in $JsonFiles) {
    Write-Host "  - $($file.Name)"
}

# ============================================================================
# Generate Markdown Documentation (for each version)
# ============================================================================

$GeneratedCount = 0

foreach ($JsonFile in $JsonFiles) {
    $Version = $JsonFile.BaseName  # e.g., "v1" from "v1.json"
    $InputJson = $JsonFile.FullName
    $OutputMarkdown = Join-Path $DocsDirectory "$Version.md"

    Write-Info "Generating documentation for $Version..."

    dotnet run --project $ToolProject `
        --configuration Release `
        --no-build `
        -- `
        --input $InputJson `
        --output $OutputMarkdown `
        --code-samples $CodeSamples

    if ($LASTEXITCODE -ne 0) {
        Write-Err "Failed to generate Markdown documentation for $Version"
        exit 1
    }

    if (!(Test-Path $OutputMarkdown)) {
        Write-Err "Markdown file not generated: $OutputMarkdown"
        exit 1
    }

    $GeneratedCount++
    Write-Info "Generated: $OutputMarkdown"
}

# ============================================================================
# Success
# ============================================================================

Write-Host ""
Write-Info "API documentation generated successfully!"
Write-Host ""
Write-Host "  Directory: $DocsDirectory"
Write-Host "  Generated: $GeneratedCount file(s)"
foreach ($JsonFile in $JsonFiles) {
    $Version = $JsonFile.BaseName
    Write-Host "    - $Version.json -> $Version.md"
}
