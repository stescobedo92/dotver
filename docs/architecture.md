# Arquitectura

## Capas

- `DotVer.Domain`
  - modelos inmutables (`SdkInstallation`, `SdkVersion`, `DotVerConfig`)
  - reglas de comparacion y representacion.
- `DotVer.Application`
  - casos de uso y coordinacion.
  - servicios de inventario, resolucion, seleccion, intercepcion, shims e instalacion.
- `DotVer.Infrastructure`
  - sistema operativo, procesos, HTTP, persistencia JSON y registro DI.
- `DotVer.Cli`
  - `Spectre.Console` / `Spectre.Console.Cli`.
  - comandos de usuario y dashboard TUI.
- `DotVer.Tests`
  - pruebas unitarias con sandboxes de filesystem y dobles de `IConfigStore` / `IProcessRunner`.

## Flujos clave

### Resolucion de SDK activo

1. Buscar `global.json` desde el directorio actual hacia arriba.
2. Si existe `currentVersion`, usarlo primero.
3. Si no aplica, usar `defaultVersion`.
4. Si no aplica, resolver `global.json` y su `rollForward` sobre inventario local.
5. Si no aplica, caer al host oficial del sistema.

### Inventario

Fuentes:

- `~/.dotver/installs/*`
- roots oficiales conocidas por plataforma
- `DOTNET_ROOT`
- entradas de `PATH`
- roots registradas manualmente

Cada raiz se clasifica y luego se proyecta a uno o mas `SdkInstallation`.

### Intercepcion

Los shims llaman:

```text
dotver intercept -- <args-originales>
```

Reglas:

- `dotnet --version` -> devuelve la version activa resuelta.
- `dotnet --list-sdks` -> devuelve el inventario consolidado.
- `dotnet --info` -> delega al host real y anexa diagnostico de dotver.
- cualquier otro comando -> se reenvia al `dotnet` resuelto.

### Bootstrap automatico de entorno

En cada comando util, `dotver` garantiza:

- generacion idempotente de shims
- presencia de `~/.dotver/shims` en `PATH`
- persistencia de `DOTVER_HOME`
- actualizacion best-effort de perfiles de PowerShell en Windows
- actualizacion best-effort de perfiles de shell en Unix

La sesion padre de la terminal no puede ser modificada directamente desde un proceso hijo en Windows; por eso el cambio persistente se refleja por completo al abrir una terminal nueva.

## Decisiones de diseno

- El estado persistente vive en `~/.dotver/config.json`.
- Las instalaciones administradas usan el script oficial `dotnet-install` para conservar compatibilidad multiplataforma y evitar privilegios elevados.
- Los shims son archivos simples (`.cmd`, `.ps1`, `bash/fish`) para que el comportamiento sea visible y depurable.
