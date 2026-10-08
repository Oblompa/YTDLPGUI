[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Query,

    [switch]$ConfirmLicensedContent,

    [string]$AppPath = (Join-Path $PSScriptRoot "..\..\src\YtDlpAudio.UI\bin\Portable\net9.0-windows\win-x64\publish\YtDlpAudio.UI.exe"),

    [ValidateRange(30, 1800)]
    [int]$DependencyTimeoutSeconds = 600,

    [ValidateRange(30, 3600)]
    [int]$SearchTimeoutSeconds = 180,

    [ValidateRange(60, 14400)]
    [int]$DownloadTimeoutSeconds = 1800
)

$ErrorActionPreference = "Stop"

if (-not $ConfirmLicensedContent) {
    throw "Only run this end-to-end download test with content you are authorized to download. Pass -ConfirmLicensedContent to acknowledge this."
}

$resolvedAppPath = (Resolve-Path -LiteralPath $AppPath).Path
$outputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "YtDlpAudio-E2E-$([guid]::NewGuid().ToString('N'))"
$appProcess = $null
$window = $null
$originalOutputDirectory = $null

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Find-ByAutomationId {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Parent,

        [Parameter(Mandatory = $true)]
        [string]$AutomationId
    )

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return $Parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-ForAutomationId {
    param(
        [Parameter(Mandatory = $true)]
        [System.Windows.Automation.AutomationElement]$Parent,

        [Parameter(Mandatory = $true)]
        [string]$AutomationId,

        [Parameter(Mandatory = $true)]
        [int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $element = Find-ByAutomationId -Parent $Parent -AutomationId $AutomationId
        if ($null -ne $element) {
            return $element
        }

        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Timed out waiting for UI control '$AutomationId'."
}

try {
    [void](New-Item -ItemType Directory -Path $outputDirectory -Force)
    Write-Host "Launching: $resolvedAppPath"
    $appProcess = Start-Process -FilePath $resolvedAppPath -PassThru

    $windowDeadline = [DateTime]::UtcNow.AddSeconds(60)
    do {
        $appProcess.Refresh()
        if ($appProcess.MainWindowHandle -ne 0) {
            break
        }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $windowDeadline)

    if ($appProcess.MainWindowHandle -eq 0) {
        throw "The application did not open its main window."
    }

    $window = [System.Windows.Automation.AutomationElement]::FromHandle($appProcess.MainWindowHandle)
    $statusControl = Wait-ForAutomationId -Parent $window -AutomationId "StatusMessage" -TimeoutSeconds 30

    Write-Host "Installing/verifying yt-dlp, Deno, and FFmpeg..."
    $setupButton = Wait-ForAutomationId -Parent $window -AutomationId "SetupDependenciesButton" -TimeoutSeconds 30
    $setupButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $dependencyDeadline = [DateTime]::UtcNow.AddSeconds($DependencyTimeoutSeconds)
    do {
        $status = $statusControl.Current.Name
        if ($status -eq "Dependencies installed and ready!") {
            break
        }
        if ($status -like "Failed to provision dependencies:*" -or $status -like "Could not prepare download dependencies:*") {
            throw "Dependency setup failed: $status"
        }
        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $dependencyDeadline)

    if ($statusControl.Current.Name -ne "Dependencies installed and ready!") {
        throw "Timed out waiting for dependency setup. Last status: $($statusControl.Current.Name)"
    }

    $outputControl = Wait-ForAutomationId -Parent $window -AutomationId "OutputDirectoryInput" -TimeoutSeconds 30
    $outputValue = $outputControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $originalOutputDirectory = $outputValue.Current.Value
    $outputValue.SetValue($outputDirectory)

    Write-Host "Searching for: $Query"
    $filterControl = Wait-ForAutomationId -Parent $window -AutomationId "SearchFilter" -TimeoutSeconds 30
    $filterControl.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $tracksCondition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::ListItem),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            "Tracks"))
    $tracksOption = $filterControl.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $tracksCondition)
    if ($null -eq $tracksOption) {
        throw "Could not select the Tracks search filter."
    }
    $tracksOption.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()

    $searchInput = Wait-ForAutomationId -Parent $window -AutomationId "SearchInput" -TimeoutSeconds 30
    $searchInput.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Query)
    $searchButton = Wait-ForAutomationId -Parent $window -AutomationId "SearchSubmitButton" -TimeoutSeconds 30
    $searchButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $resultsCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        "QueueSearchResultButton")
    $searchDeadline = [DateTime]::UtcNow.AddSeconds($SearchTimeoutSeconds)
    $firstResultButton = $null
    do {
        $firstResultButton = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $resultsCondition)
        if ($null -ne $firstResultButton) {
            break
        }

        $status = $statusControl.Current.Name
        if ($status -like "Error:*") {
            throw "Search failed: $status"
        }
        if ($status -eq "Found 0 results.") {
            throw "Search returned no results for '$Query'."
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $searchDeadline)

    if ($null -eq $firstResultButton) {
        throw "Timed out waiting for search results. Last status: $($statusControl.Current.Name)"
    }

    Write-Host "Queueing the first search result..."
    $firstResultButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    $queueTab = Wait-ForAutomationId -Parent $window -AutomationId "DownloadQueueTab" -TimeoutSeconds 30
    $queueTab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()

    Write-Host "Starting the first-result download to: $outputDirectory"
    $downloadButton = Wait-ForAutomationId -Parent $window -AutomationId "DownloadSelectedButton" -TimeoutSeconds 30
    $downloadButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()

    $downloadDeadline = [DateTime]::UtcNow.AddSeconds($DownloadTimeoutSeconds)
    $downloadedFile = $null
    do {
        $status = $statusControl.Current.Name
        if ($status -like "Completed 0 of 1 audio files*") {
            throw "The download failed. App status: $status"
        }
        if ($status -like "Could not prepare download dependencies:*") {
            throw "Dependency setup for the download failed: $status"
        }

        $downloadedFile = Get-ChildItem -LiteralPath $outputDirectory -Filter "*.mp3" -File -Recurse |
            Where-Object { $_.Length -gt 0 } |
            Select-Object -First 1
        if ($null -ne $downloadedFile -and $status -like "Completed 1 of 1 audio files*") {
            break
        }

        if ($appProcess.HasExited) {
            throw "The application exited before the download completed. Last status: $status"
        }
        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $downloadDeadline)

    if ($null -eq $downloadedFile) {
        throw "Timed out waiting for a completed MP3. Last status: $($statusControl.Current.Name)"
    }

    Write-Host "PASS: end-to-end search and download completed."
    Write-Output $downloadedFile.FullName
}
finally {
    if ($null -ne $appProcess) {
        $appProcess.Refresh()
        if (-not $appProcess.HasExited) {
            try {
                if ($null -ne $window -and $null -ne $originalOutputDirectory) {
                    $outputControl = Find-ByAutomationId -Parent $window -AutomationId "OutputDirectoryInput"
                    if ($null -ne $outputControl) {
                        $outputControl.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($originalOutputDirectory)
                    }
                }
            }
            catch {
                Write-Warning "Could not restore the previous output-folder setting: $($_.Exception.Message)"
            }

            $null = $appProcess.CloseMainWindow()
            if (-not $appProcess.WaitForExit(10000)) {
                Stop-Process -Id $appProcess.Id -ErrorAction SilentlyContinue
            }
        }
    }
}
