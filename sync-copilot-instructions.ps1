# sync-copilot-instructions.ps1
#
# Copies build.net/.github/instructions/base.instructions.md to the consuming
# repository's .github/instructions/ directory. Called automatically by the
# MSBuild target SyncCopilotInstructions in src/Directory.Build.targets.
#
# Parameters
#   -RepoRoot   Absolute path to the consuming repository root (passed by MSBuild).
#
# The script is intentionally simple: base.instructions.md is owned by build.net
# and is always overwritten. Repo-specific instructions live in a separate file
# (repo.instructions.md) that this script never touches.

param(
    [Parameter(Mandatory)]
    [string] $RepoRoot
)

$source      = Join-Path $PSScriptRoot '.github\instructions\base.instructions.md'
$targetDir   = Join-Path $RepoRoot '.github\instructions'
$destination = Join-Path $targetDir 'base.instructions.md'

if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir | Out-Null
}

Copy-Item -Path $source -Destination $destination -Force
