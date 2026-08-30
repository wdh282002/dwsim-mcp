#Requires -Version 5.1
<#
.SYNOPSIS
    Builds dwsim-mcp (the DWSIM Model Context Protocol server) as a self-contained
    Windows x64 executable.

.DESCRIPTION
    Two modes:

      A. Standalone (default) - build against an already-built DWSIM binary directory:

             .\scripts\build.ps1 -DwsimBinDir D:\DWSIM\dwsim-mcp

         The directory must contain DWSIM.Automation.dll and friends. An extracted
         DWSIM installation or a previously published dwsim-mcp folder both work.

      B. In-tree - run from a DWSIM source checkout:

             .\scripts\build.ps1 -InTree -DwsimSrc D:\src\dwsim10

    The output lands in .\dist and is directly runnable: it carries the .NET runtime,
    the DWSIM engine assemblies, the compound databases and the localised strings.

.PARAMETER DwsimBinDir
    Prebuilt DWSIM binary directory (mode A).

.PARAMETER InTree
    Build inside a DWSIM source tree using ProjectReference (mode B).

.PARAMETER DwsimSrc
    Root of the DWSIM source checkout, used with -InTree.

.PARAMETER Configuration
    Release (default) or Debug.

.PARAMETER Runtime
    Target RID. Default win-x64. linux-x64 and osx-arm64 also work in mode B.

.PARAMETER OutDir
    Output directory. Default .\dist
#>
[CmdletBinding()]
param(
    [string] $DwsimBinDir = $env:DWSIM_BIN_DIR,
    [switch] $InTree,
    [string] $DwsimSrc = $env:DWSIM_SRC,
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $OutDir = (Join-Path $PSScriptRoot "..\dist")
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project  = Join-Path $RepoRoot "src\DWSIM.MCPServer.csproj"
$OutDir   = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutDir))

if (-not (Test-Path $Project)) { throw "Project not found: $Project" }

# Locate dotnet: honour $env:DOTNET_ROOT, else the one on PATH, else the default install.
$dotnet = $null
if ($env:DOTNET_ROOT) {
    $candidate = Join-Path $env:DOTNET_ROOT "dotnet.exe"
    if (Test-Path $candidate) { $dotnet = $candidate }
}
if (-not $dotnet) {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($cmd) { $dotnet = $cmd.Source }
}
if (-not $dotnet) { throw "dotnet SDK not found. Install .NET 10 SDK or set `$env:DOTNET_ROOT." }

Write-Host "dotnet : $dotnet"
Write-Host "project: $Project"

# ---------------------------------------------------------------- mode B: in-tree
if ($InTree) {
    if (-not $DwsimSrc) { throw "-InTree needs -DwsimSrc (or `$env:DWSIM_SRC)." }
    $DwsimSrc = [System.IO.Path]::GetFullPath($DwsimSrc)
    $toolsDir = Join-Path $DwsimSrc "tools"
    if (-not (Test-Path $toolsDir)) { throw "Not a DWSIM source tree: $DwsimSrc" }

    $target = Join-Path $DwsimSrc "tools\DWSIM.MCPServer"
    Write-Host "Building in-tree at $target"
    if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
    Copy-Item (Join-Path $RepoRoot "src\*") $target -Recurse -Force

    Push-Location $target
    try {
        & $dotnet build -c $Configuration -r $Runtime --self-contained true
        if ($LASTEXITCODE -ne 0) { throw "build failed" }
        $built = Join-Path $target "bin\$Configuration\net10.0\$Runtime"
    }
    finally { Pop-Location }

    Write-Host "Copying output to $OutDir"
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
    Copy-Item (Join-Path $built "*") $OutDir -Recurse -Force
}
# ---------------------------------------------------------------- mode A: standalone
else {
    if (-not $DwsimBinDir) {
        throw "Specify -DwsimBinDir (or `$env:DWSIM_BIN_DIR) pointing at a built DWSIM binary directory."
    }
    $DwsimBinDir = [System.IO.Path]::GetFullPath($DwsimBinDir)
    if (-not (Test-Path (Join-Path $DwsimBinDir "DWSIM.Automation.dll"))) {
        throw "DWSIM.Automation.dll not found in $DwsimBinDir"
    }

    $objDir = Join-Path $RepoRoot "artifacts\obj"
    $binDir = Join-Path $RepoRoot "artifacts\bin"
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null

    # Antivirus scanners and stale MSBuild nodes sometimes hold the intermediate
    # files, which fails the build. Retry once with a pristine intermediate directory.
    function Invoke-Publish([string] $intermediateDir) {
        New-Item -ItemType Directory -Path $intermediateDir -Force | Out-Null
        & $dotnet publish $Project `
            -c $Configuration -r $Runtime --self-contained true `
            -p:DWSIM_BIN_DIR="$DwsimBinDir" `
            -p:BaseIntermediateOutputPath="$intermediateDir\" `
            -p:BaseOutputPath="$binDir\" `
            -p:PublishDir="$OutDir\"
        return $LASTEXITCODE
    }

    Write-Host "Publishing (self-contained $Runtime) ..."
    $code = Invoke-Publish $objDir
    if ($code -ne 0) {
        Write-Host "publish failed - retrying with a clean intermediate directory"
        $code = Invoke-Publish (Join-Path $RepoRoot ("artifacts\obj-" + [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()))
        if ($code -ne 0) { throw "publish failed" }
    }

    # The engine needs its data files next to the assemblies: compound databases
    # (addcomps), localised strings (de/en/es/it) and the CoolProp native library.
    Write-Host "Copying DWSIM runtime data from $DwsimBinDir"
    $exclude = @("*.pdb", "*.xml", "dwsim-mcp.*")
    Get-ChildItem -Path $DwsimBinDir -Recurse -File -Exclude $exclude | ForEach-Object {
        $rel = $_.FullName.Substring($DwsimBinDir.Length).TrimStart('\', '/')
        $dst = Join-Path $OutDir $rel
        $dstDir = Split-Path -Parent $dst
        if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
        Copy-Item $_.FullName $dst -Force
    }
}

$exe = Join-Path $OutDir "dwsim-mcp.exe"
if (-not (Test-Path $exe)) { $exe = Join-Path $OutDir "dwsim-mcp" }
if (-not (Test-Path $exe)) { throw "Build produced no executable in $OutDir" }

Write-Host ""
Write-Host "Built: $exe" -ForegroundColor Green
Write-Host ""
Write-Host "Register it with your MCP client, for example in ~/.workbuddy/mcp.json:"
$escaped = $exe -replace '\\', '\\'
Write-Host @"
  {
    "mcpServers": {
      "dwsim": {
        "command": "$escaped",
        "args": ["--stdio"]
      }
    }
  }
"@

Write-Host ""
Write-Host "Smoke test:"
Write-Host "  python scripts\smoke_test.py `"$exe`""
