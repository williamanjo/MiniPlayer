# MiniPlayer

Mini player para Windows 11 que mostra e controla a música tocando no navegador
(YouTube, Spotify Web, SoundCloud, Deezer, ...). Sem extensão: lê os controles de mídia
do Windows (SMTC), onde Chrome/Edge/Firefox/Brave/Opera publicam a Media Session.

## Recursos
- Título, artista e navegador de origem
- Play/Pause, anterior, próxima, aleatório, repetir (lista → faixa → off)
- Barra de progresso clicável (seek)
- **Fixar no topo** (checkbox)
- **Modo barra de tarefas**: player compacto sobre a taskbar, ao lado da bandeja
  - arraste o texto para mover na horizontal; clique direito → menu
  - clique duplo (fora dos botões) → volta ao modo normal
  - várias telas: escolha a barra em clique direito → "Mover para a tela" ou na bandeja →
    "Barra de tarefas da tela" (requer "Mostrar barra de tarefas em todas as telas" no Windows)
  - some sozinho em tela cheia e com taskbar auto-ocultável escondida
  - cor do texto segue tema claro/escuro do Windows
- Ícone na bandeja: mostrar player, alternar modo, fixar no topo, iniciar com Windows, sair
- Prioriza sessão de navegador tocando; troca automaticamente ao tocar em outra aba
- **Escolher aba/app**: clique no artista (▾) ou clique direito → "Tocando em"; "Automático" volta ao padrão
- **Volume**: roda do mouse sobre o player muda o volume só do app que toca (Mixer de Volume);
  clique do meio = mudo
- **Ocultar quando nada toca** (modo barra): some após 3 s sem mídia ou 30 s pausado
- **Letras sincronizadas** (botão 🎤 / "Mostrar letra na barra"): via [LRCLIB](https://lrclib.net).
  Desligado por padrão; quando ligado envia título/artista ao lrclib.net

## Temas
5 temas embutidos: **Escuro**, **Claro**, **Vidro (capa)** (capa desfocada no fundo), **Neon** e **Terminal**.
Escolha em: bandeja → Temas, ou clique direito no player.

### Criar um tema
Temas são arquivos `.json` em `%APPDATA%\MiniPlayer\themes` (ou numa pasta `themes` ao lado do exe).
A pasta já vem com `exemplo-oceano.json` comentado. Copie, mude `name` e as cores — salvar recarrega na hora.

| Campo | Exemplo | Descrição |
|---|---|---|
| `name`, `author` | `"Oceano"` | Nome no menu e autor |
| `background` | `"#F21F1F1F"` | Cor do fundo (`#AARRGGBB` aceita transparência) |
| `backgroundGradient` | `["#082032", "#137C8B"]` | Degradê diagonal (substitui `background`) |
| `border`, `borderThickness`, `cornerRadius` | `"#33FFFFFF"`, `1`, `10` | Borda e cantos |
| `foreground`, `secondaryForeground` | `"#FFFFFF"` | Texto principal e secundário |
| `accent`, `track`, `hover` | `"#60CDFF"` | Destaques, trilho do progresso, hover dos botões |
| `coverRadius` | `6` | Cantos da capa |
| `fontFamily` | `"Cascadia Mono"` | Fonte (precisa estar instalada) |
| `shadowOpacity` | `0.45` | Sombra (0 = sem) |
| `backdrop` | `"none"` / `"cover"` / `"image"` | Fundo: nada, capa desfocada, ou imagem |
| `backgroundImage` | `"fundo.png"` | Imagem na mesma pasta do tema (com `backdrop: "image"`) |
| `overlay` | `"#99000000"` | Película sobre capa/imagem p/ legibilidade |

Temas são só dados (JSON, não XAML), então um tema de terceiros não executa código.
Arquivos inválidos aparecem com ⚠ no menu de temas.

## Build
Requer .NET 10 SDK.

```bash
dotnet build
dotnet publish -c Release
```

Exe único: `bin/Release/net10.0-windows10.0.22621.0/win-x64/publish/MiniPlayer.exe` (self-contained, não precisa de .NET instalado).

Configurações: `%APPDATA%\MiniPlayer\settings.json`.

## Limitações
- **Aleatório/Repetir**: navegadores geralmente não expõem esses controles ao Windows,
  então os botões ficam desabilitados (cinza) para YouTube/Spotify Web etc. Funcionam
  com apps que suportam (ex.: Spotify desktop).
- Barra de progresso só aparece quando o site informa duração (Media Session `setPositionState`).
- Windows 11 não permite embutir janelas dentro da taskbar; o modo barra de tarefas é uma
  janela sempre-no-topo posicionada sobre ela (reafirma posição a cada 0,5 s).
  Taskbar vertical não é suportada nesse modo.
