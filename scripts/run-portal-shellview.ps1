# Lance Naultinus avec le corps de portail rendu par la vue du shell.
#
# Regles retenues par mesure sur ce poste :
#  - construire dans WSL (5 s) ; construire depuis Windows passe par le pont 9p, tres lent, et
#    laisse un arbre bin/ incomplet s'il est interrompu ;
#  - executer sur le poste, mais sur une COPIE LOCALE : le chemin UNC \\wsl.localhost... est vu par
#    fois comme absent (cache du pont 9p), ce qui fait partir dotnet.exe sans rien afficher.
param([switch]$Build)
$ErrorActionPreference = 'Stop'

$src = '\\wsl.localhost\Ubuntu-24.04\home\pc\Naultinus\Naultinus.Application\bin\Debug\net10.0-windows10.0.17763.0'
$dst = 'C:\Users\PC\Naultinus-run'

if ($Build) {
    Write-Host 'Construction dans WSL...' -ForegroundColor Cyan
    wsl.exe -d Ubuntu-24.04 bash -lc 'cd ~/Naultinus && dotnet build Naultinus.Application -p:EnableWindowsTargeting=true --nologo -v q'
    if ($LASTEXITCODE -ne 0) { throw 'La construction a echoue.' }
}

Write-Host "Copie locale vers $dst..." -ForegroundColor Cyan
robocopy $src $dst /MIR /R:5 /W:2 /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copie impossible (robocopy $LASTEXITCODE). L'arbre de build est-etre vide : relance avec -Build." }

$dll = Join-Path $dst 'Naultinus.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "Naultinus.dll absente de $dst : relance avec -Build." }

$env:NAULTINUS_SHELLVIEW = '1'
Write-Host "Execution (corps de portail = vue du shell). Journal : $env:TEMP\Naultinus_startup.log" -ForegroundColor Green
& 'C:\Program Files\dotnet\dotnet.exe' $dll
