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

function ConvertTo-SortedJson {
    <#
    .SYNOPSIS
        Converts an object to JSON with keys sorted alphabetically (recursively).
    .DESCRIPTION
        JSON key order can differ between Windows and Linux due to dictionary
        serialization differences. This function normalizes JSON by sorting all
        object keys alphabetically, enabling reliable cross-platform comparison.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$JsonPath
    )
    
    # Parse JSON and recursively sort all object keys
    $content = Get-Content $JsonPath -Raw
    $obj = $content | ConvertFrom-Json -Depth 100
    
    function Sort-ObjectRecursively {
        param($InputObject)
        
        if ($null -eq $InputObject) {
            return $null
        }
        
        if ($InputObject -is [System.Collections.IList]) {
            # Array: sort each element recursively
            $result = @()
            foreach ($item in $InputObject) {
                $result += Sort-ObjectRecursively $item
            }
            return $result
        }
        
        if ($InputObject -is [PSCustomObject]) {
            # Object: sort keys and recurse into values
            $sorted = [ordered]@{}
            $InputObject.PSObject.Properties.Name | Sort-Object | ForEach-Object {
                $sorted[$_] = Sort-ObjectRecursively $InputObject.$_
            }
            return [PSCustomObject]$sorted
        }
        
        # Primitive value: return as-is
        return $InputObject
    }
    
    $sorted = Sort-ObjectRecursively $obj
    return $sorted | ConvertTo-Json -Depth 100 -Compress
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

# Extract version names from committed JSON files
$VersionNames = $CommittedJsonFiles | ForEach-Object { $_.BaseName }

# Use generate-openapi-json.ps1 for consistent normalization
# This ensures the same post-processing (empty arrays, whitespace fixes) is applied
# both during local generation and CI verification.
& "$ScriptDir/generate-openapi-json.ps1" `
    -ApiProject $ApiProject `
    -DocsDirectory $TempDir `
    -Versions $VersionNames

if ($LASTEXITCODE -ne 0) {
    Write-Err "Failed to generate OpenAPI specs"
    Remove-TempDirectory
    exit 1
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

    # Compare JSON specs (semantically, not line-by-line)
    # JSON key order can differ between Windows/Linux, so we normalize both files
    # by parsing and re-serializing with sorted keys before comparison.
    # Additional normalizations handle platform differences and .NET generator quirks.
    try {
        $committedNormalized = ConvertTo-SortedJson -JsonPath $CommittedJson
        $freshNormalized = ConvertTo-SortedJson -JsonPath $TempJson.FullName
        
        # Normalize escaped line endings in JSON strings (\r\n → \n)
        $committedNormalized = $committedNormalized -replace '\\r\\n', '\n'
        $freshNormalized = $freshNormalized -replace '\\r\\n', '\n'
        
        # Normalize empty arrays: [ ] → [] (System.Text.Json quirk)
        $committedNormalized = $committedNormalized -replace '\[ \]', '[]'
        $freshNormalized = $freshNormalized -replace '\[ \]', '[]'
        
        # Normalize excessive whitespace after newlines in JSON strings.
        # See: https://github.com/dotnet/aspnetcore/issues/62970
        $committedNormalized = $committedNormalized -replace '\\n\s{2,}', '\n'
        $freshNormalized = $freshNormalized -replace '\\n\s{2,}', '\n'
        
        # Also normalize actual line endings (CRLF → LF)
        $committedNormalized = $committedNormalized -replace "`r`n", "`n"
        $freshNormalized = $freshNormalized -replace "`r`n", "`n"
        
        $jsonDiff = $committedNormalized -ne $freshNormalized
    }
    catch {
        Write-Err "Failed to parse JSON for comparison: $_"
        Remove-TempDirectory
        exit 1
    }

    if ($jsonDiff) {
        Write-Err "OpenAPI specification $Version.json is outdated!"
        Write-Host ""
        Write-Host "The API surface has changed. Differences detected in $Version.json."
        Write-Host ""
        
        # Find first difference position for debugging
        $minLen = [Math]::Min($committedNormalized.Length, $freshNormalized.Length)
        $diffPos = -1
        for ($i = 0; $i -lt $minLen; $i++) {
            if ($committedNormalized[$i] -ne $freshNormalized[$i]) {
                $diffPos = $i
                break
            }
        }
        
        if ($diffPos -eq -1 -and $committedNormalized.Length -ne $freshNormalized.Length) {
            $diffPos = $minLen
            Write-Host "Length difference: Committed=$($committedNormalized.Length), Generated=$($freshNormalized.Length)" -ForegroundColor Yellow
        }
        
        if ($diffPos -ge 0) {
            $contextStart = [Math]::Max(0, $diffPos - 100)
            $contextEnd = [Math]::Min($minLen, $diffPos + 100)
            
            Write-Host "First difference at position $diffPos" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Committed (around diff):" -ForegroundColor Cyan
            Write-Host $committedNormalized.Substring($contextStart, [Math]::Min($contextEnd - $contextStart, $committedNormalized.Length - $contextStart))
            Write-Host ""
            Write-Host "Generated (around diff):" -ForegroundColor Cyan
            Write-Host $freshNormalized.Substring($contextStart, [Math]::Min($contextEnd - $contextStart, $freshNormalized.Length - $contextStart))
        }
        
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