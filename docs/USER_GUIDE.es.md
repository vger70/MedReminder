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
2. En el primer inicio la aplicación muestra un **asistente de
   bienvenida** y te pide crear el primer perfil. Ese perfil es
   siempre el **administrador**: puede gestionar el servidor de
   correo compartido y la copia de seguridad automática, y crear
   los demás perfiles (véase *Perfiles múltiples*). Puedes definir
   un PIN opcional en el mismo asistente.
3. La base de datos se crea automáticamente en
   `%LOCALAPPDATA%\MedReminder\profiles\<id-perfil>\medreminder.db`.
4. Arriba se encuentra la barra de herramientas; abajo la barra de
   estado muestra el perfil activo («Perfil: Owner (administrador)»
   para un administrador, «Perfil: Abuela» para un usuario normal).
   El icono en el área de notificación de Windows permanece visible
   mientras la aplicación esté en ejecución.

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
6. **Esquema**: déjalo en **Simple** para una dosis fija tomada cada
   día — es el valor por defecto y coincide con el comportamiento
   histórico de la aplicación. Consulta *Regímenes complejos* más
   abajo para terapias cíclicas, decrecientes, semanales o a
   demanda.
7. **Guardar**.

## Regímenes complejos

No todas las terapias consumen la misma cantidad de medicamento
cada día. En el formulario **Nuevo medicamento** el selector
*Esquema* pasa de **Simple** (una dosis diaria fija) a **Avanzado**
y muestra un desplegable *Tipo de régimen* con cuatro formas
adicionales:

- **Patrón semanal** — una cantidad diferente para cada día de la
  semana (por ejemplo un anticoagulante oral con dosis distintas
  lun/mié/vie respecto al resto).
- **Cíclico (N días on / M off)** — una cantidad fija durante los
  primeros `N` días del ciclo seguidos de `M` días off. Típico de
  las terapias hormonales y de los pulsos de cortisona.
- **Reducción progresiva** — una dosis que baja (o sube)
  gradualmente hasta la dosis final. El panel de reducción ofrece
  dos variantes mediante el selector *Lineal / Por etapas*:
  - **Lineal** — la dosis varía un paso fijo cada X días hasta
    alcanzar la dosis final, y luego se mantiene. Típico del
    descenso simple de glucocorticoides.
  - **Por etapas** — una lista explícita de etapas, cada una con
    su propia dosis y su propia duración en días (por ejemplo
    4/día durante 7 días, luego 2/día durante 7 días, luego 1/día
    durante 14 días). Usa *Añadir etapa* / *Eliminar* para
    construir la secuencia y revisa la vista previa en vivo bajo la
    lista antes de guardar. Marca *Mantener la última dosis como
    dosis de mantenimiento* si la dosis final debe continuar
    indefinidamente en lugar de terminar el ciclo.
- **A demanda (PRN)** — sin consumo planificado. MedReminder sigue
  llevando el stock pero la columna *días restantes* queda vacía
  hasta que cambie el tipo de esquema.

Al seleccionar Avanzado, los campos *Dosis por toma*, *Tomas al
día* y *Horarios* de la parte superior del formulario se
desactivan: el esquema que configures abajo es la única fuente
para la cantidad diaria. Para volver al flujo "un clic" con dosis
fija, vuelve a poner el selector en Simple.

Para cambiar la forma de una terapia en curso, usa *Barra de
herramientas → Cambiar pauta*. Ahí está disponible el mismo
selector Simple/Avanzado y surte efecto a partir de la *Fecha de
vigencia* que elijas, de modo que el esquema anterior sigue siendo
válido para los días previos.

MedReminder no es un dispositivo médico: no comprueba dosis máximas
diarias, no avisa de sobredosis y no verifica interacciones
farmacológicas. Solo sigue la terapia que tu médico ha prescrito y
te recuerda antes de que se agote el stock.

## Recordatorio a la hora de la dosis

Para los medicamentos que tienen **franjas de dosis con hora** (un
momento concreto del día definido en cada franja de administración)
puedes pedir a MedReminder que te avise *en el momento en que la dosis
corresponde*. Marca **Recordarme a la hora de la dosis** en el
formulario de añadir o editar el medicamento. La opción solo está
disponible cuando el medicamento tiene al menos una franja con hora y
todavía queda stock; en caso contrario permanece desactivada.

Cuando está activada, a la hora de cada franja MedReminder muestra una
notificación en el escritorio («Es hora de tomar …»). Si has
configurado las notificaciones por e-mail y has seleccionado el canal
de e-mail para ese medicamento, el mismo recordatorio también se envía
por e-mail.

