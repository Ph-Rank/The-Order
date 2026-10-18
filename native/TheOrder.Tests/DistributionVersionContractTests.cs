using System.Diagnostics;
using TheOrder.Application;
using Xunit;

namespace TheOrder.Tests;

[Collection("Native profile resource")]
public sealed class DistributionVersionContractTests
{
    [Fact]
    public async Task CompiledPayloadMatchesPublishedWorkerVersionProtocolAndStableOrdersAfterBeta()
    {
        // Public workers validate the installed app as well as the Setup resource.
        // A larger arbitrary payload revision would report failure after installation.
        var version = AppReleaseIdentity.Version;
        var file = FileVersionInfo.GetVersionInfo(typeof(AppUpdateVersion).Assembly.Location).FileVersion!;
        Assert.True(AppUpdateVersion.TryParse(version, out var parsed));
        Assert.Equal(parsed.FileVersion, Version.Parse(file));
        using var fixture = new ProfileFixture();
        var script = Path.Combine(fixture.Parent, "version-protocol.ps1");
        File.WriteAllText(script, """
param([string]$Source,[string]$Version,[string]$FileVersion)
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile($Source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw 'WorkerParseFailure'}
foreach($name in @('ReleaseVersion','CanApply')){
 $node=$ast.Find({param($a) $a -is [Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq $name},$true)
 if(!$node){throw 'MissingFunction'}
 . ([scriptblock]::Create($node.Extent.Text))
}
$offered=ReleaseVersion $Version
if($offered.file -ne $FileVersion.Trim()){throw 'InstalledVersionMismatch'}
if(!(CanApply (ReleaseVersion '1.2.0-beta.4') (ReleaseVersion '1.2.0') 1)){throw 'StableNotAfterBeta'}
if(CanApply (ReleaseVersion '1.2.0') (ReleaseVersion '1.2.0-beta.4') 0){throw 'StableSilentlyDowngradedToBeta'}
""");
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Source", Path.Combine(ReferenceSources.Repository(), "native/scripts/update-worker.ps1"), "-Version", version, "-FileVersion", file }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); } catch { process.Kill(true); throw; }
        Assert.True(process.ExitCode == 0, await output + await error);
    }
}
