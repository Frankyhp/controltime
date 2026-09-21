# ControlTime

Aplicación de escritorio para Windows que controla el uso del ordenador mediante reglas configurables: días permitidos, horarios de uso, límite diario de tiempo, seguimiento de sesiones de uso, avisos antes de agotar el tiempo y bloqueo de la sesión de Windows cuando el uso no está permitido.

Windows only · .NET 10 · WPF · SQLite · EF Core · xUnit

---

## Estructura

```text
src/ControlTime.App              → WPF + composición de dependencias (vista, ViewModels, shell)
src/ControlTime.Core             → Dominio y lógica de negocio (RuleEngine, servicios, interfaces)
src/ControlTime.Infrastructure   → SQLite/EF Core, sondas de actividad Windows, LockWorkStation
tests/ControlTime.Tests          → Tests unitarios (xUnit) sobre Core + integración de persistencia
docs/PLAN_CONTROLTIME.md         → Hoja de ruta por fases
docs/phases/phase-NN.md          → Registro de cada fase completada
```

`ControlTime.Core` no depende de WPF, EF Core, SQLite ni APIs de Windows.

---

## Requisitos

- Windows 10 o 11.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (incluye el runtime de escritorio para WPF).
- Opcional: Visual Studio 2022+ o VS Code con la extensión de C#.
- Opcional para migraciones: `dotnet tool install --global dotnet-ef`.

---

## Puesta en marcha

```bash
# Restaurar, compilar y ejecutar los tests
dotnet build
dotnet test

# Ejecutar la aplicación
dotnet run --project src/ControlTime.App
```

En Visual Studio: abre `ControlTime.slnx`, define `ControlTime.App` como proyecto de inicio y pulsa F5.

La primera ejecución:

- Crea la base de datos SQLite automáticamente (aplica migraciones EF Core al arrancar) en:
  `%APPDATA%\ControlTime\controltime.db`
- Muestra la pantalla de Reglas para configurar la contraseña de administrador (mínimo 8 caracteres). Solo se configura una vez.

---

## Uso

### Contraseña de administrador

- Sin contraseña configurada, la primera pantalla pedida es la de configuración.
- Las funciones sensibles (crear/editar/borrar reglas, cambiar límites, otorgar tiempo extra) requieren **modo administrador**: se desbloquean introduciendo la contraseña y duran 5 minutos.
- La contraseña se guarda como hash PBKDF2-HMAC-SHA256; nunca en texto plano.
- Para restablecer una contraseña olvidada: cierra la aplicación y borra la clave `admin.password.hash` de la tabla `AppSettings` (o elimina `controltime.db`, perdiendo todos los datos).

### Reglas

En la pestaña **Reglas** (con modo administrador activo) puedes crear reglas con:

- días de la semana permitidos,
- franja horaria (inicio/fin),
- límite máximo de uso diario.

Siempre que exista al menos una regla activa, la más permisiva determina el acceso.

### Tiempo extra ("Regalar tiempo")

Un administrador puede regalar minutos para el día actual desde la propia pantalla de Reglas, introduciendo la contraseña en cada regalo. El regalo no modifica el límite configurado: se suma como permiso adicional solo para ese día.

### Panel (Dashboard)

Muestra en tiempo real:

- estado de acceso: **Permitido / Advertencia / Bloqueado**,
- uso de hoy (sesiones reales de actividad), límite configurado, bonus de hoy, permiso efectivo y tiempo restante,
- franja activa y próximo acceso permitido cuando está bloqueado,
- actividad del equipo: **Activo / Ocioso / Bloqueado**.

### Avisos

Cuando queda poco tiempo quedan **15, 5 y 1 minutos**, ControlTime muestra una notificación de Windows (un globo por umbral, sin repetirlo ese día). Requiere que Windows permita notificaciones de la aplicación.

### Bloqueo de sesión (enforcement)

Cuando el acceso está **Bloqueado** y hay reglas activas, ControlTime bloquea la sesión de Windows (`LockWorkStation`):

- un bloqueo al entrar en el episodio Blocked y otro cada vez que recuperas la actividad mientras siga Blocked,
- el modo administrador suspende el bloqueo durante sus 5 minutos,
- sin reglas activas no se bloquea nunca.

