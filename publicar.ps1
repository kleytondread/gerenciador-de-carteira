# Gera os pacotes de uma versão em artifacts\:
#   - GerenciadorDeCarteira-Setup-<versão>.exe          instalador da versão Windows
#   - GerenciadorDeCarteira-Web-<versão>-win-x64.zip     versão web para Windows
#   - GerenciadorDeCarteira-Web-<versão>-linux-x64.tar.gz versão web para Linux
# A versão vem do Directory.Build.props. Não publica nada no GitHub: no fim mostra o comando da Release.
#
# Uso:  .\publicar.ps1            (roda os testes antes)
#       .\publicar.ps1 -SemTestes
param([switch]$SemTestes)

$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
$versao = ([xml](Get-Content "$raiz\Directory.Build.props")).Project.PropertyGroup.Version
$saida = "$raiz\artifacts"
$publicacao = "$saida\publish"
$web = "$raiz\GerenciadorDeCarteira\GerenciadorDeCarteira\WalletManager.csproj"
$desktop = "$raiz\GerenciadorDeCarteira\GerenciadorDeCarteira.Desktop\GerenciadorDeCarteira.Desktop.csproj"

function Executar([string]$descricao, [scriptblock]$comando) {
    Write-Host "`n== $descricao" -ForegroundColor Cyan
    & $comando
    if ($LASTEXITCODE -ne 0) { throw "Falhou: $descricao (código $LASTEXITCODE)" }
}

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 não encontrado. Instale com: winget install JRSoftware.InnoSetup" }

Write-Host "Gerenciador de Carteira $versao" -ForegroundColor Green
if (Test-Path $saida) { Remove-Item $saida -Recurse -Force }
New-Item -ItemType Directory $saida | Out-Null

if (-not $SemTestes) {
    Executar 'Testes' { dotnet test "$raiz\GerenciadorDeCarteira\GerenciadorDeCarteira.slnx" -c Release --nologo -v q }
}

# Self-contained: quem instala não precisa ter o .NET. Sem "single file" (o instalador já junta tudo num arquivo só,
# e o WebView2 com single file é uma combinação arriscada, segundo o jrstudio).
Executar 'Versão Windows: publicação' {
    dotnet publish $desktop -c Release -r win-x64 --self-contained -p:PublishSingleFile=false -o "$publicacao\desktop" --nologo -v q
}
Executar 'Versão Windows: instalador' {
    & $iscc /Q "/DMyAppVersion=$versao" "/DPastaPublicacao=$publicacao\desktop" "/DPastaSaida=$saida" "$raiz\installer\GerenciadorDeCarteira.iss"
}

Executar 'Versão web para Windows' {
    dotnet publish $web -c Release -r win-x64 --self-contained -o "$publicacao\web-win-x64" --nologo -v q
}
Compress-Archive -Path "$publicacao\web-win-x64\*" -DestinationPath "$saida\GerenciadorDeCarteira-Web-$versao-win-x64.zip"

Executar 'Versão web para Linux' {
    dotnet publish $web -c Release -r linux-x64 --self-contained -o "$publicacao\web-linux-x64" --nologo -v q
}
Executar 'Versão web para Linux: compactação' {
    tar -czf "$saida\GerenciadorDeCarteira-Web-$versao-linux-x64.tar.gz" -C "$publicacao\web-linux-x64" .
}

Write-Host "`n== Pacotes gerados" -ForegroundColor Green
Get-ChildItem $saida -File | ForEach-Object { '{0,-55} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }

Write-Host "`nPara publicar no GitHub (depois de commit e push do código desta versão):" -ForegroundColor Yellow
Write-Host "gh release create v$versao artifacts\GerenciadorDeCarteira-* --title `"Gerenciador de Carteira v$versao`" --notes `"...`""
