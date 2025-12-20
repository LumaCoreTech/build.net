# Copyright (c) 2025 LumaCoreTech
# SPDX-License-Identifier: MIT
# Project: https://github.com/LumaCoreTech/build.net

<#
.SYNOPSIS
    Generates OpenAPI JSON files for all API versions.

.DESCRIPTION
    This script generates OpenAPI JSON specifications for each registered API version.
    It builds the API project multiple times, once per version, to produce consistently
    named output files (v1.json, v2.json, etc.).

    The Microsoft.Extensions.ApiDescription.Server package has inconsistent naming:
    - First/default version: v1.json (correct)
    - Additional versions: v2_v2.json (duplicates the version suffix)

    This script works around that by detecting the quirky names and renaming them
    to the expected format (v2.json, v3.json, etc.).

.PARAMETER ApiProject
    Path to the API project file (.csproj). Defaults to 'src/LumaCore.Api/LumaCore.Api.csproj'.

.PARAMETER DocsDirectory
    Directory where OpenAPI JSON files will be written. Defaults to 'docs/api'.

.PARAMETER Versions
    Array of API versions to generate. Defaults to @('v1').
    Update this when adding new API versions.

.EXAMPLE
    .\build.net\OpenApi\generate-openapi-json.ps1

.EXAMPLE
    .\build.net\OpenApi\generate-openapi-json.ps1 -Versions @('v1', 'v2')

.NOTES
    After running this script, run generate-api-docs.ps1 to create Markdown documentation.
#>

#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(HelpMessage = "Path to the API project file (.csproj)")]
    [string]$ApiProject = "src/LumaCore.Api/LumaCore.Api.csproj",

    [Parameter(HelpMessage = "Directory where OpenAPI JSON files will be written")]
    [string]$DocsDirectory = "docs/api",

    [Parameter(HelpMessage = "Array of API versions to generate")]
    [string[]]$Versions = @('v1')
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

Write-Info "Generating OpenAPI JSON files..."

# Check if .NET is installed
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Err ".NET SDK not found. Please install .NET 8.0 SDK or later."
    exit 1
}

# Check if API project exists
if (!(Test-Path $ApiProject)) {
    Write-Err "API project not found at: $ApiProject"
    exit 1
}

# Ensure output directory exists
New-Item -ItemType Directory -Force -Path $DocsDirectory | Out-Null

Write-Info "API Project: $ApiProject"
Write-Info "Output Directory: $DocsDirectory"
Write-Info "Versions: $($Versions -join ', ')"

# ============================================================================
# Generate OpenAPI JSON for each version
# ============================================================================

$GeneratedCount = 0

foreach ($Version in $Versions) {
    Write-Info "Generating $Version.json..."

    $OutputFile = Join-Path $DocsDirectory "$Version.json"

    # Build with specific document-name and file-name to get consistent output
    # ASPNETCORE_ENVIRONMENT=Development required for OpenAPI endpoint exposure
    # UseSharedCompilation=false bypasses the MSBuild server cache to ensure
    # our /p: properties are respected (avoids "global property cannot be changed" issue)
    # --no-incremental forces a full rebuild because OpenAPI generation happens at
    # build time - without it, subsequent versions are skipped as "up-to-date"
    $env:ASPNETCORE_ENVIRONMENT = "Development"

    dotnet build $ApiProject `
        --configuration Release `
        --no-incremental `
        /p:UseSharedCompilation=false `
        /p:OpenApiGenerateDocuments=true `
        "/p:OpenApiDocumentsDirectory=$DocsDirectory" `
        "/p:OpenApiGenerateDocumentsOptions=--document-name $Version --file-name $Version" `
        --verbosity quiet `
        --nologo

    if ($LASTEXITCODE -ne 0) {
        Write-Err "Failed to generate OpenAPI JSON for $Version"
        exit 1
    }

    # Microsoft's naming is inconsistent: v1 → v1.json, but v2 → v2_v2.json
    # Check for the quirky name and rename if needed
    $QuirkyFile = Join-Path $DocsDirectory "$Version`_$Version.json"
    if ((Test-Path $QuirkyFile) -and !(Test-Path $OutputFile)) {
        Move-Item $QuirkyFile $OutputFile -Force
    }

    if (!(Test-Path $OutputFile)) {
        Write-Err "OpenAPI JSON not generated: $OutputFile"
        exit 1
    }

    # Normalize line endings in the generated JSON
    # On Windows, Git's autocrlf may cause the C# source files to have CRLF
    # in the working copy, which gets embedded in XMLDoc descriptions.
    # These appear as escaped \r\n sequences in JSON strings.
    # This ensures consistent output regardless of platform.
    $jsonContent = Get-Content $OutputFile -Raw
    # Replace escaped CRLF in JSON strings (\r\n → \n)
    $jsonContent = $jsonContent -replace '\\r\\n', '\n'
    # Normalize empty arrays: [ ] → [] (System.Text.Json quirk)
    $jsonContent = $jsonContent -replace '\[ \]', '[]'
    # Normalize excessive whitespace after newlines in JSON strings.
    # The .NET OpenAPI generator incorrectly preserves indentation from multi-line
    # XML documentation comments, resulting in strings like "text;\n            more text".
    # See: https://github.com/dotnet/aspnetcore/issues/62970
    $jsonContent = $jsonContent -replace '\\n\s{2,}', '\n'
    # Also normalize actual file line endings (CRLF → LF)
    $jsonContent = $jsonContent -replace "`r`n", "`n"
    # Use .NET to write without BOM and with LF endings
    [System.IO.File]::WriteAllText($OutputFile, $jsonContent)

    $GeneratedCount++
    Write-Info "Generated: $OutputFile"
}

# ============================================================================
# Success
# ============================================================================

Write-Host ""
Write-Info "OpenAPI JSON generation complete!"
Write-Host ""
Write-Host "  Directory: $DocsDirectory"
Write-Host "  Generated: $GeneratedCount file(s)"
foreach ($Version in $Versions) {
    Write-Host "    - $Version.json"
}
Write-Host ""
Write-Info "Next step: Run generate-api-docs.ps1 to create Markdown documentation."