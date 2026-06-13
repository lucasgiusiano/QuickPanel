# Guía de publicación

## GitHub Releases (recomendado para empezar)

El workflow `.github/workflows/release.yml` genera el ZIP automáticamente.

```powershell
# 1. Commit y push del código
git add .
git commit -m "feat: release v1.0.0"
git push

# 2. Crear y pushear el tag → dispara el build y crea el Release con el ZIP
git tag v1.0.0
git push origin v1.0.0
```

GitHub compila, quita los `.pdb` y publica `QuickPanel-v1.0.0.zip` en Releases.

---

## Microsoft Store

### Requisitos previos
- Cuenta de desarrollador en [Partner Center](https://partner.microsoft.com/dashboard) — **USD 19** pago único.
- La app empaquetada como **MSIX** con capability `runFullTrust` (necesaria por el uso de Win32 / hooks).

### Paso 1 — Reservar el nombre
Partner Center → Apps and games → New product → reservar "QuickPanel".

### Paso 2 — Empaquetar como MSIX
Con Visual Studio Community (gratis):
1. Agregar al solution un proyecto **Windows Application Packaging Project**.
2. Referenciar el proyecto QuickPanel.
3. En `Package.appxmanifest` declarar:
   ```xml
   <Capabilities>
     <rescap:Capability Name="runFullTrust" />
   </Capabilities>
   ```
   (con el namespace `xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"`)
4. Build → genera `.msixbundle`.

### Paso 3 — Assets visuales (obligatorios)
- Logo 44x44, 150x150, 310x150, 310x310, 71x71
- Al menos 1 captura de pantalla (1366x768 o superior)
- Ícono de Store 300x300

### Paso 4 — Política de privacidad (obligatoria)
La app guarda perfiles de navegación locales. Necesitás una URL pública que lo declare.
Podés hostearla en tu portfolio: `https://lucasgiusiano.uy/quickpanel/privacy`.
Mínimo a declarar: qué datos se guardan (sesiones de las apps, localmente), que no se transmiten a terceros.

### Paso 5 — Subir y certificar
Partner Center → subir el `.msixbundle` → completar listing → enviar.
Con `runFullTrust` la revisión es **manual**: 3-10 días hábiles.

---

## Monetización (definido, pendiente de implementar)

Modelo freemium con tres planes (pago único cada uno):

| Plan | Contenido | Precio |
|------|-----------|--------|
| Free | Hasta 5 apps, con publicidad | $0 |
| Clean | Sin publicidad, 5 apps | $3.99 |
| Pro | Apps ilimitadas, con publicidad | $7.99 |
| Complete | Sin publicidad + ilimitadas | $11.99 |

Implementación pendiente:
- Límite de 5 apps en Free (validar en `AddAppDialog` / `OverlayManager`).
- Banner de publicidad en el panel (quitar al comprar).
- Verificación de licencia vía `Windows.Services.Store` (IAP de la Store) o licencia propia.

---

## Pendientes de robustez (antes de escala)

- **Detección de Edge frágil**: si Edge cambia su estructura de ventanas, `EdgeWindowMonitor.IsEdgeTopLevel` puede requerir ajuste.
- **Telemetría de errores**: sin reporting, los fallos en PCs de usuarios son invisibles. Considerar Sentry o AppCenter.
- **Firma de código**: para distribución directa sin warnings de SmartScreen, conviene un certificado de firma (~USD 200/año) o build reputation.
