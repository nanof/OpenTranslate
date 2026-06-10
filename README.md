# OpenTranslate

Windows clipboard translator powered by AI models via [OpenRouter](https://openrouter.ai/).

Copy text with **Ctrl+C** twice in quick succession (within 500 ms) and the app translates the clipboard contents, replaces them with the translation, and pastes it automatically into the active application.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- OpenRouter API key ([get one here](https://openrouter.ai/keys))

## Usage

1. Run the application. A **T** icon appears in the system tray.
2. Open **Settings…** (right-click the icon or double-click) and enter your API key.
3. By default, it translates from **Spanish** to **English** using the `google/gemini-2.0-flash-001` model.
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
| API key | OpenRouter API key |
| Model | OpenRouter model ID (configurable) |
| Source language | Language code, e.g. `es` |
| Target language | Language code, e.g. `en` |
| Start with Windows | Register the app to run at login |

## Build and publish

```bash
dotnet build
dotnet publish src/OpenTranslate/OpenTranslate.csproj -c Release -r win-x64 --self-contained false
```

The executable will be at `src/OpenTranslate/bin/Release/net8.0-windows/win-x64/publish/`.

## Privacy

Copied text is sent to the OpenRouter API for translation. It is not stored locally beyond the clipboard and encrypted settings.

## License

MIT
