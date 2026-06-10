# OpenTranslate

Traductor de portapapeles para Windows que usa modelos de IA vía [OpenRouter](https://openrouter.ai/).

Copia un texto con **Ctrl+C** dos veces seguidas (en menos de 500 ms) y la app traduce el contenido del portapapeles, lo sustituye por la traducción y lo pega automáticamente en la aplicación activa.

## Requisitos

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- API key de OpenRouter ([obtener aquí](https://openrouter.ai/keys))

## Uso

1. Ejecuta la aplicación. Aparecerá un icono **T** en la bandeja del sistema.
2. Abre **Configuración…** (clic derecho en el icono o doble clic) e introduce tu API key.
3. Por defecto traduce de **español** a **inglés** con el modelo `google/gemini-2.0-flash-001`.
4. Selecciona texto en cualquier app, copia con **Ctrl+C** dos veces rápido y la traducción se pegará automáticamente.

También puedes usar **Traducir portapapeles ahora** desde el menú contextual para traducir manualmente lo que haya en el portapapeles.

## Configuración

Los ajustes se guardan cifrados en:

```
%AppData%\OpenTranslate\settings.dat
```

Opciones disponibles:

| Campo | Descripción |
|-------|-------------|
| API key | Clave de OpenRouter |
| Modelo | ID del modelo en OpenRouter (configurable) |
| Idioma origen | Código de idioma, p. ej. `es` |
| Idioma destino | Código de idioma, p. ej. `en` |
| Iniciar con Windows | Registra la app en el inicio de sesión |

## Compilar y publicar

```bash
dotnet build
dotnet publish src/OpenTranslate/OpenTranslate.csproj -c Release -r win-x64 --self-contained false
```

El ejecutable quedará en `src/OpenTranslate/bin/Release/net8.0-windows/win-x64/publish/`.

## Privacidad

El texto copiado se envía a la API de OpenRouter para traducirlo. No se almacena localmente más allá del portapapeles y la configuración cifrada.

## Licencia

MIT
