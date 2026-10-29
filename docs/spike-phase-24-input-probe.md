# Spike — Fase 24: sonda de entrada de usuario desde el Servicio

Fecha: 2026-10-29. Entorno: Windows 11 (26100, escritorio), usuario estándar en sesión 1,
procedimiento lanzado como `LocalSystem` (servicio temporal), sin Secure App Cache
(estado uncertain; la política AppLocker está en modo `NotConfigured`).

## Técnica elegida (validada de extremo a extremo)

El servicio (SYSTEM, sesión 0) lanza un proceso ayudante efímero en la sesión consola y
le lee el resultado por memoria compartida:

1. `WTSGetActiveConsoleSessionId` → id de sesión consola.
2. `WTSQueryUserToken` + `DuplicateTokenEx(TokenPrimary)`.
3. `LoadUserProfile` **necesario** (sin `lpUserName` → err 87; sin perfil cargado el
   proceso su padre falla — ver lista de fracasos).
4. `CreateEnvironmentBlock` (bloque de entorno del usuario).
5. SHM: `CreateFileMappingW` página `Global\ControlTime.*` con DACL propia
   (escritura solo para el SID del usuario o bien `AU` con token de autenticación
   dentro del contenido). **No** `Global\` con DACL por defecto desde usuario normal
   (falta `SeCreateGlobalPrivilege`; SYSTEM sí puede).
6. `CreateProcessAsUserW` con:
   - `STARTUPINFO.lpDesktop = NULL` ← **clave**: pasar `"Winsta0\Default"` provoca
     `0xC0000142 STATUS_DLL_INIT_FAILED` en procesos con imports GUI (todo exe .NET,
     whoami, powershell) lanzados desde session 0. `cmd.exe` es la única excepción
     observada (tolera el fallo de adjunto al escritorio). Sin `lpDesktop` el hijo
     hereda la window station del servicio y AÚN ASÍ `GetLastInputInfo` devuelve la
     entrada REAL de la sesión del token (comprobado: "hace 1s" con usuario activo).
   - `CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT`, `lpCurrentDirectory` válida,
     `bInheritHandles=FALSE` (salvo E/S diagnóstica con `SECURITY_ATTRIBUTES`
     heredable).
7. El hijo escribe en la SHM el `FILETIME` UTC de la última entrada
   (`now - (GetTickCount64 - lii.dwTime)`) y termina; el servicio espera con timeout y
   lee. Cada fallo de sonda = valores ausentes → comportamiento optimista actual
   (decisión de diseño) con log limitado.

## Fracasos medidos (para no repetirlos)

- `OpenFileMapping` de `WinSta0_DesktopInUseTimeout`: la sección ya no existe en
  Windows 11 (err 2 vía `Local\`, err 161 vía ruta nativa `\Sessions\N\...`).
- `winsta!WinStationQueryInformation` clase 8: devuelve `WINSTATIONINFORMATION`
  correcto (conect/logon FILETIME, nombre `Console`) pero `LastInputTime = 0` siempre
  en Windows cliente (solo se rellena en Windows Server/RDS). Escaneo de todo el
  buffer: ningún valor plausible de última entrada.
- `lpDesktop = "Winsta0\Default"` en `CreateProcessAsUser` desde session 0:
  `0xC0000142` para cualquier PE con init GUI (.NET apphost, framework-dependent,
  self-contained y single-file; también whoami/powershell). Sin `lpDesktop`: OK.
- exe .NET fallaba también por resolver el runtime: en esta máquina el SDK está en
  `%LOCALAPPDATA%\Microsoft\dotnet` (solo HKCU); la implementación real usará el
  Service autocontenido ya instalado en `%ProgramFiles%\ControlTime` o un exe nativo
  diminuto, sin apphost huérfano.

## Métricas del ayudante validado

- Exe framework-dependent `net10.0` (apphost 162 KB) → arranque + salida en < 1 s.
- `LoadUserProfile` + `CreateEnvironmentBlock` + lanzamiento + espera: < 2 s.
- El valor leído coincide con `GetLastInputInfo` local (margen < 1 s).

## Consecuencias para la implementación (fase 24)

- El ayudante **no necesita `Winsta0\Default`**; basta `lpDesktop = NULL` (más
  simple y sin tocar DACLs de window station/desktop).
- En la implementación el ayudante puede ser un modo del propio
  `ControlTime.Service.exe --probe-input <mapa>` (autocontenido, ya instalado) en
  lugar de un proyecto extra; se evaluará en el plan.
- Frecuencia de sondeo propuesta: 5-10 s, solo cuando no hay informe fresco del App
  (el informe IPC del App sigue teniendo prioridad, Fase 18 intacta).
- El cierre de sesión de uso se hace en el `LastInputAt` medido (precisión =
  frecuencia de sondeo, no umbral de idle).
