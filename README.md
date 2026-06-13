# OpenTranslate

Windows clipboard translator powered by AI models via [OpenRouter](https://openrouter.ai/), [OpenAI](https://platform.openai.com/), or [Gemini](https://aistudio.google.com/).

Copy text with **Ctrl+C** twice in quick succession (within 500 ms) and the app translates the clipboard contents, replaces them with the translation, and pastes it automatically into the active application.

## Requirements

- Windows 10/11 (64-bit)
- API key from [OpenRouter](https://openrouter.ai/keys), [OpenAI](https://platform.openai.com/api-keys), or [Gemini](https://aistudio.google.com/apikey)

> End users installing the release build do not need the .NET SDK.

## Install

Download `OpenTranslate-Setup-x.y.z.exe` from [GitHub Releases](https://github.com/nanof/OpenTranslate/releases) and run the installer.

Settings are preserved across upgrades at `%AppData%\OpenTranslate\settings.dat`.

## Usage

1. Run the application. A **T** icon appears in the system tray.
2. Open **Settings…** (right-click the icon or double-click) and enter your API key.
3. By default, it translates from **Spanish** to **English** using the `google/gemini-3.1-flash-lite` model (a low-latency model well suited to translation).
4. Select text in any app, press **Ctrl+C** twice quickly, and the translation will be pasted automatically.

You can also use **Translate clipboard now** from the context menu to manually translate whatever is on the clipboard.

## Settings

Settings are stored encrypted at:

```
%AppData%\OpenTranslate\settings.dat
```

Available options:

| Field | Description |
|-------|-------------|
| Provider | OpenRouter, OpenAI, or Gemini (Google) |
| API key | Key for the selected provider |
| Model | Model ID (default: `google/gemini-3.1-flash-lite` on OpenRouter, `gpt-4o-mini` on OpenAI, `gemini-3.1-flash-lite` on Gemini). The settings window shows a speed/latency hint for the selected model. |
| Source language | Language code, e.g. `es` |
| Target language | Language code, e.g. `en` |
| Start with Windows | Register the app to run at login |

## Build and publish

```bash
dotnet build
```

### Create installer locally

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and [Inno Setup 6](https://jrsoftware.org/isinfo.php).

```powershell
.\scripts\build-installer.ps1 -Version 1.0.0
```

The installer will be at `dist/OpenTranslate-Setup-1.0.0.exe`.

### Publish only (no installer)

```bash
dotnet publish src/OpenTranslate/OpenTranslate.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable will be at `src/OpenTranslate/bin/Release/net8.0-windows/win-x64/publish/`.

### Release on GitHub

Push a version tag to trigger the release workflow:

```bash
git tag v1.0.0
git push origin v1.0.0
```

This builds the installer and attaches it to a GitHub Release automatically.

## Privacy

Copied text is sent to the configured provider (OpenRouter, OpenAI, or Gemini) for translation. It is not stored locally beyond the clipboard and encrypted settings.

## License

MIT
