# Requires PowerShell 7 and a Debug build of the solution.
param([string]$ServerDirectory = '')

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
if ($ServerDirectory) {
    $taskContentRoot = [IO.Path]::GetFullPath($ServerDirectory)
    $taskDll = Join-Path $taskContentRoot 'Synapse.Blocks.Server.dll'
} else {
    $taskContentRoot = Join-Path $taskRoot 'Server'
    $taskDll = Join-Path $taskRoot 'Server/bin/Debug/net10.0/Synapse.Blocks.Server.dll'
}
if (-not (Test-Path -LiteralPath $taskDll)) { throw 'Run dotnet build Synapse.Blocks.slnx first.' }
$taskStorage = Join-Path ([IO.Path]::GetTempPath()) ('synapse-smoke-' + [guid]::NewGuid().ToString('N'))
$taskListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$taskListener.Start()
$taskPort = $taskListener.LocalEndpoint.Port
$taskListener.Stop()
$taskUrl = "http://127.0.0.1:$taskPort"
$taskPassword = [guid]::NewGuid().ToString('N')
$script:taskProcess = $null

function Start-TestServer {
    $taskStart = [Diagnostics.ProcessStartInfo]::new('dotnet')
    $taskStart.ArgumentList.Add($taskDll)
    $taskStart.WorkingDirectory = $taskContentRoot
    $taskStart.UseShellExecute = $false
    $taskStart.CreateNoWindow = $true
    $taskStart.RedirectStandardOutput = $true
    $taskStart.RedirectStandardError = $true
    $taskStart.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
    $taskStart.Environment['ASPNETCORE_URLS'] = $taskUrl
    $taskStart.Environment['Admin__Password'] = $taskPassword
    $taskStart.Environment['Storage__Directory'] = $taskStorage
    $script:taskProcess = [Diagnostics.Process]::Start($taskStart)
    $script:taskStdout = $script:taskProcess.StandardOutput.ReadToEndAsync()
    $script:taskStderr = $script:taskProcess.StandardError.ReadToEndAsync()
    for ($taskAttempt = 0; $taskAttempt -lt 80; $taskAttempt++) {
        if ($script:taskProcess.HasExited) {
            throw "Server exited: $($script:taskStdout.GetAwaiter().GetResult()) $($script:taskStderr.GetAwaiter().GetResult())"
        }
        try {
            $taskReady = Invoke-WebRequest "$taskUrl/api/admin/session" -TimeoutSec 1 -SkipHttpErrorCheck
            if ($taskReady.StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Milliseconds 250
    }
    throw 'Server did not start.'
}

function Stop-TestServer {
    if ($script:taskProcess) {
        if (-not $script:taskProcess.HasExited) { $script:taskProcess.Kill($true); $script:taskProcess.WaitForExit() }
        $script:taskProcess.Dispose()
        $script:taskProcess = $null
    }
}

function Assert-Status($taskResponse, [int]$taskExpected, [string]$taskLabel) {
    if ([int]$taskResponse.StatusCode -ne $taskExpected) {
        throw "$taskLabel`: expected $taskExpected, got $($taskResponse.StatusCode): $($taskResponse.Content)"
    }
    Write-Host "PASS $taskLabel"
}

try {
    Start-TestServer
    $taskPublic = Invoke-WebRequest "$taskUrl/api/levels" -SkipHttpErrorCheck
    Assert-Status $taskPublic 200 'Public campaign'
    $taskLevels = @($taskPublic.Content | ConvertFrom-Json)
    if ($taskLevels.Count -lt 1) { throw 'Seed campaign is empty.' }
    foreach ($taskRoute in @('/', '/game', '/admin', '/admin/levels', '/admin/chapters', '/admin/editor', '/editor', '/css/app.css', '/css/admin.css', '/css/markdown.css', '/js/admin-ui.js')) {
        Assert-Status (Invoke-WebRequest ($taskUrl + $taskRoute) -SkipHttpErrorCheck) 200 "Hosted frontend $taskRoute"
    }
    $taskHtml = (Invoke-WebRequest $taskUrl).Content
    $taskBootPath = [regex]::Match($taskHtml, 'src="(_framework/blazor\.webassembly[^"#]*\.js)"').Groups[1].Value
    if (-not $taskBootPath) { throw 'Frontend boot script was not resolved.' }
    Assert-Status (Invoke-WebRequest "$taskUrl/$taskBootPath" -SkipHttpErrorCheck) 200 'Blazor boot script'
    Assert-Status (Invoke-WebRequest "$taskUrl/api/missing" -SkipHttpErrorCheck) 404 'Unknown API route'
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/levels" -SkipHttpErrorCheck) 401 'Anonymous editor denied'
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/levels" -Method Put -ContentType 'application/json' -Body $taskPublic.Content -Headers @{'X-Synapse-Request'='1'} -SkipHttpErrorCheck) 401 'Anonymous save denied'
    $taskSession = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    $taskLogin = @{ password = $taskPassword } | ConvertTo-Json
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/login" -Method Post -ContentType 'application/json' -Body $taskLogin -WebSession $taskSession -SkipHttpErrorCheck) 403 'Login without request header denied'
    $taskHeaders = @{'X-Synapse-Request'='1'}
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/login" -Method Post -ContentType 'application/json' -Body '{"password":"incorrect"}' -Headers $taskHeaders -WebSession $taskSession -SkipHttpErrorCheck) 401 'Wrong password denied'
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/login" -Method Post -ContentType 'application/json' -Body $taskLogin -Headers $taskHeaders -WebSession $taskSession -SkipHttpErrorCheck) 204 'Admin login'
    $taskEditor = Invoke-WebRequest "$taskUrl/api/admin/levels" -WebSession $taskSession -SkipHttpErrorCheck
    Assert-Status $taskEditor 200 'Authenticated editor'
    $taskOldETag = [string]$taskEditor.Headers.ETag[0]
    $taskHeaders['If-Match'] = $taskOldETag
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/levels" -Method Put -ContentType 'application/json' -Body '[]' -Headers $taskHeaders -WebSession $taskSession -SkipHttpErrorCheck) 400 'Empty campaign rejected'
    $taskNewLevel = $taskLevels[0] | ConvertTo-Json -Depth 30 | ConvertFrom-Json
    $taskNewLevel.Id = [guid]::NewGuid().ToString()
    $taskNewLevel.Order = $taskLevels.Count + 1
    $taskNewLevel.Title = 'Integration test level'
    $taskLevels += $taskNewLevel
    $taskBody = ConvertTo-Json -InputObject $taskLevels -Depth 30
    [void]$taskSession.Headers.Remove('X-Synapse-Request')
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/levels" -Method Put -ContentType 'application/json' -Body $taskBody -Headers @{'If-Match'=$taskOldETag} -WebSession $taskSession -SkipHttpErrorCheck) 403 'Save without request header denied'
    $taskSaved = Invoke-WebRequest "$taskUrl/api/admin/levels" -Method Put -ContentType 'application/json' -Body $taskBody -Headers $taskHeaders -WebSession $taskSession -SkipHttpErrorCheck
    Assert-Status $taskSaved 204 'New level saved'
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/levels" -Method Put -ContentType 'application/json' -Body $taskBody -Headers $taskHeaders -WebSession $taskSession -SkipHttpErrorCheck) 409 'Stale campaign write rejected'
    $taskHeaders['If-Match'] = [string]$taskSaved.Headers.ETag[0]
    $taskNewLevel.Tests = $null
    $taskInvalidBody = ConvertTo-Json -InputObject $taskLevels -Depth 30
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/levels" -Method Put -ContentType 'application/json' -Body $taskInvalidBody -Headers $taskHeaders -WebSession $taskSession -SkipHttpErrorCheck) 400 'Malformed level rejected'
    $taskPublished = @((Invoke-WebRequest "$taskUrl/api/levels").Content | ConvertFrom-Json)
    if ($taskPublished.Count -ne $taskLevels.Count -or $taskPublished[-1].Title -ne 'Integration test level') { throw 'New level not published.' }
    Write-Host 'PASS Saved level visible to players; invalid write preserved campaign'
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/logout" -Method Post -Headers $taskHeaders -WebSession $taskSession -SkipHttpErrorCheck) 204 'Admin logout'
    Assert-Status (Invoke-WebRequest "$taskUrl/api/admin/levels" -WebSession $taskSession -SkipHttpErrorCheck) 401 'Logged out editor denied'
    Stop-TestServer
    Start-TestServer
    $taskPersisted = @((Invoke-WebRequest "$taskUrl/api/levels").Content | ConvertFrom-Json)
    if ($taskPersisted.Count -ne $taskPublished.Count -or $taskPersisted[-1].Id -ne $taskPublished[-1].Id) { throw 'Campaign did not survive restart.' }
    Write-Host 'PASS Campaign persisted after server restart'
} finally {
    Stop-TestServer
    $taskResolvedStorage = [IO.Path]::GetFullPath($taskStorage)
    $taskTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($taskResolvedStorage.StartsWith($taskTempRoot) -and (Split-Path $taskResolvedStorage -Leaf) -match '^synapse-smoke-[a-f0-9]{32}$' -and (Test-Path -LiteralPath $taskResolvedStorage)) {
        Remove-Item -LiteralPath $taskResolvedStorage -Recurse -Force
    }
}
