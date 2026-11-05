$ErrorActionPreference='Stop'
$taskViewRoot=Join-Path $PSScriptRoot 'build-private/views'
[void][IO.Directory]::CreateDirectory($taskViewRoot)
foreach($taskView in @('Widget.xaml','SettingsStyles.xaml')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('src/'+$taskView)) -Destination $taskViewRoot -Force}
$taskProject=@'
<Project ToolsVersion="4.0" DefaultTargets="Build" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <PropertyGroup><OutputType>Library</OutputType><AssemblyName>CompiledViews</AssemblyName><TargetFrameworkVersion>v4.0</TargetFrameworkVersion><PlatformTarget>x64</PlatformTarget><TargetFrameworkRootPath>C:\Windows\Microsoft.NET\Framework64\v4.0.30319\</TargetFrameworkRootPath><OutputPath>bin\</OutputPath></PropertyGroup>
  <ItemGroup><Reference Include="System"/><Reference Include="System.Core"/><Reference Include="WindowsBase"/><Reference Include="PresentationCore"/><Reference Include="PresentationFramework"/><Reference Include="System.Xaml"/></ItemGroup>
  <ItemGroup><Page Include="Widget.xaml"/><Page Include="SettingsStyles.xaml"/></ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets"/>
</Project>
'@
$taskProjectFile=Join-Path $taskViewRoot 'CompiledViews.csproj'
[IO.File]::WriteAllText($taskProjectFile,$taskProject,[Text.UTF8Encoding]::new($false))
$taskMsbuild=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/MSBuild.exe'
& $taskMsbuild $taskProjectFile /nologo /verbosity:minimal /target:Rebuild
if($LASTEXITCODE -ne 0){throw 'WPF compiled view build failed'}
if(-not(Test-Path (Join-Path $taskViewRoot 'obj/Debug/CompiledViews.g.resources'))){throw 'Compiled WPF resources missing'}
