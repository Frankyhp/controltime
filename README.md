# ControlTime

Aplicación de escritorio para Windows que controla el uso del ordenador mediante reglas configurables: días permitidos, horarios de uso, límite diario de tiempo, seguimiento de sesiones de uso reales, avisos antes de agotar el tiempo y bloqueo de la sesión de Windows cuando el uso no está permitido.

Dos procesos cooperantes: el **App** (interfaz WPF en la sesión de usuario) y el **Service** (servicio de Windows que mide el uso, evalúa reglas y bloquea la sesión, independiente de la interfaz).

Windows only · .NET 10 · WPF · SQLite · EF Core · xUnit

---

## Arquitectura

```text
src/ControlTime.App              → WPF + MVVM + bandeja + cliente del Service (informe de actividad y estado en vivo)
src/ControlTime.Core             → Dominio y lógica de negocio (RuleEngine, uso, historial, avisos, tracking, enforcement, interfaces)
src/ControlTime.Infrastructure   → SQLite/EF Core, actividad de Windows (LastInputInfo/WTS), bloqueo por desconexión de sesión,
                                   arranjo automático, IPC por named pipes, logs por archivo
src/ControlTime.Service          → Servicio de Windows (SYSTEM): tick de 1 s actividad → sesiones de uso → evaluación → bloqueo
tests/ControlTime.Tests          → Tests unitarios (xUnit) sobre Core + integración de persistencia
docs/PLAN_CONTROLTIME.md         → Hoja de ruta por fases
docs/phases/phase-NN.md          → Registro de cada fase completada
docs/manual-testing.md           → Guion de regresión manual reutilizable
```

`ControlTime.Core` no depende de WPF, EF Core, SQLite ni APIs de Windows.

