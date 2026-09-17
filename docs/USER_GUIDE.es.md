# MedReminder — Guía rápida

Guía operativa para el usuario final. El archivo
[`ANALYSIS.md`](ANALYSIS.md) describe en cambio la arquitectura
técnica.

> **MedReminder es un recordatorio organizativo, no un dispositivo
> médico.** No proporciona diagnósticos, indicaciones terapéuticas,
> modificaciones del tratamiento ni sugerencias clínicas. Toda
> decisión sobre la terapia debe tomarse con tu médico.

---

## Primer inicio

1. Ejecuta `MedReminder.exe`.
2. En la primera apertura la ventana está vacía: la base de datos se
   crea automáticamente en
   `%LOCALAPPDATA%\MedReminder\medreminder.db`.
3. Arriba se encuentra la barra de herramientas; abajo la barra de
   estado. El icono en el área de notificación de Windows permanece
   visible mientras la aplicación esté en ejecución.

### SmartScreen de Windows en el primer inicio

Los binarios publicados no están firmados digitalmente. En el primer
inicio de `MedReminder.exe`, Windows muestra un diálogo azul «Windows
protegió tu PC». Para continuar:

1. Haz clic en **Más información**.
2. Haz clic en **Ejecutar de todas formas**.

Windows recuerda la elección para ese archivo concreto: los inicios
siguientes no volverán a preguntar. Si instalas mediante el MSI, el
diálogo UAC indica «Editor desconocido» por el mismo motivo y es
normal.

## Añadir un medicamento

1. Barra de herramientas → **Nuevo medicamento**.
2. Rellena los campos obligatorios (marcados con `*`): Nombre,
   Unidad, Dosis por toma, Tomas al día, Fecha de inicio, Umbral de
   aviso (días restantes).
3. Campos opcionales: Principio activo, Envase, Fecha de fin de
   terapia, Médico de referencia, Notas.
4. **Stock inicial**: indica los comprimidos/ml/dosis que ya tienes
   en el momento del registro. Se crea un movimiento `InitialLoad`.
5. **Canales de notificación**: marca Windows y/o E-mail. Debes
   haber configurado los ajustes SMTP (ver más abajo) para que el
   e-mail funcione.
6. **Guardar**.

## Añadir stock (nueva caja)

1. Selecciona el medicamento en la cuadrícula.
2. Barra de herramientas → **Añadir stock**.
3. Elige el tipo de movimiento:
   - **Nueva caja**: el caso habitual tras una compra.
   - **Añadido manual**: por ejemplo si recibes muestras del médico.
   - **Corrección positiva**: habías contado menos que la cantidad
     real.
4. Introduce la cantidad (en la unidad del medicamento) y confirma.

**Efecto**: el stock aumenta y el `StockEpoch` del medicamento
avanza en 1. Esto reinicia el ciclo de aviso — la próxima
notificación podrá emitirse cuando el stock vuelva a bajar del
umbral.

## Corregir una cantidad en defecto

Si observas que el stock real es menor que el calculado (comprimido
perdido, derramado, etc.):

1. Selecciona el medicamento.
2. Barra de herramientas → **Corregir stock**.
3. El tipo por defecto es **Corrección negativa**: la cantidad
   introducida se resta del stock. No hace avanzar el epoch: no
   reprograma el ciclo de notificaciones.

Si la corrección dejara el stock por debajo de cero, la operación se
bloquea con un error.

## Editar o desactivar un medicamento

- **Editar**: doble clic en la fila o barra de herramientas →
  **Editar**. Puedes cambiar el nombre, principio activo, envase,
  unidad, umbral, médico, notas, fecha de fin, canales de
  notificación, y el estado Activo/Inactivo.
  **La dosis y la frecuencia NO se modifican desde aquí**: usa el
  cambio de posología (función en línea de comandos o edición
  directa de la DB en el MVP).
- **Desactivar**: barra de herramientas → **Desactivar**. El
  medicamento desaparece de las comprobaciones automáticas y de los
  avisos, pero los datos históricos (movimientos, notificaciones)
  permanecen en la DB para auditoría.

## Configurar el envío de e-mails

**Configuración → E-mail SMTP**:

- **Servidor**: por ejemplo `smtp.gmail.com`,
  `smtp-mail.outlook.com`, etc.
- **Puerto**: normalmente 587 (StartTLS) o 465 (SSL/TLS directo).
  MedReminder usa StartTLS cuando la casilla correspondiente está
  marcada.
