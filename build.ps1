param([switch]$Test)
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) { $dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" }
if (-not (Test-Path $dotnet)) { throw '.NET SDK not found: https://dotnet.microsoft.com/download' }

function Build($config, $target) {
  & $dotnet build Glint.csproj -c $config -v q --nologo -clp:ErrorsOnly
  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
  Copy-Item "bin\$config\net48\Glint.exe" $target -Force
  "Built $target ($([math]::Round((Get-Item $target).Length / 1KB)) KB)"
}

Build 'Release' 'Glint.exe'
if ($Test) { Build 'Test' 'GlintTest.exe' }
