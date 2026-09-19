# Empacota o mod no formato Thunderstore, para o r2modman importar como "local mod".
#
# Sem isso o DLL ate carrega (o BepInEx varre a pasta plugins), mas o r2modman
# nao lista nem deixa ligar/desligar -- ele so conhece o que esta no mods.yml.
# O mesmo zip serve para mandar para outra pessoa.
#
# Uso: .\pack.ps1

$ErrorActionPreference = 'Stop'
$raiz  = Split-Path -Parent $MyInvocation.MyCommand.Path
$dll   = Join-Path $raiz "bin\Release\ValheimTweaks.dll"

# Versao vem do csproj, para nao divergir do que o plugin reporta no log.
$versao = ([xml](Get-Content (Join-Path $raiz "ValheimTweaks.csproj"))).Project.PropertyGroup.Version |
          Where-Object { $_ } | Select-Object -First 1

# Compila Release aqui dentro. Antes o script so LIA bin\Release e avisava para
# compilar antes; como o dia a dia e `dotnet build` (que gera Debug), o Release
# ficou parado por dias e varios zips sairam com DLL velho e manifest novo.
# O sintoma enganava: o r2modman mostrava a versao nova (le o manifest) e o
# Configuration Manager mostrava a velha (le o DLL), o que parece mod duplicado.
Write-Host "compilando Release..."
& dotnet build -c Release -v minimal | Out-Null
if ($LASTEXITCODE -ne 0) { throw "a compilacao Release falhou; zip nao gerado" }
if (-not (Test-Path $dll)) { throw "bin\Release\ValheimTweaks.dll nao apareceu" }

# Cinto de seguranca: confere que o DLL empacotado E a versao do csproj. Um zip
# com versao errada custa uma rodada de teste da outra pessoa.
$verDll = [Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString(3)
if ($verDll -ne $versao) {
    throw "DLL em bin\Release e $verDll mas o csproj diz $versao. Zip abortado."
}
Write-Host "  DLL conferido: $verDll"

$saida = Join-Path $raiz "dist"
$stage = Join-Path $saida "stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

# --- manifest.json (formato Thunderstore) ---
# description tem limite de 250 caracteres.
$manifest = [ordered]@{
    name           = "ValheimTweaks"
    version_number = $versao
    website_url    = ""
    description    = "Video and network tweaks the Valheim menu does not offer: anisotropic filtering, quality anti-aliasing, adjustable fog, exclusive fullscreen, network timeout and simulation distance."
    dependencies   = @(
        "denikson-BepInExPack_Valheim-5.4.2350",
        # O menu de opcoes (F1) vem daqui; sem ele so da para editar o .cfg na mao.
        "shudnal-ConfigurationManager-1.1.18"
    )
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $stage "manifest.json") -Encoding UTF8

# --- icon.png: o Thunderstore exige exatamente 256x256 ---
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.Clear([System.Drawing.Color]::FromArgb(255, 26, 32, 40))
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 214, 158, 74)), 10
# Marca simples: losango (escudo) com uma barra, legivel em miniatura.
$g.DrawPolygon($pen, @(
    (New-Object System.Drawing.Point(128, 34)),
    (New-Object System.Drawing.Point(214, 128)),
    (New-Object System.Drawing.Point(128, 222)),
    (New-Object System.Drawing.Point(42, 128))
))
$g.DrawLine($pen, 128, 78, 128, 178)
$g.Dispose()
$bmp.Save((Join-Path $stage "icon.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# FINDINGS.md fica so no repositorio: e nota de investigacao, nao documentacao de uso.
Copy-Item (Join-Path $raiz "README.md") (Join-Path $stage "README.md") -Force
Copy-Item $dll (Join-Path $stage "ValheimTweaks.dll") -Force

$zip = Join-Path $saida "ValheimTweaks-$versao.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
Remove-Item $stage -Recurse -Force

"pacote: $zip"
Get-ChildItem $zip | Select-Object Name, @{n='KB';e={[math]::Round($_.Length/1KB,1)}} | Format-Table -AutoSize
