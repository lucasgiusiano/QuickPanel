# QuickPanel

Panel flotante nativo que recrea la barra lateral de Edge. Se ancla a cada ventana de Edge, abre apps web en ventanas WebView2 nativas (sin chrome de Edge, sesión persistente) y usa estética Material Design 3.

## Requisitos

- Windows 10/11
- .NET 8 SDK (`dotnet --version` ≥ 8.0)
- Runtime de WebView2 (preinstalado en Win11)

## Build y ejecución

```powershell
cd QuickPanel
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

El ejecutable queda en `bin\Release\net8.0-windows\QuickPanel.exe`.
Output esperado: aparece un ícono en la bandeja del sistema. Al abrir una ventana de Edge, aparece el botón circular flotante sobre ella.

## Publicar como exe único (opcional)

```powershell
dotnet publish -c Release -r win-x64 --self-contained false /p:PublishSingleFile=true
```

Genera `QuickPanel.exe` en `bin\Release\net8.0-windows\win-x64\publish\`.

## Uso

- **Click en el botón** → despliega `+` (agregar app), `⚙` (settings) y la pila de apps.
- **Despliegue direccional**: si el botón está en la mitad inferior de la pantalla, las apps suben; si está en la superior, bajan. Al llegar al borde, fluyen a una nueva columna a la izquierda.
- **Agregar app**: presets (WhatsApp, Gmail, Claude, etc.) o URL custom.
- **Click derecho en una app** (en el menú) → quitar.
- **Settings**:
  - *Mover botón*: activa el modo arrastre; soltás y queda guardado relativo a Edge.
  - *Color*: cambia el seed MD3 de toda la interfaz.
  - *Tamaño del panel*: S / M / L.
  - *Iniciar con Windows*: toggle (registro Run).

## Comportamiento

- Una ventana de Edge = un botón + su propio set de apps abiertas.
- Cerrás la ventana de Edge → su overlay se destruye.
- Cerrás una app con la ✕ de su barra → se oculta (no se destruye), mantiene sesión. Se destruye al quitarla o al salir.
- Salir definitivamente: ícono de bandeja → Salir.

## Notas técnicas

- El seguimiento de Edge usa `SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE)` global + scan periódico para altas/bajas de ventanas.
- El anclado z-order es `GWLP_HWNDPARENT` cross-process: el botón sigue a Edge y se oculta al minimizarlo.
- Cada app tiene su `UserDataFolder` propio en `%AppData%\QuickPanel\Profiles\<id>` → sesiones independientes y persistentes.
- DPI: manifest `PerMonitorV2`; el posicionamiento del botón escala por monitor.

## Limitaciones conocidas

- Solo Edge (filtra por `msedge.exe` + clase `Chrome_WidgetWin_1`). Para Chrome, agregar su process name en `EdgeWindowMonitor.IsEdgeTopLevel`.
- Si Edge cambia su estructura de ventanas en un update, el filtro de detección puede requerir ajuste.
- Con snap/maximizado multi-monitor a distinto DPI, puede haber un frame de desfase hasta el siguiente `LOCATIONCHANGE`.
