<div align="center">

# QuickPanel

**La barra lateral de Microsoft Edge, de vuelta — y mejor.**

Panel flotante que se ancla a cada ventana de Edge y abre tus apps web favoritas
(WhatsApp, Gmail, Claude, Notion…) en ventanas nativas, sin perder lo que estás haciendo.

[![License](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)

</div>

---

## ¿Qué es?

Microsoft eliminó la barra lateral de apps de Edge. QuickPanel la recrea como app nativa de Windows:
un botón flotante anclado a cada ventana de Edge que despliega tus apps web favoritas en paneles
nativos con sesión persistente.

A diferencia de las extensiones (que usan iframes y fallan con Gmail, WhatsApp, etc.), QuickPanel usa
**WebView2** — el mismo motor del navegador — así que **cualquier sitio carga sin bloqueos**.

## Características

- **Se ancla a Edge** — un botón por ventana de Edge; si abrís dos ventanas, cada una tiene su panel.
- **Apps nativas** — Gmail, WhatsApp y cualquier web cargan sin los bloqueos de iframe.
- **Sesión persistente** — cada app mantiene su login entre aperturas.
- **No interrumpe** — cerrar un panel lo oculta sin perder estado; click afuera lo minimiza.
- **Material Design 3** — interfaz limpia con color personalizable.
- **Despliegue inteligente** — las apps se despliegan según el espacio disponible en la ventana.

## Instalación

1. Descargá el último `QuickPanel.zip` desde [Releases](../../releases).
2. Descomprimí en una carpeta (ej. `C:\Tools\QuickPanel`).
3. Ejecutá `QuickPanel.exe`.
4. (Opcional) Activá "Iniciar con Windows" desde la configuración.

> **Requisito**: [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
> (preinstalado en Windows 11; en Windows 10 se instala automáticamente con Edge actualizado).

## Uso

- Abrí una ventana de Edge → aparece el botón flotante.
- Click en el botón → se despliega el menú con `+` (agregar app) y `⚙` (configuración).
- Agregá apps desde presets o pegando una URL.
- Click en una app → se abre el panel anclado, redimensionable desde el borde libre.
- Click derecho en una app del menú → quitar.

## Compilar desde el código

```powershell
git clone https://github.com/MergeCraft/QuickPanel.git
cd QuickPanel
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

El resultado queda en `bin\Release\net8.0-windows\win-x64\publish\`.

## Stack técnico

WPF (.NET 8) · WebView2 · Win32 interop (`SetWinEventHook`, `GWLP_HWNDPARENT`) · Material Design 3

El seguimiento de la ventana de Edge usa hooks de eventos de Windows; cada panel se ancla
cross-process al rect de su ventana de Edge. Detalles en el código (`Core/`).

## Licencia

[PolyForm Noncommercial 1.0.0](LICENSE) — el código es visible y podés compilarlo para uso
personal, pero el uso comercial y la reventa están reservados al autor.

---

<div align="center">
Hecho por <a href="https://lucasgiusiano.uy">Lucas Giusiano</a>
</div>
