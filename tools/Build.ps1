param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
Push-Location $projectRoot
try {
    dotnet build .\DeltaMarketAssistant.sln --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Solution build failed.' }

    dotnet run --project .\tests\DeltaMarketAssistant.Tests\DeltaMarketAssistant.Tests.csproj `
        --configuration $Configuration --no-build -- --countdown-samples .\countdown-samples
    if ($LASTEXITCODE -ne 0) { throw 'Portable regression tests failed.' }

    if ($Publish) {
        [xml]$project = Get-Content .\src\DeltaMarketAssistant\DeltaMarketAssistant.csproj
        $version = [string]$project.Project.PropertyGroup.Version
        if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Project version must be MAJOR.MINOR.PATCH.' }
        $outputPath = Join-Path $projectRoot "artifacts\publish\win-x64\v$version"
        dotnet publish .\src\DeltaMarketAssistant\DeltaMarketAssistant.csproj `
            --configuration $Configuration --runtime win-x64 --self-contained true --no-restore `
            --output $outputPath
        if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
        Write-Output "Published application: $outputPath"
    }
}
finally {
    Pop-Location
}