Algunos detalles útiles que conviene saber:

- **Un recordatorio por franja y día.** Cada franja con hora se activa
  como máximo una vez en un día natural dado, aunque se reinicie la
  aplicación.
- **Ventana de tolerancia.** Si la aplicación no se está ejecutando
  exactamente a la hora de la franja — por ejemplo, el ordenador estaba
  suspendido — el recordatorio se activa igualmente en la siguiente
  comprobación de la aplicación, siempre que sea dentro de los 30
  minutos posteriores a la hora de la franja. Pasada esa ventana, la
  dosis se considera perdida y no se muestra ningún recordatorio;
  MedReminder no lleva un registro de dosis perdidas y nunca da consejos
  clínicos.
- **Un stock a cero lo desactiva.** Cuando el stock llega a cero no se
  envía ningún recordatorio, porque ya no queda nada que tomar.
- **Horario de verano.** En la noche del cambio a horario de verano,
  una franja que cae en la hora omitida no se activa (esa hora no
  existe). En la noche del cambio a horario de invierno, la franja se
  activa una sola vez, como de costumbre.

Este recordatorio es solo un aviso práctico. No registra si has tomado
la dosis y no modifica el stock: para eso usa *Registrar toma*.

## Catálogo de referencia (multi-país)

MedReminder incluye dos instantáneas de un catálogo de medicamentos
de referencia y las utiliza para autocompletar el formulario del
medicamento.

- En los campos **Nombre comercial** y **Principio activo** empieza
  a escribir para ver las coincidencias. Seleccionar una fila
  rellena también el otro campo (y, entre bastidores, el código
  nacional y el código ATC), de modo que no tienes que escribir
  ambos.
- La lista desplegable muestra como máximo 20 filas y se actualiza
  unos 150 ms después de dejar de teclear. Un círculo rojo junto a
  una fila indica que el producto está **suspendido o retirado del
  mercado**: puedes elegirlo igualmente, MedReminder solo señala el
  estado.
- **¿Medicamento no incluido en el catálogo?** Simplemente sigue
  escribiendo lo que sepas. Si no seleccionas ninguna fila de la
  lista, MedReminder guarda el texto tal cual y no se almacena
  ningún vínculo con el catálogo: el recordatorio funciona
  exactamente como antes.
- El **país de referencia** se elige en *Ajustes → General → País
  de referencia*. El valor por defecto es Italia; el cambio se
  aplica en la siguiente apertura del formulario del medicamento.

### Medicamentos con autorización centralizada UE

Algunos medicamentos están autorizados en toda la Unión Europea
mediante el *procedimiento centralizado*, gestionado por la Agencia
Europea de Medicamentos (EMA). MedReminder incorpora el catálogo
EMA EPAR — *European public assessment reports* — y muestra esos
medicamentos en la misma lista desplegable del autocompletado.

- Si tu **país de referencia es un Estado miembro de la UE** (por
  ejemplo Italia por defecto, o cualquier otro país UE elegido en
  Ajustes), el autocompletado muestra **tu catálogo nacional + los
  medicamentos centralizados válidos en toda la UE**, mezclados en
  la misma lista. No tienes que cambiar nada: las filas UE aparecen
  solas cuando coinciden.
- Si estableces el **país de referencia en `EU`**, el
  autocompletado muestra **solo** los medicamentos centralizados
  UE, sin filas nacionales. Útil cuando quieres examinar o
  vincular un producto específicamente a su autorización EMA.
- Un medicamento UE y un producto nacional equivalente pueden
  aparecer al mismo tiempo en la lista; las dos filas no se
  deduplican. Elige la que corresponde a la caja que tienes en la
  mano.

### Catálogos nacionales español y francés

El catálogo español procede de AEMPS CIMA (registro
«Medicamentos») y el catálogo francés de ANSM BDPM (*Base de
données publique des médicaments*). En el autocompletado se
comportan exactamente como el catálogo italiano:

- Configura **Ajustes → General → País de referencia** en `ES` o
  `FR` una vez cargada la instantánea correspondiente (`ES` y `FR`
  aparecen automáticamente en el menú desplegable en cuanto sus
  catálogos están en la base de datos).
- El autocompletado enumera entonces **tu catálogo nacional + los
  medicamentos centralizados UE**, mezclados en la misma lista.
  España y Francia son Estados miembros de la UE, así que las
  filas UE se incluyen por defecto igual que para Italia.
