param([string]$OutputDirectory)
$ErrorActionPreference='Stop'
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'free-core/source-manifest.json') -Raw | ConvertFrom-Json
if($manifest.schema -ne 1 -or $manifest.edition -ne 'FreeCore'){throw 'Invalid Free Core source manifest'}
if(-not $OutputDirectory){$OutputDirectory=Join-Path $PSScriptRoot 'build-free-core/Unified'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
foreach($legacy in @('customer','customer-commercial','customer-trial','customer-qa')){
    $prefix=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot $legacy)).TrimEnd('\')
    if($output -eq $prefix -or $output.StartsWith($prefix+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Free Core must not overwrite a historical build variant'}
}
$sources=@($manifest.unifiedSources)+@($manifest.readerSources)+@($manifest.bridgeSources)
if($sources | Where-Object {$_ -in @('LicenseClient.cs','AccountTrialClient.cs','CompanionClient.cs','GeminiOAuthIdentity.cs')}){throw 'Account services are not Free Core dependencies'}
foreach($name in ($sources | Select-Object -Unique)){
    if([IO.Path]::GetFileName($name) -ne $name -or -not(Test-Path -LiteralPath (Join-Path $PSScriptRoot ('src/'+$name)))){throw ('Missing or invalid source: '+$name)}
}
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){throw 'Build Free Core on Windows with .NET Framework 4.8/WPF installed'}
& (Join-Path $PSScriptRoot 'compile-motion-shader.ps1')
& (Join-Path $PSScriptRoot 'compile-views.ps1')
$refs=@('System.dll','System.Net.Http.dll','System.Core.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Management.dll','System.Drawing.dll','System.Windows.Forms.dll','Microsoft.CSharp.dll') | ForEach-Object {'/reference:'+(Join-Path $framework $_)}
$refs+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll','UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object {'/reference:'+(Join-Path $framework ('WPF/'+$_))}
$common=@('/nologo','/platform:x64','/optimize+','/debug-','/utf8output','/codepage:65001','/nowarn:0162,0429')
$viewResource=Join-Path $PSScriptRoot 'build-private/views/obj/Debug/CompiledViews.g.resources'
$shader=Join-Path $PSScriptRoot 'src/HorizontalMotionBlur.ps'
[void][IO.Directory]::CreateDirectory($output)
$nativeManifest=Join-Path $output 'free-core.manifest'
$text=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'src/app.manifest')) -replace '<assemblyIdentity[^>]*/>','<assemblyIdentity version="0.5.3.0" name="QuoriloMonitorFreeCore"/>'
[IO.File]::WriteAllText($nativeManifest,$text,[Text.UTF8Encoding]::new($false))
$nativeArgument='/win32manifest:'+$nativeManifest
function Copy-FreeAssets([string]$destination){
    [void][IO.Directory]::CreateDirectory((Join-Path $destination 'Fonts'))
    foreach($file in @('NotoSansThai-Regular.ttf','NotoSansThai-SemiBold.ttf','NotoSansThai-Bold.ttf','OFL.txt')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('assets/Fonts/'+$file)) -Destination (Join-Path $destination ('Fonts/'+$file)) -Force}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'free-core/FREE-CORE-NOTICE.txt') -Destination (Join-Path $destination 'FREE-CORE-NOTICE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'free-core/PRIVACY.th.md') -Destination (Join-Path $destination 'PRIVACY.th.md') -Force
}
foreach($provider in @('ChatGPT','Claude','Gemini')){
    $destination=Join-Path $output ('Readers/'+$provider)
    Copy-FreeAssets $destination
    $name=if($provider -eq 'ChatGPT'){'ChatGPTCreditMonitor'}elseif($provider -eq 'Claude'){'ClaudeCodeMonitor'}else{'GeminiQuotaMonitor'}
    $define='/define:FREE_CORE,'+$provider.ToUpperInvariant()
    $readerRefs=@()
    if($provider -eq 'Gemini'){
        $readerRefs=@('/reference:'+(Join-Path $PSScriptRoot 'vendor/WebView2/Microsoft.Web.WebView2.Core.dll'))
        foreach($dependency in @('Microsoft.Web.WebView2.Core.dll','WebView2Loader.dll','WebView2-LICENSE.txt','WebView2-NOTICE.txt')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('vendor/WebView2/'+$dependency)) -Destination $destination -Force}
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/GeminiCLI-LICENSE.txt') -Destination $destination -Force
    }
    $readerSources=@($manifest.readerSources | ForEach-Object {Join-Path $PSScriptRoot ('src/'+$_)})
    & $compiler @common $nativeArgument /target:winexe /main:CreditMonitor.Program $define @refs @readerRefs ('/out:'+(Join-Path $destination ($name+'.exe'))) ('/resource:'+$viewResource+','+$name+'.g.resources') ('/resource:'+$shader+',HorizontalMotionBlur.ps') @readerSources
    if($LASTEXITCODE -ne 0){throw ('Free Core reader compilation failed: '+$provider)}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/app.config') -Destination (Join-Path $destination ($name+'.exe.config')) -Force
    if($provider -eq 'Claude'){
        $bridgeSources=@($manifest.bridgeSources | ForEach-Object {Join-Path $PSScriptRoot ('src/'+$_)})
        & $compiler @common $nativeArgument /target:exe $define @refs ('/out:'+(Join-Path $destination 'ClaudeQuotaBridge.exe')) @bridgeSources
        if($LASTEXITCODE -ne 0){throw 'Free Core Claude bridge compilation failed'}
    }
}
Copy-FreeAssets $output
$unifiedSources=@($manifest.unifiedSources | ForEach-Object {Join-Path $PSScriptRoot ('src/'+$_)})
& $compiler @common $nativeArgument /target:winexe /main:CreditMonitor.Program /define:UNIFIED,FREE_CORE @refs ('/out:'+(Join-Path $output 'CreditMonitor.exe')) ('/resource:'+$viewResource+',CreditMonitor.g.resources') ('/resource:'+$shader+',HorizontalMotionBlur.ps') @unifiedSources
if($LASTEXITCODE -ne 0){throw 'Free Core unified compilation failed'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/app.config') -Destination (Join-Path $output 'CreditMonitor.exe.config') -Force
Write-Output ('FREE CORE SOURCE BUILD: '+(Join-Path $output 'CreditMonitor.exe'))
Write-Output 'Private prerelease candidate only. No account configuration, protection download, installer, signing, promotion or distribution performed.'
