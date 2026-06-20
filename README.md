<div align="center">

# QuickPanel

**La barra lateral de Microsoft Edge, de vuelta — y mejor.**

Panel flotante que se ancla a tu navegador y abre tus apps web favoritas
(WhatsApp, Gmail, Claude, Notion…) en ventanas nativas, sin perder lo que estás haciendo.

[![License](https://img.shields.io/badge/license-PolyForm%20Noncommercial-blue)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows)](https://www.microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)

</div>

---

## ¿Qué es?

Microsoft eliminó la barra lateral de apps de Edge. QuickPanel la recrea como app nativa de Windows:
un botón flotante anclado a tu navegador que despliega tus apps web favoritas en paneles
nativos con sesión persistente.

A diferencia de las extensiones (que usan iframes y fallan con Gmail, WhatsApp, etc.), QuickPanel usa
**WebView2** — el mismo motor del navegador — así que **cualquier sitio carga sin bloqueos**.

## Características

**Núcleo**
- **Se ancla a tu navegador** — detecta tu navegador predeterminado de Windows (Edge, Chrome, Brave,
  Opera o Vivaldi) y se ancla a él; un botón por ventana abierta.
- **Apps nativas vía WebView2** — Gmail, WhatsApp y cualquier web cargan sin los bloqueos de iframe.
- **Sesión y zoom persistentes** — cada app mantiene su login y su nivel de zoom entre aperturas.
- **No interrumpe** — minimizar un panel lo oculta sin perder estado; cerrar libera la memoria;
  click afuera de la configuración la minimiza sola.
- **Links externos al navegador** — un link que abre en otro dominio (ej. un adjunto en un mail) se
  abre en tu navegador, no dentro del panel. Los flujos de login federado quedan exceptuados.
- **Despliegue inteligente** — el menú y los paneles se posicionan según el lado del botón y el
  espacio disponible, sin salirse de la ventana.

**Personalización**
- Temas oscuro, claro o según el sistema, con paletas de color.
- Reordenar apps por drag & drop, íconos y nombres personalizados, color por app.
- Tamaños de panel y de menú (S/M/L).
- Grupos/carpetas para organizar apps en el menú.

**Productividad**
- Atajos de teclado globales: `Ctrl+Alt+1‑0` se autoasignan a las primeras 10 apps; el resto y las
  acciones (abrir menú, ocultar panel, app siguiente/anterior, auto‑ocultar, mover botón) son
  configurables.
- Historial de navegación y búsqueda rápida por app.
- Auto‑ocultar el botón flotante y abrir una app automáticamente al iniciar.
- Contador de notificaciones no leídas por app y total.

**Rendimiento**
- Entorno de WebView2 compartido entre todos los paneles (reduce procesos duplicados de Chromium).
- **Modo Lite**, pensado para equipos con poca RAM: suspende los paneles ocultos a los 20s, baja
  su uso de memoria, y mantiene como máximo 3 paneles activos en simultáneo (los demás se cierran
  solos, sin perder configuración). Cualquier panel puede marcarse como **"Mantener activo"** para
  quedar exento — útil para música o llamadas en curso.

## Planes

| | Free | Pro (USD 4.99) | Complete (USD 9.99) |
|---|---|---|---|
| Apps | Hasta 3 | Ilimitadas | Ilimitadas |
| Reordenar, hotkeys, notificaciones, búsqueda | | ✅ | ✅ |
| Temas claro/sistema, paletas premium | | ✅ | ✅ |
| Historial, inicio en app, auto-ocultar | | ✅ | ✅ |
| Grupos/carpetas, múltiples cuentas/perfiles, color por app | | | ✅ |
| Picture-in-picture, importar/exportar config | | | ✅ |

## Instalación

1. Descargá el último `QuickPanel.zip` desde [Releases](../../releases), o el instalador
   `QuickPanelSetup.exe` si está publicado.
2. Si usás el ZIP: descomprimí en una carpeta (ej. `C:\Tools\QuickPanel`) y ejecutá `QuickPanel.exe`.
3. Si usás el instalador: seguilo, te va a preguntar por acceso directo e inicio con Windows.
4. (Opcional) Activá "Iniciar con Windows" desde Configuración si no lo hiciste al instalar.

> **Requisito**: [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
> (preinstalado en Windows 11; en Windows 10 se instala automáticamente con Edge actualizado).
>
> **Navegador**: QuickPanel se ancla a tu navegador predeterminado de Windows. Funciona con
> cualquiera basado en Chromium (Edge, Chrome, Brave, Opera, Vivaldi). Si tu predeterminado es
> otro (ej. Firefox), la app te avisa al iniciar y no podrá anclarse.

## Uso

- Abrí tu navegador → aparece el botón flotante.
- Click en el botón → se despliega el menú con `+` (agregar app) y `⚙` (configuración).
- Agregá apps desde presets o pegando una URL.
- Click en una app → se abre el panel anclado, redimensionable desde el borde libre.
- Click derecho en una app del menú → quitar.
- Mantené presionado el handle (`⠿`) de una fila en Administrar apps para reordenar arrastrando.

## Compilar desde el código

```powershell
git clone https://github.com/lucasgiusiano/QuickPanel.git
cd QuickPanel
dotnet publish QuickPanel.csproj -c Release -r win-x64 --self-contained true -o publish
```

El resultado queda en `publish\QuickPanel.exe`.

Para generar un instalador, instalá [Inno Setup](https://jrsoftware.org/isinfo.php) y compilá
`installer.iss` con `ISCC.exe installer.iss` — el resultado queda en `installer-output\`.

## Stack técnico

WPF (.NET 8) · WebView2 (entorno compartido entre paneles) · Win32 interop (`SetWinEventHook`,
`GWLP_HWNDPARENT`, `RegisterHotKey`) · Material Design 3

El seguimiento del navegador usa hooks de eventos de Windows; cada panel se ancla cross-process al
rect de su ventana. El navegador objetivo se detecta una vez al iniciar la app, leyendo el
predeterminado configurado en Windows. Detalles en el código (`Core/`, `Services/`).

## Licencia

[PolyForm Noncommercial 1.0.0](LICENSE) — el código es visible y podés compilarlo para uso
personal, pero el uso comercial y la reventa están reservados al autor.

---

<div align="center">
Hecho por <a href="https://lucasgiusiano.uy">Lucas Giusiano</a>
</div>
