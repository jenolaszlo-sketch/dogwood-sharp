[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int] $Port,
    [Parameter(Mandatory)] [string] $ConfigPath,
    [Parameter(Mandatory)] [string] $LogPath,
    [Parameter(Mandatory)] [string] $ReadyPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$config = Get-Content -Raw -LiteralPath $ConfigPath | ConvertFrom-Json
$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://127.0.0.1:$Port/")
$listener.Start()
[System.IO.File]::WriteAllText($ReadyPath, 'ready', (New-Object System.Text.UTF8Encoding($false)))

function Write-Log($Object) {
    $line = $Object | ConvertTo-Json -Compress -Depth 8
    [System.IO.File]::AppendAllText($LogPath, $line + "`n", (New-Object System.Text.UTF8Encoding($false)))
}

function Send-Bytes($Context, [int] $StatusCode, [byte[]] $Bytes) {
    $Context.Response.StatusCode = $StatusCode
    $Context.Response.ContentType = 'application/octet-stream'
    if ($null -ne $Bytes) {
        $Context.Response.ContentLength64 = $Bytes.Length
        $Context.Response.OutputStream.Write($Bytes, 0, $Bytes.Length)
    }
    else {
        $Context.Response.ContentLength64 = 0
    }
    $Context.Response.OutputStream.Close()
}

function Get-ModeResponse($ModeConfig, [string] $SourceFile, [string] $OptionalConflictFile) {
    $mode = [string] $ModeConfig.mode
    if ($null -eq $ModeConfig.PSObject.Properties['counter']) {
        $ModeConfig | Add-Member -NotePropertyName counter -NotePropertyValue 0
    }
    if ($mode -eq 'file' -or $mode -eq 'delay404') {
        $ModeConfig.counter = [int] $ModeConfig.counter + 1
        if ($mode -eq 'delay404' -and [int] $ModeConfig.counter -le [int] $ModeConfig.delayCount) {
            return [pscustomobject]@{ status = 404; bytes = $null }
        }
        if (!(Test-Path -LiteralPath $SourceFile -PathType Leaf)) {
            return [pscustomobject]@{ status = 404; bytes = $null }
        }
        return [pscustomobject]@{ status = 200; bytes = [System.IO.File]::ReadAllBytes($SourceFile) }
    }
    if ($mode -eq 'missing') { return [pscustomobject]@{ status = 404; bytes = $null } }
    if ($mode -eq 'status503') { return [pscustomobject]@{ status = 503; bytes = $null } }
    if ($mode -eq 'status429') { return [pscustomobject]@{ status = 429; bytes = $null } }
    if ($mode -eq 'status403') { return [pscustomobject]@{ status = 403; bytes = $null } }
    if ($mode -eq 'reset') { return [pscustomobject]@{ status = -1; bytes = $null } }
    if ($mode -eq 'conflict') {
        if (!(Test-Path -LiteralPath $SourceFile -PathType Leaf)) {
            return [pscustomobject]@{ status = 404; bytes = $null }
        }
        $bytes = [System.IO.File]::ReadAllBytes($SourceFile)
        if (![string]::IsNullOrEmpty([string] $OptionalConflictFile) -and (Test-Path -LiteralPath $OptionalConflictFile -PathType Leaf)) {
            return [pscustomobject]@{ status = 200; bytes = [System.IO.File]::ReadAllBytes($OptionalConflictFile) }
        }
        if ($bytes.Length -gt 0) { $bytes[$bytes.Length - 1] = [byte] (($bytes[$bytes.Length - 1] + 1) % 256) }
        return [pscustomobject]@{ status = 200; bytes = $bytes }
    }
    return [pscustomobject]@{ status = 500; bytes = $null }
}

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            $requestPath = $context.Request.Url.AbsolutePath
            $segments = @($requestPath.Trim('/').Split('/'))
            $logEntry = [ordered]@{
                timeUtc = [DateTime]::UtcNow.ToString('o')
                method = [string] $context.Request.HttpMethod
                path = $requestPath
                symbolChecksum = [string] $context.Request.Headers['SymbolChecksum']
            }
            Write-Log $logEntry

            if ($segments.Count -ge 4 -and $segments[0] -ceq 'symbols') {
                $pdbName = $segments[1]
                $key = $segments[2]
                $symbolConfig = $config.symbols
                if ([bool] $symbolConfig.requireChecksum -and [string]::IsNullOrEmpty([string] $context.Request.Headers['SymbolChecksum'])) {
                    Send-Bytes $context 403 $null
                    continue
                }
                $sourceFile = if ($symbolConfig.directory) { Join-Path $symbolConfig.directory "$pdbName/$key/$pdbName" } else { '' }
                $response = Get-ModeResponse $symbolConfig $sourceFile $symbolConfig.conflictFile
                if ($response.status -eq -1) { $context.Response.Abort(); continue }
                Send-Bytes $context $response.status $response.bytes
                continue
            }

            $packageConfig = $config.package
            $requestedFile = $segments[$segments.Count - 1]
            if ([string] $packageConfig.file) {
                if ($requestedFile -cne [string] $packageConfig.file) {
                    Send-Bytes $context 404 $null
                    continue
                }
            }
            $packageSource = if ([string] $packageConfig.directory) {
                Join-Path ([string] $packageConfig.directory) $requestedFile
            }
            else {
                [string] $packageConfig.sourceFile
            }
            $packageResponse = Get-ModeResponse $packageConfig $packageSource $null
            if ($packageResponse.status -eq -1) { $context.Response.Abort(); continue }
            Send-Bytes $context $packageResponse.status $packageResponse.bytes
        }
        catch {
            try { Send-Bytes $context 500 $null } catch { }
        }
    }
}
finally {
    try { $listener.Stop(); $listener.Close() } catch { }
}
