# Lance Naultinus avec le corps de portail rendu par la vue du shell.
#
# Regles retenues par mesure sur ce poste :
#  - construire dans WSL (6 s) ; construire depuis Windows passe par le pont 9p, tres lent, et une
#    construction interrompue laisse un bin\ sans Naultinus.dll ;
#  - executer sur une COPIE LOCALE : le chemin UNC \\wsl.localhost...\bin\ est vu parfois comme
#    absent (cache du pont 9p) et dotnet.exe rend alors la main sans rien afficher ;
#  - arreter les copies deja lancées : le mutex Local\Naultinus.SingleInstance ferme aussitôt la
#    copie de developpement si l'application installee tourne (journal : « Instance deja en cours »).
param(
    [switch]$Build,        # construit d'abord dans WSL
    [switch]$KeepRunning   # ne pas arreter les copies en cours
)
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
if ($LASTEXITCODE -ge 8) { throw "Copie impossible (robocopy $LASTEXITCODE). Arbre de build vide ? Relancez avec -Build." }

$dll = Join-Path $dst 'Naultinus.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "Naultinus.dll absente de $dst. Relancez avec -Build." }

if (-not $KeepRunning) {
    $ids = @(
        (Get-Process Naultinus -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
        (Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" |
            Where-Object { $_.CommandLine -like '*Naultinus.dll*' } | ForEach-Object { $_.ProcessId })
    )
    if ($ids.Count -gt 0) {
        Write-Host ("Copie(s) deja en cours arrete(s) : " + ($ids -join ', ')) -ForegroundColor Yellow
        $ids | ForEach-Object { Stop-Process -Id $_ -Force -ErrorAction SilentlyContinue }
        Start-Sleep -Seconds 2
    }
}

$env:NAULTINUS_SHELLVIEW = '1'
Write-Host "Execution. Journal : $env:TEMP\Naultinus_startup.log" -ForegroundColor Green
& 'C:\Program Files\dotnet\dotnet.exe' $dll