**Interruptor de prueba**: define la variable de entorno `CONTROLTIME_ENFORCEMENT=off` antes de arrancar para desactivar el bloqueo (útil al validar la aplicación). Se registra un aviso en el log de arranque.

---

## Depuración

### Ejecutar con F5 sin que bloquee la sesión

El repositorio incluye dos configuraciones de depuración de VS Code (`.vscode/launch.json`):

- **ControlTime (debug: sin bloqueo de sesión)** — F5 por defecto; arranca con `CONTROLTIME_ENFORCEMENT=off`, por lo que el enforcement está desactivado aunque exista una regla activa en estado Bloqueado.
- **ControlTime (debug: con bloqueo de sesión)** — idéntica pero con el bloqueo activo, para validar el enforcement real.

Desde terminal, el equivalente es:

```powershell
$env:CONTROLTIME_ENFORCEMENT = "off"
dotnet run --project src/ControlTime.App
```

Con el enforcement desactivado, la app registra un aviso en el log de arranque ("El bloqueo de sesión está desactivado…"). La variable solo la lee la composición de la app; no afecta a los tests.

### Registro (logs)

La aplicación usa `Microsoft.Extensions.Logging` con `AddDebug()`: los mensajes (fallos de base de datos, de actividad, de notificación y de bloqueo) se envían a la **salida de depuración**.

- Con Visual Studio: ventana **Salida → Fuente de mensajes: Depuración de .NET**.
- Sin depurador adjunto: [DebugView](https://learn.microsoft.com/sysinternals/downloads/debugview) de Sysinternals (activa *Capture Win32*/Global).

### Base de datos

Para inspeccionar datos en ejecución:

```bash
dotnet ef migrations list --project src/ControlTime.Infrastructure
dotnet ef database update  --project src/ControlTime.Infrastructure   # no necesario: la app migra al arrancar
```

Cualquier visor de SQLite (por ejemplo, DB Browser for SQLite) puede abrir `%APPDATA%\ControlTime\controltime.db` mientras la app está cerrada. Tablas principales: `UsageRules`, `UsageSessions`, `TimeGrants`, `AppSettings`.

### Comportamiento difícil de reproducir

- **Ociosidad**: el umbral de inactividad es de 5 minutos (fijo por ahora); deja el equipo sin teclado/ratón o cambia `WindowsActivityMonitor.DefaultIdleThreshold` en local.
- **Límites y umbrales de aviso**: configura un límite diario pequeño (p. ej. 3 min) y observa las transiciones 15/5/1 min usando tiempo extra para volver a subir el restante.
- **Bloqueo**: usa `CONTROLTIME_ENFORCEMENT=off` mientras iteras sobre otras funciones.

---

## Despliegue

No hay instalador todavía (el plan de fases no lo incluye). Opciones:

```bash
# Ejecutable autocontenido por arquitectura (no requiere .NET instalado)
dotnet publish src/ControlTime.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

# O framework-dependent (más ligero, requiere .NET 10 Desktop Runtime)
dotnet publish src/ControlTime.App -c Release -r win-x64 --self-contained false
```

El resultado queda en `src/ControlTime.App/bin/Release/net10.0-windows/win-x64/publish/`.

Notas de despliegue:

- La base de datos se crea por y para el usuario en `%APPDATA%\ControlTime\` (primera ejecución).
- Las notificaciones y el bloqueo operan dentro de la sesión de usuario iniciada; la aplicación es un proceso de escritorio normal.
- Modelo de seguridad: ControlTime protege la **configuración** frente a usuarios normales y casuales; un administrador de Windows puede eludirlo (matar el proceso, editar la base de datos). El endurecimiento (bandeja persistente, servicio de Windows) corresponde a fases posteriores.

---

## Verificación

```bash
dotnet test
dotnet build
```

Los tests de `ControlTime.Tests` no requieren WPF, Windows UI ni base de datos real, salvo los de integración de persistencia (SQLite in-process).

---

## Documentación

- `docs/PLAN_CONTROLTIME.md`: hoja de ruta completa por fases.
- `docs/phases/phase-NN.md`: qué se implementó y decidió en cada fase.
- `docs/product-rules.md`: reglas de producto estables (acceso, tiempo extra, evaluación de reglas).
- `AGENTS.md`: instrucciones de desarrollo y arquitectura.
