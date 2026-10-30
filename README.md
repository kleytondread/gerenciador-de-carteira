# Gerenciador de Carteira

Acompanhamento de carteira de investimentos da B3 (ações, FIIs, ETFs, BDRs e Tesouro Direto): posições, rentabilidade
comparada a CDI/IBOV/IPCA, proventos, eventos corporativos e os dados para a declaração do Imposto de Renda.
Roda no seu computador, com os dados num banco SQLite local. Cotações vêm do Yahoo Finance, os títulos do Tesouro
Transparente e os índices do Banco Central.

## Download

Na página de [Releases](../../releases), escolha um:

| Arquivo | Para quem |
|---|---|
| `GerenciadorDeCarteira-Setup-<versão>.exe` | **Windows (recomendado).** App com janela própria. Instala sem pedir administrador. |
| `GerenciadorDeCarteira-Web-<versão>-win-x64.zip` | Versão web no Windows, usada pelo navegador |
| `GerenciadorDeCarteira-Web-<versão>-linux-x64.tar.gz` | Versão web no Linux, usada pelo navegador |

Nenhum deles precisa do .NET instalado.

### App Windows

Rode o instalador e abra pelo Menu Iniciar. As cotações e índices são atualizados enquanto o app está aberto
(diariamente às 20h, horário de Brasília). Se o horário passar com o app fechado, a atualização roda quando você
abrir. Para atualizar, rode o instalador da versão nova por cima. Desinstalar (Configurações › Apps) não apaga os
seus dados.

### Versão web

**Windows:** extraia o `.zip`, rode `GerenciadorDeCarteira.exe` e abra `http://localhost:8888` no navegador. A
janela de console mostra que está rodando. Para parar, use Ctrl+C.

**Linux:**

```bash
mkdir gerenciador-carteira && tar -xzf GerenciadorDeCarteira-Web-*-linux-x64.tar.gz -C gerenciador-carteira
cd gerenciador-carteira && chmod +x GerenciadorDeCarteira && ./GerenciadorDeCarteira
```

Depois abra `http://localhost:8888`. O app só aceita conexões do próprio computador e não tem login: não exponha
essa porta na rede.

Use **uma** versão de cada vez (app Windows ou web). As duas usam a mesma pasta de dados.

## Onde ficam os dados

- Windows: `%LOCALAPPDATA%\GerenciadorDeCarteira` (banco `gestorcarteira.db`, preços do Tesouro e, no app Windows, `logs`)
- Linux: `~/.local/share/GerenciadorDeCarteira`

Backup: copie essa pasta com o app fechado.

## Primeiros passos

Em **Configurações**, cadastre a corretora (nome e CNPJ). Ela é a fonte pagadora dos títulos do Tesouro Direto na
tela de Imposto de Renda. Se você usa mais de uma, marque a **principal**: títulos novos vêm com ela, e você troca
no cadastro do título quando ele estiver em outra.

## Desenvolvimento

Requer o .NET SDK 10. A solução está em `GerenciadorDeCarteira/GerenciadorDeCarteira.slnx`:

| Projeto | O que é |
|---|---|
| `Wal.Domain`, `Wal.Data`, `Wal.ExternalApis`, `Wal.Services` | Entidades, banco (EF Core + SQLite), APIs externas e regras |
| `Wal.Jobs` | Jobs do Hangfire e o registro de serviços usado pelos dois apps (`ServicosCarteira`) |
| `Wal.UI` | As telas (Blazor + MudBlazor), com CSS e JS |
| `GerenciadorDeCarteira` (`WalletManager.csproj`) | Versão web (Blazor Server), com painel `/hangfire` e API `/api` |
| `GerenciadorDeCarteira.Desktop` | App Windows (WPF + BlazorWebView) |
| `Wal.Tests` | Testes |

Rodando pelo Visual Studio ou `dotnet run`, os dois apps usam o banco de desenvolvimento, que fica na pasta do
projeto web (`GerenciadorDeCarteira/GerenciadorDeCarteira/gestorcarteira.db`).

```bash
dotnet run --project GerenciadorDeCarteira/GerenciadorDeCarteira/WalletManager.csproj --launch-profile http
```

```bash
dotnet run --project GerenciadorDeCarteira/GerenciadorDeCarteira.Desktop/GerenciadorDeCarteira.Desktop.csproj
```

```bash
dotnet test GerenciadorDeCarteira/GerenciadorDeCarteira.slnx
```

### Gerar os pacotes

Com o [Inno Setup 6](https://jrsoftware.org/isinfo.php) instalado (`winget install JRSoftware.InnoSetup`):

1. Ajuste `<Version>` no `Directory.Build.props`.
2. Rode `.\publicar.ps1`. Ele roda os testes e gera os três pacotes em `artifacts\`.
3. Faça commit e push do código da versão. Depois crie a Release com o comando que o script mostra no fim
   (`gh release create v<versão> artifacts\GerenciadorDeCarteira-* ...`).
