#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$output=[IO.Path]::GetFullPath($OutputDirectory)
if([IO.Path]::GetFileName($exe) -ne 'TheOrder.WinUI.exe'){throw 'Expected the complete native build entry point.'}
if(Test-Path -LiteralPath $output){throw 'Use a new output directory to preserve earlier evidence.'}
if([IO.Path]::GetPathRoot($output) -ne 'D:\'){throw 'Keep generated Test profiles and evidence on D:.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
$results=[Collections.Generic.List[object]]::new()

function Assert-That([bool]$Condition,[string]$Message){if(!$Condition){throw $Message}}
function Invoke-Check($Process,[string]$Control,[string]$Action,[hashtable]$Payload=@{}){
    Assert-That (!$Process.HasExited) 'Test process exited before responding.'
    $request=@{}+$Payload
    $request.action=$Action; $request.nonce=[guid]::NewGuid().ToString('N')
    $pending=Join-Path $Control 'pending.json'
    $request|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $pending -Encoding utf8
    Move-Item -LiteralPath $pending -Destination (Join-Path $Control 'request.json') -Force
    if($Action -eq 'exit'){return}
    $deadline=[DateTime]::UtcNow.AddSeconds(25)
    while([DateTime]::UtcNow -lt $deadline){
        Assert-That (!$Process.HasExited) 'Test process exited before responding.'
        $receipt=$null
        try{$receipt=Get-Content -LiteralPath (Join-Path $Control 'receipt.json') -Raw -ErrorAction Stop|ConvertFrom-Json}catch{}
        if($receipt -and $receipt.nonce -eq $request.nonce){
            $receipt|ConvertTo-Json -Depth 40|Set-Content -LiteralPath (Join-Path $Control ($request.nonce+'.json')) -Encoding utf8
            Assert-That ($receipt.success -eq $true) ('Native fixture failed: '+$receipt.error)
            return $receipt.evidence
        }
        Start-Sleep -Milliseconds 100
    }
    throw ('No matching receipt for '+$Action+'; do not treat this as passing.')
}
function Assert-Ready($Evidence){
    Assert-That ($Evidence.startup.ready -and !$Evidence.startup.failed -and $Evidence.startup.libraryLoaded -and $Evidence.startup.sessionsReady) 'Startup did not complete with a ready library and session service.'
    Assert-That ($Evidence.pageMotion.opacity -eq 1) 'The static page is not fully opaque.'
    if($null -ne $Evidence.pageMotion.translation){
        $position=$Evidence.pageMotion.translation
        Assert-That ($position.x -eq 0 -and $position.y -eq 0 -and $position.z -eq 0) 'Page translation was not restored to zero.'
    }
}

foreach($scenario in @('cold-reduced-motion','normal-and-toggle','animation-failure')){
    $run=Join-Path $output $scenario
    $control=Join-Path $run 'control'
    $profile=Join-Path $run 'profile'
    $temp=Join-Path $run 'temp'
    foreach($path in @($control,$temp)){[IO.Directory]::CreateDirectory($path)|Out-Null}
    $start=[Diagnostics.ProcessStartInfo]::new($exe)
    $start.UseShellExecute=$false; $start.WorkingDirectory=Split-Path $exe -Parent
    foreach($name in @($start.Environment.Keys)){
        if($name.StartsWith('THE_ORDER_NATIVE_') -or $name -in @('KGM_DATA_DIR','KGM_TEST_EXECUTABLE')){[void]$start.Environment.Remove($name)}
    }
    $variables=@{TEMP=$temp;TMP=$temp;THE_ORDER_NATIVE_PROFILE_KIND='Test';THE_ORDER_NATIVE_DATA_DIR=$profile;THE_ORDER_NATIVE_FIXTURE_PROFILE='0';THE_ORDER_NATIVE_DIAGNOSTICS='1';THE_ORDER_NATIVE_VISUAL_TEST='1';THE_ORDER_NATIVE_VISUAL_CONTROL=$control;THE_ORDER_NATIVE_ONLINE_FIXTURE='0';THE_ORDER_NATIVE_REDUCED_MOTION_TEST=$(if($scenario -eq 'cold-reduced-motion'){'1'}else{'0'});THE_ORDER_NATIVE_PAGE_MOTION_FAILURE_TEST=$(if($scenario -eq 'animation-failure'){'1'}else{'0'})}
    foreach($name in $variables.Keys){$start.Environment[$name]=$variables[$name]}
    $process=[Diagnostics.Process]::Start($start)
    $failure=$null; $evidence=$null
    [ordered]@{pid=$process.Id;startedUtc=$process.StartTime.ToUniversalTime().ToString('o');exe=$exe;profile=$profile;productionAccess=$false}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $run 'process.json') -Encoding utf8
    try{
        # Wait for the actual UI loop before posting a request; initialization ignores old requests.
        $deadline=[DateTime]::UtcNow.AddSeconds(25)
        while(!(Test-Path -LiteralPath (Join-Path $control 'fixture-ready.json')) -and [DateTime]::UtcNow -lt $deadline -and !$process.HasExited){Start-Sleep -Milliseconds 100}
        $evidence=Invoke-Check $process $control 'probe'
        Assert-Ready $evidence
        if($scenario -eq 'cold-reduced-motion'){
            Assert-That (!$evidence.material.motionAllowed -and !$evidence.pageMotion.translationEnabled -and !$evidence.pageMotion.failed -and $evidence.pageMotion.animationCount -eq 0) 'Cold reduced-motion startup touched or failed the optional Translation property.'
        }else{
            Assert-That ($evidence.material.motionAllowed) 'Windows animations must be enabled for the positive/toggle and failure scenarios; this is not a passing run.'
        }
        foreach($page in @('Statistics','Settings','Library')){
            $evidence=Invoke-Check $process $control 'page' @{value=$page}
            Assert-Ready $evidence
            Assert-That ($evidence.visualScreen -eq $page) 'The requested page did not become current.'
        }
        if($scenario -eq 'cold-reduced-motion'){
            Assert-That (!$evidence.pageMotion.translationEnabled -and !$evidence.pageMotion.failed -and $evidence.pageMotion.animationCount -eq 0) 'Reduced-motion navigation attempted page animation.'
        }elseif($scenario -eq 'normal-and-toggle'){
            Assert-That ($evidence.pageMotion.translationEnabled -and !$evidence.pageMotion.failed -and $evidence.pageMotion.animationCount -gt 0) 'Enabled animations were not exercised.'
            $count=$evidence.pageMotion.animationCount
            $evidence=Invoke-Check $process $control 'motion' @{value=$true}
            Assert-Ready $evidence
            Assert-That (!$evidence.material.motionAllowed -and !$evidence.pageMotion.failed) 'Turning motion off after animation failed.'
            $evidence=Invoke-Check $process $control 'page' @{value='Settings'}
            Assert-Ready $evidence
            Assert-That ($evidence.pageMotion.animationCount -eq $count) 'Navigation still animated with motion off.'
            $null=Invoke-Check $process $control 'motion' @{value=$false}
            $evidence=Invoke-Check $process $control 'page' @{value='Library'}
            Assert-Ready $evidence
            Assert-That (!$evidence.pageMotion.failed -and $evidence.pageMotion.animationCount -gt $count) 'Animations did not resume after re-enabling motion.'
        }else{
            Assert-That ($evidence.pageMotion.failed -and $evidence.pageMotion.failure -like 'ArgumentException:*' -and $evidence.pageMotion.animationCount -eq 0) 'Injected optional-animation failure did not fall back to static navigation.'
            $evidence=Invoke-Check $process $control 'motion' @{value=$true}
            Assert-Ready $evidence
        }
        Assert-That (!(Test-Path -LiteralPath (Join-Path $profile 'visual-fatal.txt'))) 'The native instance recorded an unexpected fatal visual error.'
    }catch{$failure=$_.Exception.Message}
    finally{
        if(!$process.HasExited){
            try{Invoke-Check $process $control 'exit'}catch{if(!$failure){$failure=$_.Exception.Message}}
            if(!$process.WaitForExit(15000) -and !$failure){$failure='Test instance did not exit normally; it has not been force-terminated.'}
        }
        if($process.HasExited -and $process.ExitCode -ne 0 -and !$failure){$failure='Test process exited with code '+$process.ExitCode}
        $result=[ordered]@{scenario=$scenario;passed=(!$failure);failure=$failure;normalExit=($process.HasExited -and $process.ExitCode -eq 0);startup=$evidence.startup;pageMotion=$evidence.pageMotion}
        $results.Add($result)
        $result|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $run 'result.json') -Encoding utf8
        $results|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding utf8
        $process.Dispose()
    }
    if($failure){throw ($scenario+': '+$failure)}
    Write-Output ('PASS '+$scenario)
}
