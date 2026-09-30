param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

function Require-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$projectPath = Join-Path $RepositoryRoot 'src\SprocketPartClipboard\SprocketPartClipboard.csproj'
$testProjectPath = Join-Path $RepositoryRoot 'tests\SprocketPartClipboard.ContractTests\SprocketPartClipboard.ContractTests.csproj'
$solutionPath = Join-Path $RepositoryRoot 'SprocketPartClipboard.slnx'
$readmePath = Join-Path $RepositoryRoot 'README.md'
$usagePath = Join-Path $RepositoryRoot 'docs\usage.md'
$outputDirectory = Join-Path $RepositoryRoot ('src\SprocketPartClipboard\bin\' + $Configuration + '\net6.0')

foreach ($requiredPath in @(
    $projectPath,
    $testProjectPath,
    $solutionPath,
    $readmePath,
    $usagePath,
    (Join-Path $RepositoryRoot 'src\SprocketPartClipboard\PartClipboardMod.cs'),
    (Join-Path $RepositoryRoot 'src\SprocketPartClipboard\AssemblyInfo.cs'),
    (Join-Path $RepositoryRoot 'src\SprocketPartClipboard\Clipboard\ClipboardLibraryStore.cs'),
    (Join-Path $RepositoryRoot 'src\SprocketPartClipboard\Clipboard\ClipboardLibrary.cs'),
    (Join-Path $RepositoryRoot 'src\SprocketPartClipboard\Clipboard\ClipboardEntry.cs'),
    (Join-Path $RepositoryRoot 'src\SprocketPartClipboard\Designer\DesignerPartAccess.cs'),
    (Join-Path $RepositoryRoot 'src\SprocketPartClipboard\Designer\PartTreeClipboardService.cs'),
    (Join-Path $RepositoryRoot 'src\SprocketPartClipboard\Designer\VehicleBlueprintCodec.cs')
)) {
    Require-True (Test-Path -LiteralPath $requiredPath) `
        ('Required single-assembly file is missing: ' + $requiredPath)
}

$projectText = Get-Content -LiteralPath $projectPath -Raw -Encoding UTF8
$testProjectText = Get-Content -LiteralPath $testProjectPath -Raw -Encoding UTF8
$usageText = Get-Content -LiteralPath $usagePath -Raw -Encoding UTF8

Require-True ($projectText.Contains('<Version>0.1.0-fix1</Version>')) `
    'Single-assembly package version is not 0.1.0-fix1.'
Require-True ($testProjectText.Contains('src\SprocketPartClipboard\SprocketPartClipboard.csproj')) `
    'Contract tests do not reference the shipping assembly.'

$requiredUsageItems = @(
    'SprocketPartClipboard.dll',
    'SprocketModAPI',
    'Ctrl+C',
    'Ctrl+V',
    'UserData\SprocketPartClipboard\library.json',
    '[PartClipboard]'
)
foreach ($requiredUsage in $requiredUsageItems) {
    Require-True ($usageText.Contains($requiredUsage)) `
        ('Usage documentation contract is missing: ' + $requiredUsage)
}

if (Test-Path -LiteralPath $outputDirectory) {
    Require-True (Test-Path -LiteralPath `
        (Join-Path $outputDirectory 'SprocketPartClipboard.dll')) `
        'Shipping DLL is missing from the build output.'
}

Write-Output (
    'PASS package=single-assembly version=0.1.0-fix1 ' +
    'contracts=shipping-assembly docs=usage-complete'
)
