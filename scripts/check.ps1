param([switch]$Browser)
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& dotnet build Workspace.sln --no-restore --configuration Release --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'The solution build failed.' }
& dotnet test Workspace.sln --no-build --no-restore --configuration Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'The deterministic controls failed.' }
foreach ($profile in Get-ChildItem profiles -Directory | Where-Object { $_.Name -ne 'private' }) {
    & dotnet run --project tools\Workspace.Tools --no-build --configuration Release -- validate --profile $profile.Name
    if ($LASTEXITCODE -ne 0) { throw "Profile validation failed: $($profile.Name)." }
}
Push-Location src\Workspace.Web\frontend
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'The browser dependency build failed.' }
    & npm test
    if ($LASTEXITCODE -ne 0) { throw 'The source rendering checks failed.' }
} finally { Pop-Location }
if ($Browser) {
    $settings = Get-Content config\app.json -Raw | ConvertFrom-Json
    $address = "http://127.0.0.1:$($settings.port)"
    New-Item .local -ItemType Directory -Force | Out-Null
    $root = Split-Path $PSScriptRoot -Parent
    $webDirectory = Join-Path $root 'src\Workspace.Web'
    $executableName = if ($IsWindows) { 'Workspace.Web.exe' } else { 'Workspace.Web' }
    $executable = Join-Path $webDirectory "bin\Release\net10.0\$executableName"
    $process = Start-Process $executable -WorkingDirectory $webDirectory `
        -RedirectStandardOutput (Join-Path $root '.local\server-output.log') `
        -RedirectStandardError (Join-Path $root '.local\server-error.log') -PassThru
    try {
        $responsive = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if ($process.HasExited) { throw 'The check server stopped before readiness. Inspect .local server logs.' }
            try {
                $response = Invoke-WebRequest "$address/api/config" -SkipHttpErrorCheck
                if ($response.StatusCode -eq 200) { $responsive = $true; break }
            } catch [System.Net.Http.HttpRequestException] { }
            Start-Sleep -Milliseconds 500
        }
        if (-not $responsive) { throw 'The check server did not respond.' }
        $env:WORKSPACE_BASE_URL = $address
        Push-Location src\Workspace.Web\frontend
        try {
            & npm run test:browser
            if ($LASTEXITCODE -ne 0) { throw 'The browser checks failed.' }
        } finally { Pop-Location }
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id }
    }
}
