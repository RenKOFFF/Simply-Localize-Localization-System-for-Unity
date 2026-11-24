# Windows x64 acceptance. Creates two disposable projects outside the repository.
param(
    [Parameter(Mandatory)] [string] $UnityEditor,
    [string] $ResultsDirectory = [IO.Path]::GetTempPath()
)
$ErrorActionPreference = 'Stop'
$package = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../../../src/SimplyLocalize')).Path.Replace('\', '/')
$results = Join-Path $ResultsDirectory ('SL-Chapter-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$utf8 = New-Object System.Text.UTF8Encoding($false)

function Invoke-Hidden([string] $exe, [string[]] $arguments, [int] $timeout, [string] $log) {
    $quoted = $arguments | ForEach-Object { '"' + $_ + '"' }
    $process = Start-Process -FilePath $exe -ArgumentList $quoted -PassThru -WindowStyle Hidden
    if (!$process.WaitForExit($timeout * 1000)) {
        $process.Kill()
        throw "Timed out. See $log"
    }
    if ($process.ExitCode -ne 0) { throw "Exit code $($process.ExitCode). See $log" }
}

function Write-Wave([string] $path) {
    $writer = [IO.BinaryWriter]::new([IO.File]::Create($path))
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF'))
        $writer.Write([int]1636)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
        $writer.Write([int]16)
        $writer.Write([int16]1)
        $writer.Write([int16]1)
        $writer.Write([int]8000)
        $writer.Write([int]16000)
        $writer.Write([int16]2)
        $writer.Write([int16]16)
        $writer.Write([Text.Encoding]::ASCII.GetBytes('data'))
        $writer.Write([int]1600)
        for ($sample=0; $sample -lt 800; $sample++) {
            $writer.Write([int16](2000 * [Math]::Sin(2 * [Math]::PI * 440 * $sample / 8000)))
        }
    } finally { $writer.Dispose() }
}

Write-Output "Chapter audio verification: $results"
foreach ($variant in 'WithAddressables', 'ResourcesOnly') {
    $project = Join-Path $results $variant
    foreach ($folder in 'Assets/Editor','Packages','ProjectSettings') {
        [IO.Directory]::CreateDirectory((Join-Path $project $folder)) | Out-Null
    }
    $dependencies = @{ 'com.renkoff.simply-localize' = "file:$package"; 'com.unity.ugui' = '2.0.0' }
    if ($variant -eq 'WithAddressables') {
        $dependencies['com.unity.addressables'] = '2.7.6'
        [IO.File]::WriteAllText("$project/Assets/csc.rsp", '-define:TEST_ADDRESSABLES', $utf8)
    }
    [IO.File]::WriteAllText("$project/Packages/manifest.json", (@{dependencies=$dependencies} | ConvertTo-Json), $utf8)
    [IO.File]::WriteAllLines("$project/ProjectSettings/ProjectVersion.txt", [string[]]@('m_EditorVersion: 6000.0.62f1'), $utf8)
    Copy-Item -LiteralPath "$PSScriptRoot/ChapterAudioProbe.cs" -Destination "$project/Assets"
    Copy-Item -LiteralPath "$PSScriptRoot/Editor/ChapterAudioBuild.cs" -Destination "$project/Assets/Editor"
    Write-Wave "$project/Assets/Narration.wav"
    $buildLog = "$project/build.log"
    $playerLog = "$project/player.log"
    Invoke-Hidden $UnityEditor @('-batchmode','-nographics','-quit','-projectPath',$project,'-executeMethod','ChapterAudioBuild.Run','-logFile',$buildLog) 900 $buildLog
    foreach ($assembly in 'Unity.Addressables.dll', 'Unity.ResourceManager.dll', 'SimplyLocalize.Addressables.dll') {
        $present = Test-Path -LiteralPath "$project/Build/Chapter_Data/Managed/$assembly"
        if ($present -ne ($variant -eq 'WithAddressables')) { throw "Unexpected $assembly presence in $variant" }
    }
    Invoke-Hidden "$project/Build/Chapter.exe" @('-batchmode','-nographics','-logFile',$playerLog) 120 $playerLog
    if (!(Select-String -LiteralPath $playerLog -SimpleMatch 'PASS: chapter audio lifecycle' -Quiet)) { throw "Missing result in $playerLog" }
    Write-Output "$variant passed"
}
