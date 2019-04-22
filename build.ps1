<#
.SYNOPSIS
Builds all assets in this repository.
.PARAMETER Configuration
The project configuration to build.
#>
[CmdletBinding(SupportsShouldProcess)]
Param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration,

    [Parameter()]
    [ValidateSet('minimal', 'normal', 'detailed', 'diagnostic')]
    [string]$MsBuildVerbosity = 'minimal'
)

$msbuildCommandLine = "dotnet build `"$PSScriptRoot\src\Nerdbank.GitVersioning.sln`" /m /verbosity:$MsBuildVerbosity /nologo /p:Platform=`"Any CPU`""
$msbuildPack = "dotnet pack --no-build `"$PSScriptRoot\src\Nerdbank.GitVersioning.sln`" -o bin"
$msbuildPublish = "dotnet publish --no-build .\src\nbgv\nbgv.csproj -f netcoreapp2.1 -o .\src\nerdbank-gitversioning.npm\out\nbgv.cli\tools\netcoreapp2.1\any"

if (Test-Path "C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll") {
    $msbuildCommandLine += " /logger:`"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll`""
}

if ($Configuration) {
    $msbuildCommandLine += " /p:Configuration=$Configuration"
    $msbuildPack += " /p:Configuration=$Configuration"
    $msbuildPublish += " /p:Configuration=$Configuration"
}

Push-Location .
try {
    if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src\Nerdbank.GitVersioning.sln", "msbuild")) {
        Invoke-Expression $msbuildCommandLine
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed"
        }
        Invoke-Expression $msbuildPack
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet pack failed"
        }
        Invoke-Expression $msbuildPublish
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed"
        }
    }
    
    if ($PSCmdlet.ShouldProcess("$PSScriptRoot\src\nerdbank-gitversioning.npm", "gulp")) {
        cd "$PSScriptRoot\src\nerdbank-gitversioning.npm"
        yarn run build
        if ($LASTEXITCODE -ne 0) {
            throw "Node build failed"
        }
    }
} catch {
    Write-Error "Build failure"
    # we have the try so that PS fails when we get failure exit codes from build steps.
    throw;
} finally {
    Pop-Location
}
