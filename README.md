# dotver

`dotver` es un gestor de versiones de SDK de .NET inspirado en la ergonomia de `nvm` y `sdkman`, pero centrado en el ecosistema `dotnet`.

## Objetivos

- TUI/CLI interactiva con `Spectre.Console`.
- Multiplaforma: Windows, Linux y macOS.
- Deteccion de SDKs instalados por raiz oficial, `DOTNET_ROOT`, `PATH` y roots registradas manualmente.
- Instalaciones aisladas en `~/.dotver/installs/<version>` usando el script oficial `dotnet-install`.
- Shims para interceptar `dotnet --info`, `dotnet --list-sdks` y `dotnet --version`.
- Tests unitarios y pipelines para build/release.
- Empaquetado preparado para npm y generacion de manifests para WinGet.

## Comandos principales

```bash
dotver
dotver list
dotver current
dotver use 8.0.412
dotver default 10.0.201
dotver alias lts 8.0.412
dotver install 10.0.201
dotver install 8.0
dotver install lts
dotver list-remote
dotver uninstall 8.0.412
dotver doctor
dotver register-root /custom/dotnet
```

## Modelo operacional

`dotver` diferencia entre dos tipos de inventario:

- `managed`: SDKs aislados bajo `~/.dotver/installs/<version>`, directamente seleccionables.
- `external`: SDKs encontrados en roots oficiales o personalizadas del sistema. Estos se detectan y auditan, pero solo se pueden usar directamente cuando su host root contiene un unico SDK.

Esto es intencional: una raiz oficial como `C:\Program Files\dotnet` puede contener multiples SDKs, y el host `dotnet` decide cual usar segun `global.json` y las reglas de roll-forward de la CLI.

## Configuracion automatica

`dotver` ahora configura automaticamente:

- shims en `~/.dotver/shims`
- `PATH` persistente del usuario
- perfiles de PowerShell en Windows y perfiles soportados en Linux/macOS
- `current` y `default` al instalar un SDK

En Windows, el proceso actual no puede mutar el shell padre ya abierto; si el PATH persistente cambia, basta con abrir una nueva terminal.

## Documentacion usada

- `Spectre.Console` y `Spectre.Console.Cli` via Context7 para estructura de `CommandApp`, DI, prompts y testing.
- Microsoft Learn para:
  - `global.json` y seleccion de SDKs.
  - `dotnet-install` script.
  - deteccion de instalaciones de SDK.
  - empaquetado/publicacion en WinGet.

## CI/CD

- `ci.yml`: restore, build y test en Windows, Linux y macOS, incluyendo cobertura del bootstrap y los shims por plataforma.
- `release.yml`: publica binarios por RID, release de GitHub, paquete npm y manifests de WinGet como artifact, incluyendo `osx-arm64` y `osx-x64`.

## Limitaciones conscientes

- `dotver` puede detectar SDKs externos multi-version, pero no puede forzar de forma fiable una version exacta dentro de una raiz oficial multi-SDK sin apoyarse en `global.json`. Ese comportamiento respeta la semantica real de la CLI de .NET.
- La publicacion en WinGet requiere revision del ecosistema WinGet. El pipeline deja los manifests listos y puede extenderse para submission automatica con los secretos adecuados.
