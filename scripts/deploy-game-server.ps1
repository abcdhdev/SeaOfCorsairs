param(
    [Parameter(Mandatory = $true)]
    [Alias("Host")]
    [string]$Server,

    [string]$User = "ubuntu",

    [int]$Port = 22,

    [string]$Password,

    [string]$KeyPath,

    [string]$RemoteRoot = "/opt/seawars",

    [switch]$SkipEnvSync
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$pythonScript = Join-Path $scriptDir "deploy_game_server.py"

$argsList = @(
    $pythonScript,
    "--host", $Server,
    "--user", $User,
    "--port", $Port.ToString(),
    "--remote-root", $RemoteRoot
)

if ($Password) {
    $argsList += @("--password", $Password)
}

if ($KeyPath) {
    $argsList += @("--key-path", $KeyPath)
}

if ($SkipEnvSync) {
    $argsList += "--skip-env-sync"
}

& python $argsList
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
