# OpenTranslate

Windows clipboard translator powered by [MyMemory](https://mymemory.translated.net/) (free, no API key) or AI models via [OpenRouter](https://openrouter.ai/), [OpenAI](https://platform.openai.com/), or [Gemini](https://aistudio.google.com/).

Copy text with **Ctrl+C** twice in quick succession (within 500 ms) and the app translates the clipboard contents, replaces them with the translation, and pastes it automatically into the active application.

When the target is not editable (e.g. a web page or PDF), a floating **translation tooltip** appears near the cursor instead of pasting.

## Requirements

- Windows 10/11 (64-bit)
- No API key required for the default **MyMemory** provider
- API key from [OpenRouter](https://openrouter.ai/keys), [OpenAI](https://platform.openai.com/api-keys), or [Gemini](https://aistudio.google.com/apikey) for AI providers

> End users installing the release build do not need the .NET SDK.

## Install

Download `OpenTranslate-Setup-x.y.z.exe` from [GitHub Releases](https://github.com/nanof/OpenTranslate/releases) and run the installer.

Settings are preserved across upgrades at `%AppData%\OpenTranslate\settings.dat`.

## Usage

1. Run the application. A **T** icon appears in the system tray.
2. Open **Settings…** (right-click the icon or double-click). Out of the box, **MyMemory** translates from **Spanish** to **English** with no setup.
3. To use AI models, switch to OpenRouter, OpenAI, or Gemini in the **Provider** tab and enter your API key.
4. Select text in any app, press **Ctrl+C** twice quickly, and the translation will be pasted automatically (or shown in the tooltip if the field is read-only).

You can also use **Translate clipboard now** from the context menu to manually translate whatever is on the clipboard.

### Translation tooltip

For non-editable targets, OpenTranslate shows a floating tooltip with:

- **Copy** and **Replace** actions (Replace is hidden when the target cannot be edited)
- **Modes** panel (AI providers only) to preview variants: fix grammar, natural tone, concise, formal, casual, summarize, explain in source/target language, or improve without translating
- Resizable window that remembers your last size
- macOS-style entrance animation and a Matrix-style glyph spinner while loading

## Settings

Settings are stored encrypted at:

```
%AppData%\OpenTranslate\settings.dat
```

The settings window is organized into tabs:

### Provider

| Field | Description |
|-------|-------------|
| Provider | MyMemory (free), OpenRouter, OpenAI, or Gemini (Google) |
| API key | Key for the selected AI provider (not required for MyMemory) |
| Model | Model ID for AI providers (default: `google/gemini-3.1-flash-lite` on OpenRouter, `gpt-4o-mini` on OpenAI, `gemini-3.1-flash-lite` on Gemini). The settings window shows a speed/latency hint for the selected model. |
| Improve text (AI) | Optional post-processing: fix spelling & grammar, natural tone, concise, formal, casual, or improve only without translating |

### Languages

| Field | Description |
|-------|-------------|
| Source language | Language code, e.g. `es` |
| Target language | Language code, e.g. `en` |
| Auto-detect source | Detect source language automatically and translate in either direction between the configured pair (works with all providers, including MyMemory) |
| Preserve code, URLs, paths, and mentions | Keep code blocks, URLs, file paths, `@mentions`, Slack links, and Markdown formatting intact during translation |

### Behavior

| Field | Description |
|-------|-------------|
| Start with Windows | Register the app to run at login |
| Play sound when translation starts | Audible feedback on each translation |
| Typewriter paste | Paste the translation character by character |
| Tooltip font size | Font size for the floating tooltip |
| Keyboard shortcut | Customizable activation shortcut (default: double **Ctrl+C**) |

### Usage

Local usage tracking with daily and monthly estimates (characters, approximate tokens, translation count). Data stays on your device only.

### About

App version, project link, and copyright.

## What's new in 1.2.0

- **MyMemory** as the free default provider — works immediately, no API key
- **Translation tooltip** for read-only content with Copy, Replace, and AI **Modes** previews
- **AI text improvement** modes (grammar, tone, concise, formal, casual, improve-only)
- **Summarize** and **explain** modes in the tooltip Modes panel
- **Preserve formatting**: code, URLs, paths, mentions, Slack links, and Markdown
- **Local usage tracking** with daily/monthly estimates in Settings → Usage
- **Settings tabs** (Provider, Languages, Behavior, Usage, About)
- Resizable tooltip that remembers size, themed scrollbar, and snappier loading spinner

## Build and publish

```bash
dotnet build
```

### Create installer locally

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
.\scripts\build-installer.ps1 -Version 1.2.0
```

The installer will be at `dist/OpenTranslate-Setup-1.2.0.exe`.

### Publish only (no installer)

```bash
dotnet publish src/OpenTranslate/OpenTranslate.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable will be at `src/OpenTranslate/bin/Release/net8.0-windows/win-x64/publish/`.

### Release on GitHub

Push a version tag to trigger the release workflow:

```bash
git tag v1.2.0
git push origin v1.2.0
```

This builds the installer and attaches it to a GitHub Release automatically.

## Privacy

Copied text is sent to the configured provider (MyMemory, OpenRouter, OpenAI, or Gemini) for translation. It is not stored locally beyond the clipboard, encrypted settings, and optional local usage statistics (character counts on your device only).

## License

MIT
