param(
    [Alias("Root")]
    [string[]] $Roots = @(".\mod\Data"),
    [switch] $WhatIf
)

$ErrorActionPreference = "Stop"
$markerPattern = '//\s*AGENT-DEBUG-LOG\b'

function Get-Newline {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Text
    )

    if ($Text.Contains("`r`n")) {
        return "`r`n"
    }

    if ($Text.Contains("`n")) {
        return "`n"
    }

    return [Environment]::NewLine
}

function Read-FileWithEncoding {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $reader = [System.IO.StreamReader]::new($Path, $true)
    try {
        $text = $reader.ReadToEnd()
        $encoding = $reader.CurrentEncoding
    }
    finally {
        $reader.Dispose()
    }

    return @{
        Text = $text
        Encoding = $encoding
    }
}

$expandedRoots = New-Object 'System.Collections.Generic.List[string]'
foreach ($root in $Roots) {
    foreach ($expandedRoot in ($root -split "[;,]")) {
        $trimmed = $expandedRoot.Trim()
        if ($trimmed.Length -gt 0) {
            $expandedRoots.Add($trimmed)
        }
    }
}

$files = New-Object 'System.Collections.Generic.List[string]'
foreach ($root in $expandedRoots) {
    if (-not (Test-Path -LiteralPath $root)) {
        continue
    }

    Get-ChildItem -LiteralPath $root -Recurse -Filter *.cs -File |
        ForEach-Object { $files.Add($_.FullName) }
}

if ($files.Count -eq 0) {
    Write-Output "No C# files found under the requested roots."
    exit 0
}

$touchedFiles = 0
$removedLines = 0

foreach ($file in $files) {
    $content = Read-FileWithEncoding -Path $file
    $text = $content.Text
    $newline = Get-Newline -Text $text
    $endsWithNewline = $text.EndsWith("`r`n") -or $text.EndsWith("`n")
    $lines = [System.Text.RegularExpressions.Regex]::Split($text, "\r?\n")
    $keptLines = New-Object 'System.Collections.Generic.List[string]'
    $fileRemovedLines = 0

    foreach ($line in $lines) {
        if ($line -match $markerPattern) {
            $fileRemovedLines++
            continue
        }

        $keptLines.Add($line)
    }

    if ($fileRemovedLines -eq 0) {
        continue
    }

    $touchedFiles++
    $removedLines += $fileRemovedLines
    $relative = Resolve-Path -LiteralPath $file -Relative
    Write-Output "${relative}: removing $fileRemovedLines marked debug log line(s)."

    if ($WhatIf) {
        continue
    }

    $updatedText = [string]::Join($newline, $keptLines)
    if ($endsWithNewline -and -not $updatedText.EndsWith($newline)) {
        $updatedText += $newline
    }

    $writer = [System.IO.StreamWriter]::new($file, $false, $content.Encoding)
    try {
        $writer.Write($updatedText)
    }
    finally {
        $writer.Dispose()
    }
}

if ($touchedFiles -eq 0) {
    Write-Output "No marked debug log lines found."
    exit 0
}

if ($WhatIf) {
    Write-Output "Preview complete: $removedLines marked debug log line(s) would be removed from $touchedFiles file(s)."
    exit 0
}

Write-Output "Removed $removedLines marked debug log line(s) from $touchedFiles file(s)."
