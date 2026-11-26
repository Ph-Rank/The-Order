param([Parameter(Mandatory)][string]$OutputDirectory)

& (Join-Path $PSScriptRoot 'test-guide-fixture.ps1') -Fixture GuidePresentationTests -OutputDirectory $OutputDirectory