- Las demás reglas se mantienen: selecciona una fila para rellenar
  ambos lados, o sigue escribiendo para guardar un texto libre que
  la aplicación no conoce.

**Fuentes de los datos y condiciones de reutilización.** El
catálogo italiano procede de los datos abiertos de AIFA
(Agenzia Italiana del Farmaco), publicados bajo licencia Creative
Commons Attribution 4.0 International (CC BY 4.0). El catálogo UE
procede del conjunto de datos EMA EPAR, reutilizado según el aviso
legal de la EMA (decisión 2011/833/UE sobre la reutilización de
los documentos de la Comisión). El catálogo español procede de
AEMPS CIMA, reutilizado según el régimen español de reutilización
de la información del sector público (Ley 37/2007). El catálogo
francés procede de ANSM BDPM, reutilizado bajo Licence Ouverte
Etalab 2.0. El cuadro de diálogo Acerca de y el archivo
`THIRD-PARTY-NOTICES.md` en la raíz de la instalación incluyen
las atribuciones completas.

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
  **La dosis y la frecuencia NO se modifican desde aquí**: usa
  *Barra de herramientas → Cambiar pauta* (véase *Regímenes
  complejos* más arriba).
- **Desactivar**: barra de herramientas → **Desactivar**. El
  medicamento desaparece de las comprobaciones automáticas y de los
  avisos, pero los datos históricos (movimientos, notificaciones)
  permanecen en la DB para auditoría.

## Perfiles múltiples y roles administrador/usuario

MedReminder puede gestionar medicamentos para **varias personas**
desde la misma cuenta de Windows — caso típico: un padre o madre
que sigue su propia terapia y la de uno o dos familiares. Cada
perfil tiene su propia base de datos y su propio destinatario de
correo; el servidor SMTP, la carpeta de la copia de seguridad
automática y el registro de perfiles se comparten y los gestiona
un perfil **administrador**.

### Roles

- **Administrador** — gestiona los ajustes globales (SMTP, Copia
  de seguridad, lista de perfiles, PIN de cualquier perfil)
  además de sus propios datos. Siempre debe existir al menos un
  administrador.
- **Usuario** — gestiona solo su propio perfil (medicamentos,
  stock, terapias, destinatario de correo personal). No ve la
  pestaña SMTP ni la pestaña Copia de seguridad en Ajustes, y no
  ve `Herramientas → Gestionar perfiles…`.

El rol se elige al crear el perfil y **no se puede cambiar
después**. Si en el futuro necesitas cambiar el rol de un perfil,
la solución actual es crear un perfil nuevo con el rol deseado y
copiar los datos.

El rol es una barrera «suave»: quien tiene acceso al sistema de
archivos puede editar `profiles.json` a mano y hacerse
administrador. La interfaz respeta el rol, el sistema de archivos
no.

### Crear perfiles adicionales (administrador)

1. `Herramientas → Gestionar perfiles…` — esta entrada solo existe
   para los administradores.
2. **Nuevo perfil** → introduce un nombre, elige Administrador o
   Usuario (predeterminado: Usuario), define opcionalmente un PIN.
   Confirma.
3. El nuevo perfil aparece inmediatamente en el selector en el
   próximo inicio.

### Cambiar de perfil

`Archivo → Cambiar perfil…` abre el selector. Elige el perfil de
destino y confirma: la aplicación se reinicia automáticamente para
que el nuevo perfil esté totalmente aislado. Si el perfil elegido
tiene un PIN, la solicitud aparece antes de que la aplicación se
abra.

### Renombrar, cambiar PIN, eliminar

`Herramientas → Gestionar perfiles…` (solo administrador) ofrece
también:

- **Renombrar** — solo el nombre mostrado. El identificador interno
  nunca cambia.
- **Cambiar PIN** — establecer, cambiar o eliminar el PIN de
  cualquier perfil.
- **Eliminar** — pide **escribir el nombre del perfil** para
  confirmar. Una casilla separada permite eliminar también los
  datos del perfil del disco; está desactivada por defecto, así la
  carpeta queda disponible para recuperación manual.

El perfil activo no se puede eliminar (cambia antes de perfil), y
tampoco el último administrador restante.

### Sobre el PIN

El PIN es una **barrera, no seguridad**. Bloquea los cambios
accidentales de perfil, pero **no** cifra los datos — cualquiera
con acceso a este PC puede seguir abriendo los archivos del
perfil. Tres intentos incorrectos cierran la solicitud y la
aplicación.

