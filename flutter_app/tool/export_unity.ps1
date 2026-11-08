param([ValidateRange(1, 60)][int]$TimeoutMinutes = 30)

$ErrorActionPreference = 'Stop'
$unityRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$descriptorPath = Join-Path $unityRoot 'Library\Pipeline\.unity-pipeline-port'
if (-not (Test-Path -LiteralPath $descriptorPath)) {
    throw 'Open the parent Unity project first, or use Build > Export Unity Library for Flutter in Unity.'
}
$descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json
$port = [int]$descriptor.port
if ($port -lt 1 -or $port -gt 65535) {
    throw 'Invalid local Unity bridge port.'
}
$baseUrl = "http://127.0.0.1:$port"
$headers = @{ Authorization = "Bearer $($descriptor.evalToken)" }
$startedAt = Get-Date
$request = @{
    command = 'eval'
    parameters = @{
        code = 'FlutterUnityExporter.Export(); return "Unity library export completed";'
        timeout = $TimeoutMinutes * 60000
    }
    job = $true
} | ConvertTo-Json -Depth 4
$response = Invoke-RestMethod -Uri "$baseUrl/api/exec" -Headers $headers -Method Post -ContentType 'application/json' -Body $request -TimeoutSec 15
$jobId = $response.result.jobId
if (-not $response.success -or -not $jobId) {
    throw 'Unity did not accept the export. Check the Editor console and stop Play mode.'
}
$deadline = (Get-Date).AddMinutes($TimeoutMinutes)
Write-Host "Unity export started: $jobId"
do {
    Start-Sleep -Seconds 3
    $job = Invoke-RestMethod -Uri "$baseUrl/api/job?id=$jobId" -Headers $headers -TimeoutSec 15
    if ($job.state -in @('failed', 'canceled')) {
        throw "Unity export job $($job.state). Check the Editor console."
    }
    if ((Get-Date) -gt $deadline) {
        throw 'Timed out waiting; the Unity export may still be running. Check the Editor before retrying.'
    }
} while ($job.state -ne 'completed')
if ($job.result.success -eq $false -or $job.result.result.success -eq $false) {
    throw 'Unity export reported an error. Check the Editor console.'
}
$summary = Join-Path $unityRoot 'flutter_app\unity_export\export-summary.txt'
if (-not (Test-Path -LiteralPath $summary) -or (Get-Content -LiteralPath $summary -Raw) -notmatch 'Result: Succeeded') {
    throw 'No successful export summary found. Check the Editor console.'
}
if ((Get-Item -LiteralPath $summary).LastWriteTime -lt $startedAt) {
    throw 'The export summary is stale; this export was not verified.'
}
Get-Content -LiteralPath $summary