- El **Service** es el único escritor de sesiones de uso y quien aplica el bloqueo; funciona aunque el App esté cerrado.
- El **App** reporta su actividad de teclado/ratón al Service por named pipe (con sesión de Windows verificada por el servidor) y pinta el estado que devuelve el Service.
- Datos compartidos a nivel de máquina: base de datos SQLite en `%PROGRAMDATA%\ControlTime\controltime.db` (se migra automáticamente desde la antigua ruta `%APPDATA%`).
- Logs en `%PROGRAMDATA%\ControlTime\logs\` (un archivo por día y proceso, retención 14 días).

---

## Requisitos

- Compilar: Windows 10/11 x64 y [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Instalador (opcional): [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).
- Ejecutar: la publicación es autocontenida (no requiere .NET instalado).

---

## Puesta en marcha

```bash
dotnet build
dotnet test
```

En Visual Studio: abre `ControlTime.slnx`. En desarrollo pueden ejecutarse ambos procesos como consola:

```powershell
dotnet run --project src/ControlTime.Service   # deja de ser consola al registrarse como servicio
dotnet run --project src/ControlTime.App
```

Sin el Service en marcha, el App funciona en modo solo-visualización y muestra un aviso de "Service no disponible".

La primera ejecución del App pide la **contraseña de administrador** (mínimo 8 caracteres) antes de tocar Reglas o Ajustes. La base de datos se crea con migraciones EF Core al arrancar (App y Service) en `%PROGRAMDATA%\ControlTime\`.

---

## Uso

### Contraseña de administrador

- Protege: crear/editar/borrar reglas, regalar tiempo, cambiar contraseña y salir de la aplicación.
- Modo administrador temporal tras introducir la contraseña correcta; 5 intentos fallidos seguidos activan un bloqueo temporal (~30 s).
- Hash PBKDF2-HMAC-SHA256; nunca en texto plano.
- Restablecer una contraseña olvidada: detén el Service y borra la clave `admin.password.hash` de la tabla `AppSettings` (`%PROGRAMDATA%\ControlTime\controltime.db`).

### Reglas

En la pestaña **Reglas** (con modo administrador): días de la semana, franja horaria y límite diario. Regla deshabilitada = ignorada.

### Tiempo extra ("Regalar tiempo")

Un administrador puede regalar minutos para el día actual desde Reglas, con contraseña en cada regalo. No modifica el límite configurado: solo eleva el permiso de ese día.

### Panel (Dashboard)

Estado de acceso (**Permitido / Advertencia / Bloqueado**), uso de hoy (sesiones reales), límite, bonus, permiso efectivo, tiempo restante, franja activa, actividad del equipo (**Activo / Ocioso / Bloqueado**) y estado del Service.

### Historial

Pestaña **Historial**: total de la semana, lista de 14 días (uso y bonus por día) y sesiones del día seleccionado.

### Avisos

Notificaciones (globo) al quedar **15, 5 y 1 minutos**: uno por umbral, sin repetición ese día. Requiere que Windows permita notificaciones de la aplicación.

### Bloqueo de sesión (enforcement)

Con acceso **Bloqueado** y reglas activas, el Service desconecta la sesión de consola (pantalla de acceso de Windows):

- un bloqueo al entrar en el episodio Blocked y otro por cada nuevo episodio de actividad mientras siga Blocked,
- margen de ~30 s al inicio de cada episodio activo,
- el modo administrador (vía el Service) suspende el bloqueo mientras dura,
- sin reglas activas no se bloquea nunca.

**Interruptor de prueba**: variable de entorno `CONTROLTIME_ENFORCEMENT=off` en el **entorno del Service** (p. ej. a nivel de equipo y reiniciar el servicio). La lee solo el Service; se registra un aviso en el log.

### Bandeja y arranque

- Cerrar la ventana principal oculta el App a la bandeja; salir exige contraseña de administrador.
- El estado y el tiempo restante se muestran en el tooltip de la bandeja.
- "Iniciar con Windows" (pestaña Ajustes, con contraseña): preferencia por usuario en `HKCU\...\Run`; no se instala autostart global.

---

## Instalación

```powershell
pwsh build/publish.ps1                  # publica App + Service (win-x64 autocontenido) y compila el instalador
pwsh build/publish.ps1 -SkipInstaller   # solo artefactos a publish\
```

Resultado: `dist\ControlTime-Setup-<versión>.exe` (Inno Setup, por máquina, español).

- Registra `ControlTimeService` (`sc create start= delayed-auto obj= LocalSystem`) con reinicio automático ante fallos (×3).
- Actualizar = volver a ejecutar el instalador: detiene el Service, cierra el App y conserva datos, historial y contraseña.
- Desinstalar elimina el Service y los archivos; `%PROGRAMDATA%\ControlTime` **se conserva** salvo confirmación explícita.
- La versión se define en `Directory.Build.props`.
- Instalador sin firmar: expect SmartScreen warning on first run.

---

## Depuración

### Ejecutar sin que bloquee la sesión

```powershell
$env:CONTROLTIME_ENFORCEMENT = "off"   # antes de arrancar el Service en consola
dotnet run --project src/ControlTime.Service
```

### Logs

Además de la salida de depuración/EventLog, App y Service escriben logs por archivo en `%PROGRAMDATA%\ControlTime\logs\` (uno por día y proceso, retención 14 días). Útil para diagnosticar actividad, sesiones, bloqueo, IPC y disponibilidad del Service.

### Base de datos

Con el Service detenido, cualquier visor de SQLite (p. ej. DB Browser for SQLite) abre `%PROGRAMDATA%\ControlTime\controltime.db`. Tablas principales: `UsageRules`, `UsageSessions`, `TimeGrants`, `AppSettings`.

```bash
dotnet ef migrations list --project src/ControlTime.Infrastructure   # la app/service migra solo al arrancar
```

### Comportamiento difícil de reproducir

- **Ociosidad**: umbral de inactividad de 5 minutos (`WindowsActivityMonitor.DefaultIdleThreshold`, cambio local).
- **Límites y avisos**: límite diario pequeño (p. ej. 3 min) y tiempo extra para volver a subir el restante.
- **Bloqueo**: mantén `CONTROLTIME_ENFORCEMENT=off` mientras iteras sobre otras funciones.

---

## Modelo de seguridad

ControlTime protege la **configuración** frente a usuarios normales y casuales. Un administrador de Windows puede eludirlo (detener el servicio, editar la base de datos). El instalador corre el Service como SYSTEM, lo que ya exige privilegios para desactivarlo manualmente.

---

## Verificación

```bash
dotnet test
dotnet build
```

Regresión manual completa: `docs/manual-testing.md`.

---

## Documentación

- `docs/PLAN_CONTROLTIME.md`: hoja de ruta completa por fases.
- `docs/phases/phase-NN.md`: qué se implementó y decidió en cada fase.
- `docs/manual-testing.md`: guion de regresión manual reutilizable.
- `docs/product-rules.md`: reglas de producto estables (acceso, tiempo extra, evaluación de reglas).
- `AGENTS.md`: instrucciones de desarrollo y arquitectura.