Si olvidas un PIN, elimínalo a mano en
`%LOCALAPPDATA%\MedReminder\profiles.json` (borra `PinHash` y
`PinSalt` y pon `PinIterations` a `0` en la entrada afectada).
Esto se documenta en lugar de resolverse con un flujo de
«restablecer PIN» a propósito: la recuperación no es un bug,
porque el PIN no es seguridad.

### Disposición en disco

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json                        ← registro de perfiles
├── smtp.settings.json                   ← SMTP compartido (admin)
├── smtp.protected                       ← contraseña cifrada con DPAPI
├── backup.settings.json                 ← config. copia compartida (admin)
├── backup.state.json                    ← estado de la última copia
├── logs\medreminder-YYYYMMDD.log
└── profiles\
    ├── <id-perfil>\                     ← una carpeta por perfil
    │   ├── medreminder.db (+ -wal, -shm)
    │   └── notifications.settings.json  ← ToAddress de este perfil
    └── …
```

### La copia automática cubre todos los perfiles

Cuando la copia automática está activada, cada ejecución diaria
hace copia de seguridad de la base de datos de **todos** los
perfiles en la carpeta compartida, con nombres del tipo
`medreminder-<id-perfil>-YYYYMMDD-HHmmss.db`. La retención se
aplica por perfil, de modo que la copia más reciente de un perfil
no protege las copias más antiguas de otro.

Al restaurar desde `Ajustes → Copia de seguridad → Restaurar
copia…`, el diálogo pregunta en qué perfil debe cargarse la base
importada. Por defecto selecciona el perfil indicado en el nombre
del archivo. Si restauras en un perfil distinto al activo, la
aplicación no se reinicia; si restauras en el perfil activo, se
reinicia para abrir la nueva base limpiamente.

### Inicio automático con Windows

La entrada de inicio automático de Windows es única por usuario de
Windows. Al iniciar sesión, la aplicación abre el perfil **más
reciente** sin mostrar el selector; si ese perfil tiene PIN, la
solicitud se muestra sobre la ventana vacía. Para abrir un perfil
distinto en el arranque, usa `Archivo → Cambiar perfil…` una vez
que la aplicación esté abierta.

### Actualización desde una instalación mono-usuario

Si ya tienes un archivo `medreminder.db` en
`%LOCALAPPDATA%\MedReminder\` de una versión anterior, en el
próximo inicio la aplicación ejecuta una **migración V1 → V2**
una vez:

1. Hace una copia obligatoria en
   `%LOCALAPPDATA%\MedReminder\backups\pre-migration-YYYYMMDD-HHmmss\`
   que contiene el `medreminder.db` original (y sus archivos
   asociados) y el `smtp.settings.json` original.
2. Mueve la base a `profiles\default\medreminder.db` y crea el
   `profiles.json` inicial con un único perfil administrador
   llamado `User`.
3. Extrae el destinatario (`Smtp.ToAddress`) de
   `smtp.settings.json` a
   `profiles\default\notifications.settings.json`.

La migración es **atómica** — si un paso falla tras la copia
previa, la aplicación revierte al estado V1 y conserva la copia
de pre-migración.

La **copia de pre-migración no se limpia automáticamente**: tras
comprobar que la aplicación migrada abre los mismos datos, puedes
eliminar manualmente la carpeta `backups\pre-migration-*`.
Renombra el perfil `User` como prefieras desde
`Herramientas → Gestionar perfiles… → Renombrar`.

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

## Notificaciones al cuidador

**Configuración → Notificaciones → E-mail del cuidador (opcional)**.

Un perfil puede designar un segundo destinatario — por ejemplo un
familiar o un cuidador que gestiona la renovación de la receta en tu
nombre. Cuando este campo está configurado, cada e-mail enviado al
destinatario principal también se envía al cuidador, en el **mismo**
mensaje. Nada más cambia: el transporte, el contenido del mensaje y los
momentos de envío son exactamente los mismos que antes.

- **Para activarlo**: escribe la dirección e-mail del cuidador y
  guarda.
- **Para desactivarlo**: vacía el campo y guarda. Un campo vacío
  significa que no hay cuidador configurado — el comportamiento por
  defecto.
- **Ambas direcciones son visibles para ambos destinatarios**: el
  cuidador y el destinatario principal pueden ver la dirección del
  otro en el e-mail. Es intencional para que una respuesta llegue a
  todos.
- La dirección del cuidador no puede coincidir con la del
  destinatario principal y debe ser una dirección e-mail válida; de
  lo contrario el guardado se rechaza con un mensaje.

El ajuste es por perfil: el cuidador de un perfil no es el cuidador de
otro perfil.

## Configurar el inicio automático

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

## Exportación e importación

Además de la copia de seguridad directa de la base de datos, MedReminder
puede producir un único **archivo cifrado y portable** con todos tus
datos. A diferencia de una copia normal, este archivo no está vinculado
a tu cuenta de Windows ni a tu PC, por lo que es el método recomendado
para mover MedReminder a un nuevo ordenador.

**Configuración → Copia de seguridad → Exportar todos los datos
(cifrado)…**:

- Elige dónde guardar el archivo (extensión `.mrz`).
- Elige una **frase de contraseña** (mínimo 12 caracteres) e
  introdúcela dos veces.
- Activa opcionalmente los ajustes compartidos a incluir: ajustes
  SMTP, contraseña SMTP, preferencias de copia, preferencias de
  usuario (idioma y país de referencia del catálogo). Todos
  desactivados por defecto. Si incluyes la contraseña SMTP, se
  vuelve a cifrar con tu frase — nunca se escribe en texto claro.
- Haz clic en **Exportar**.

**La frase de contraseña no puede recuperarse.** No existe reinicio,
puerta trasera ni copia en servidor. Si pierdes la frase, el archivo no
podrá leerse nunca — guárdala en un lugar seguro.

**Configuración → Copia de seguridad → Importar desde exportación…**:

- Selecciona el archivo `.mrz`. MedReminder muestra su contenido
  (versión, fecha, alcance, ajustes incluidos) antes de hacer nada.
- Escribe la frase de contraseña.
- Marca **«Entiendo que esto sobrescribirá los datos del perfil
  actual.»** La importación reemplaza íntegramente los datos del
  perfil actual — no hay modo de fusión. Antes se conserva una copia
  de seguridad como `medreminder.db.bak-<timestamp>`.
- Si el archivo se exportó desde otro perfil, MedReminder pide
  confirmación: importarlo sustituye los datos del perfil activo por
  los del otro perfil.
- Haz clic en **Importar** y luego **reinicia** MedReminder cuando
  se solicite para que los datos importados se carguen limpiamente.

Si la frase es incorrecta, el archivo está dañado o fue producido por
una versión más reciente de MedReminder, la importación se detiene con
un mensaje claro y tus datos actuales quedan intactos.

El formato del archivo está documentado públicamente en
[`docs/EXPORT-FORMAT.md`](EXPORT-FORMAT.md), así que tus datos nunca
están bloqueados — pueden descifrarse con herramientas estándar si es
necesario.

## Copia de seguridad en carpeta en la nube

MedReminder también puede escribir la copia de seguridad automática
diaria como una **instantánea cifrada** en una carpeta local que tu
sistema operativo ya está sincronizando (OneDrive, iCloud Drive,
Dropbox, Google Drive Desktop, …). Es la forma económica de llevar tus
datos de un "PC de casa" a un "PC del trabajo" sin ningún servidor, y
mantiene una copia fuera del equipo por si falla el disco.

**Esto no es sincronización en tiempo real.** MedReminder escribe como
máximo una instantánea al día, y solo un ordenador a la vez debería
escribir. Si editas medicamentos en dos dispositivos entre dos
instantáneas, las dos copias divergen — y la siguiente restauración
borra los datos del equipo en el que restauras. Decide de antemano qué
dispositivo es el "activo" y restaura en el otro solo cuando cambies.

### Configuración en el primer dispositivo

**Configuración → Copia de seguridad → Copia de seguridad a carpeta
sincronizada (cifrada)**:

- Marca la casilla.
- Elige una carpeta dentro de la carpeta de sincronización local de tu
  servicio en la nube (por ejemplo
  `C:\Users\<nombre>\OneDrive\MedReminder`). MedReminder nunca se
  comunica por sí mismo con OneDrive / iCloud / Dropbox — solo escribe
  los archivos ahí, y el agente de sincronización del sistema los sube.
- Indica el número de instantáneas que conservar (por defecto: 30).
- Haz clic en **Establecer / cambiar…** junto a Frase de contraseña de
  copia de seguridad y elige una frase (mínimo 12 caracteres). Esta
  frase nunca sale del equipo.
- Guarda.

A partir del siguiente ciclo diario, MedReminder escribe
`medreminder-<profileId>-<timestamp>.mrz` en la carpeta. El archivo
está cifrado con una clave derivada de tu frase de contraseña de
copia; el servicio en la nube nunca ve tus datos en claro.

Se escribe una instantánea para **cada perfil** del equipo, como en la
copia local, y todas se cifran con la misma frase de contraseña de
copia. Quien conozca la frase puede, por tanto, leer los datos de
todos los perfiles, incluidos los protegidos con PIN.

### Configuración en el segundo dispositivo

- Instala MedReminder.
- **Configuración → Copia de seguridad → Establecer / cambiar…** e
  introduce la **misma** frase de contraseña de copia que configuraste
  en el primer dispositivo. Es el único paso imprescindible: sin la
  misma frase, el segundo equipo no puede descifrar lo que escribió el
  primero.
- La instantánea automática diaria queda desactivada en el segundo
  dispositivo — solo hace falta en un equipo.

### Restauración en el segundo dispositivo

**Configuración → Copia de seguridad → Restaurar desde carpeta en la
nube…**:

- Indica en la ventana la carpeta de sincronización local (la misma en
  la que escribe el primer dispositivo).
- Elige la instantánea más reciente de la lista. Cada fila muestra la
  fecha, el nombre del perfil (o su id, si el perfil no existe en este
  equipo) y un breve "hash del dispositivo" para distinguir
  instantáneas de equipos distintos. El hash del dispositivo es una
  huella SHA-256 del nombre de host del equipo de origen — suficiente
  para agrupar instantáneas por procedencia, no para identificar el
  equipo.
- Se preselecciona la instantánea más reciente del perfil activo. La
  restauración siempre sobrescribe el perfil **activo**: para
  restaurar otro perfil, cambia antes a ese perfil. Si eliges la
  instantánea de un perfil distinto, MedReminder pide confirmación
  antes de sustituir con ella los datos del perfil activo.
- Marca **"Entiendo que esto sobrescribirá los datos del perfil
  actual."** — la restauración solo sobrescribe.
- Haz clic en **Restaurar**. MedReminder descifra la instantánea,
  sustituye la base de datos del perfil actual y te pide reiniciar.

### Notas

- **Perder la frase de contraseña es perder los datos.** No hay forma
  de restablecerla. La frase se guarda localmente, cifrada con las
  credenciales de tu cuenta de Windows; nunca sale del equipo ni
  aparece en la nube.
- La instantánea automática diaria **no** incluye la contraseña SMTP
  ni tus preferencias de usuario — para eso, usa la exportación
  cifrada puntual descrita arriba, con las casillas de ajustes
  compartidos.
- La retención de MedReminder solo elimina archivos antiguos de la
  carpeta visible. Tu servicio en la nube probablemente conserva los
  archivos eliminados en su propia papelera (OneDrive: 30 días por
  defecto) — MedReminder no puede vaciarla por ti, y no lo intenta.
- **No** coloques el archivo de base de datos en uso en una carpeta
  sincronizada. Ahí solo deben ir las instantáneas cifradas `.mrz`.

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

## Apoyar el desarrollo

Si el mantenedor lo ha habilitado, la opción **? → Apoyar el
desarrollo…** abre una pequeña ventana en la que puedes, de forma
totalmente voluntaria, contribuir al proyecto. Es opcional y nunca es
necesario para usar MedReminder.

- Elige un importe fijo (2 €, 5 €, 10 €, 20 €) o, cuando se ofrezca, un
  **importe personalizado**.
- Elige un método de pago (Stripe o PayPal).
- Haz clic en **Continuar con …**: MedReminder abre la página de pago
  oficial del proveedor en tu navegador predeterminado.

Con un importe personalizado eliges la cifra exacta **en la página del
proveedor**, no dentro de MedReminder. La aplicación nunca procesa el
pago, no ve los datos de tu tarjeta y no puede confirmar que un pago se
haya completado: solo abre la página. Si el mantenedor no ha
configurado esta función, la opción de menú no aparece.

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

- No registra si has tomado una dosis, no hace seguimiento de la
  adherencia terapéutica y no alerta por dosis perdidas (el
  recordatorio a la hora de la dosis es solo un aviso práctico, no
  un sistema de adherencia).
- No proporciona indicaciones terapéuticas ni interacciones
  farmacológicas.
- No sincroniza entre dispositivos.
- No pide medicamentos automáticamente.
- No contacta directamente con tu médico.

Su único propósito es avisarte a tiempo de que necesitas solicitar
una nueva receta.
