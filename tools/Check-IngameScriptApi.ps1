param(
    [Parameter(Mandatory = $true)]
    [string[]] $Roots
)

$ErrorActionPreference = "Stop"
$prohibitedPatterns = @(
    @{ Name = "System.Reflection namespace"; Pattern = "^\s*using\s+System\.Reflection\s*;" },
    @{ Name = "System.Reflection qualified access"; Pattern = "\bSystem\.Reflection\." },
    @{ Name = "MethodInfo"; Pattern = "\bMethodInfo\b" },
    @{ Name = "MethodBase"; Pattern = "\bMethodBase\b" },
    @{ Name = "ConstructorInfo"; Pattern = "\bConstructorInfo\b" },
    @{ Name = "FieldInfo"; Pattern = "\bFieldInfo\b" },
    @{ Name = "PropertyInfo"; Pattern = "\bPropertyInfo\b" },
    @{ Name = "MemberInfo"; Pattern = "\bMemberInfo\b" },
    @{ Name = "ParameterInfo"; Pattern = "\bParameterInfo\b" },
    @{ Name = "EventInfo"; Pattern = "\bEventInfo\b" },
    @{ Name = "BindingFlags"; Pattern = "\bBindingFlags\b" },
    @{ Name = "AppDomain assemblies"; Pattern = "\bAppDomain\s*\.\s*CurrentDomain\s*\.\s*GetAssemblies\s*\(" },
    @{ Name = "Type.GetType"; Pattern = "\bType\s*\.\s*GetType\s*\(" },
    @{ Name = "GetMethod"; Pattern = "\.\s*GetMethod\s*\(" },
    @{ Name = "GetField"; Pattern = "\.\s*GetField\s*\(" },
    @{ Name = "GetProperty"; Pattern = "\.\s*GetProperty\s*\(" },
    @{ Name = "GetConstructor"; Pattern = "\.\s*GetConstructor\s*\(" },
    @{ Name = "GetMember"; Pattern = "\.\s*GetMember\s*\(" },
    @{ Name = "InvokeMember"; Pattern = "\.\s*InvokeMember\s*\(" }
)

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
    Write-Output "error SIUAI000: No C# files found for in-game script API guard. Check the CheckIngameScriptApi MSBuild target roots."
    exit 1
}

$failed = $false
foreach ($file in $files) {
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines($file)) {
        $lineNumber++
        foreach ($entry in $prohibitedPatterns) {
            if ($line -cmatch $entry.Pattern) {
                $failed = $true
                $relative = Resolve-Path -LiteralPath $file -Relative
                $message = "Medieval Engineers script compiler rejects this reflection API; use direct game/mod APIs instead."
                Write-Output "$relative($lineNumber,1): error SIUAI001: Prohibited in-game script API '$($entry.Name)'. $message"
            }
        }
    }
}

if ($failed) {
    exit 1
}
