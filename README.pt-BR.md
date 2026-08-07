# RemoteNest

Gerenciador de conexões de Área de Trabalho Remota para Windows. Mantém seus perfis RDP
em um só lugar, organizados em grupos, e abre cada um sem redigitar host e credenciais.

*[Read in English](README.md)*

## Recursos

- Perfis de conexão com grupos, busca e duplicação
- Cerca de 65 configurações `.rdp` por perfil — tela, redirecionamento, gateway,
  experiência, sessão, segurança e RemoteApp — mais um campo livre para qualquer chave
  sem interface própria
- Senhas protegidas com DPAPI do Windows (vinculadas à sua conta, nunca em texto puro)
- Importação e exportação de perfis em JSON, ou importação de um arquivo `.rdp` existente
- Espelhamento de sessão (`mstsc /shadow`)
- Opção que suprime as caixas de aviso do Remote Desktop do Windows, com reversão exata
- Temas claro, escuro e azul-escuro, com transparência acrylic ajustável
- Português (Brasil) e inglês, detectados do Windows na primeira execução

## Download

Baixe na página de [releases](https://github.com/xp3z41x/RemoteNest.Desktop/releases).

| Arquivo | O que é | Requer |
| --- | --- | --- |
| `RemoteNest-Setup.exe` | Instalador. Cria atalhos no menu Iniciar e desinstalador; instala por usuário ou para todos. | [.NET Desktop Runtime 10 (x64)](https://dotnet.microsoft.com/download/dotnet/10.0/runtime) |
| `RemoteNest-Portable.exe` | Arquivo único, sem instalação, runtime embutido. | Nada |
| `RemoteNest-Portable-Slim.exe` | Arquivo único, sem instalação, bem menor. | [.NET Desktop Runtime 10 (x64)](https://dotnet.microsoft.com/download/dotnet/10.0/runtime) |

Windows 10 1809 (build 17763) ou mais recente, 64 bits.

Confira o download com o `SHA256SUMS.txt` da mesma release:

```powershell
Get-FileHash .\RemoteNest-Portable.exe -Algorithm SHA256
```

Os binários não têm assinatura digital, então o SmartScreen pode avisar na primeira
execução e alguns antivírus sinalizam apps .NET de arquivo único por heurística. Compare
o hash ou, se preferir, compile a partir do código.

## Compilar

Requer o [SDK do .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build RemoteNest.sln -c Release
```

```powershell
dotnet test RemoteNest.sln -c Release
```

Gerar os artefatos de release em `dist\` (o instalador precisa do
[Inno Setup 6](https://jrsoftware.org/isinfo.php)):

```powershell
.\scripts\publish.ps1
```

## Dados

Os perfis ficam num banco SQLite em `%APPDATA%\RemoteNest\remotenest.db`, as preferências
no `settings.json` ao lado, e os logs em `%LOCALAPPDATA%\RemoteNest\logs`. Desinstalar o
app não remove esses arquivos; apague as pastas para remover seus dados.

As senhas são cifradas com DPAPI, então só podem ser decifradas pelo mesmo usuário do
Windows na mesma máquina. Copiar o banco para outro computador não leva as senhas junto.

## Avisos de segurança RDP do Windows

Em Configurações há uma opção que desliga os avisos que o Windows mostra antes de cada
conexão: a caixa de redirecionamento/anti-phishing, o pedido de consentimento ao abrir, o
aviso de certificado não verificado e o aviso de dispositivos locais por host dos seus
perfis salvos.

Esses avisos existem para impedir que um `.rdp` malicioso redirecione silenciosamente
suas unidades, área de transferência e credenciais. Só desligue se todas as conexões que
você abre vierem deste app. Os valores originais do registro — inclusive "este valor não
existia" — são salvos antes de qualquer alteração e restaurados integralmente ao religar.
A parte que afeta a máquina toda exige aprovação de administrador.

## Tecnologia

.NET 10, WPF com MVVM (CommunityToolkit.Mvvm), ModernWpfUI para a aparência Fluent e
Microsoft.Data.Sqlite com SQL escrito à mão.

## Licença

Projeto privado.