- **Nombre de usuario / Nueva contraseña**: si el servidor requiere
  autenticación. La contraseña se cifra con DPAPI y se guarda en
  `%LOCALAPPDATA%\MedReminder\smtp.protected`. No aparece en
  `smtp.settings.json` ni en los logs.
- **Eliminar contraseña guardada**: borra `smtp.protected` al
  siguiente guardado.
- **Remitente / Nombre del remitente**: el «from» de los e-mails
  enviados.
- **Destinatario**: dónde recibir los avisos (normalmente tu
  dirección personal).
- **Timeout**: segundos antes de considerar la conexión fallida.
- **Probar conexión**: abre una sesión SMTP, autentica, cierra. No
  envía un e-mail real.
- **Guardar configuración SMTP**: escribe
  `%LOCALAPPDATA%\MedReminder\smtp.settings.json`. La configuración
  se recarga en caliente, sin reiniciar la aplicación.

### Ejemplo: Gmail con app-password

1. Activa la 2FA en tu cuenta de Google.
2. Crea una app-password en
   `myaccount.google.com/apppasswords`.
3. En MedReminder: Servidor `smtp.gmail.com`, Puerto `587`,
   StartTLS activado, Nombre de usuario `tudireccion@gmail.com`,
   Contraseña la app-password que acabas de crear.

Google y otros proveedores pueden modificar sus requisitos: consulta
la documentación de tu proveedor si la prueba de conexión falla.

## Inicio automático con Windows

**Configuración → Inicio automático**: marca la casilla. Se crea
una entrada en `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
que lanza MedReminder con el argumento `--minimized` (arranca en el
área de notificación, ventana oculta). No requiere privilegios de
administrador.

## Copia de seguridad de la base de datos

**Configuración → Copia de seguridad / Restaurar**:

- **Exportar**: elige una carpeta. La DB se copia como
  `medreminder-YYYYMMDD-HHMMSS.db`. Guarda la copia en una unidad
  externa o en la nube personal si quieres más resiliencia.
- **Restaurar**: selecciona una copia anterior. La DB actual se
  renombra a `medreminder.db.bak-<timestamp>` (¡no se pierde!) y se
  sustituye. **Cierra y vuelve a abrir MedReminder** tras la
  restauración para evitar incoherencias.

## Comprobar ahora

El monitor se ejecuta automáticamente cada 30 minutos (configurable
en `appsettings.json` en la clave `Monitoring:IntervalMinutes`). Si
quieres forzar una comprobación inmediata: barra de herramientas →
**Comprobar ahora** o menú del área de notificación → **Comprobar
ahora**.

## Icono en el área de notificación

- **Doble clic** → abre la ventana.
- **Menú contextual (clic derecho)**:
  - Abrir MedReminder
  - Comprobar ahora
  - Configuración…
  - Salir

Cerrar la ventana principal con la X la minimiza al área de
notificación; la aplicación sigue ejecutándose en segundo plano.
Para salir de verdad: menú del área de notificación → **Salir**.

## Idioma de la interfaz

**Configuración → General**: elige el idioma del desplegable
(español, inglés, italiano o francés) y haz clic en **Guardar
idioma**. MedReminder se reinicia automáticamente para aplicar el
cambio.

Notas:
- Las notificaciones toast de Windows siguen siempre el idioma del
  sistema (Windows), independientemente del idioma elegido aquí.
- Las notificaciones e-mail y la ficha de terapia usan el idioma
  seleccionado aquí.

## Diagnóstico

- **Logs**:
  `%LOCALAPPDATA%\MedReminder\logs\medreminder-YYYYMMDD.log`.
  Contiene los ticks del planificador, envíos de notificaciones,
  errores.
- **DB corrupta o incompatible**: elimina `medreminder.db`,
  `medreminder.db-shm`, `medreminder.db-wal` bajo
  `%LOCALAPPDATA%\MedReminder\`. En el próximo inicio la DB se
  recrea vacía. Haz antes una copia manual si tienes datos
  importantes.
- **Aplicación ya en ejecución**: solo una instancia por usuario
  Windows. Si el arranque indica «ya en ejecución», busca el icono
  en el área de notificación.

## Lo que MedReminder NO hace

- No recuerda tomar el medicamento en cada dosis (no es una alarma).
- No proporciona indicaciones terapéuticas ni interacciones
  farmacológicas.
- No sincroniza entre dispositivos.
- No pide medicamentos automáticamente.
- No contacta directamente con tu médico.

Su único propósito es avisarte a tiempo de que necesitas solicitar
una nueva receta.
