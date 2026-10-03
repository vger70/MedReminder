# MedReminder — Guía de usuario

MedReminder te avisa **a tiempo** cuando un medicamento está a punto de
acabarse, para que puedas pedir la receta a tu médico antes de quedarte
sin él. También puede recordarte la hora de cada dosis, seguir a varias
personas y funcionar en varios ordenadores.

> **MedReminder es un recordatorio organizativo, no un producto
> sanitario.** No proporciona diagnósticos, indicaciones
> terapéuticas, cambios de terapia ni sugerencias clínicas. Toda
> decisión sobre la terapia debe tomarse con tu médico.

Pulsa **F1** o abre **? → Guía de usuario** para leer esta guía dentro
de la aplicación. La arquitectura técnica se describe en
`docs/ANALYSIS.md`.

---

## Índice

1. [Primeros pasos](#start)
   - [Primer inicio](#first-start) · [La ventana principal](#main-window) ·
     [Dónde encontrar cada cosa](#where)
2. [Medicamentos](#medicines)
   - [Añadir un medicamento](#add-medicine) ·
     [Horarios de toma](#slots) ·
     [Regímenes complejos](#regimens) ·
     [Recordatorio a la hora de la dosis](#dose-reminder) ·
     [Editar, desactivar, eliminar](#edit-medicine)
3. [Encontrar un medicamento: catálogo y código de barras](#catalogue)
4. [Stock](#stock)
   - [Añadir una caja](#add-package) · [Registrar una toma](#intake) ·
     [Corregir el stock](#correct) · [Contar existencias](#count) ·
     [Historial](#history)
5. [Cronología, ficha de terapia y solicitud de receta](#documents)
6. [Notificaciones y correo](#notifications)
7. [Varias personas: perfiles y roles](#profiles)
8. [Proteger tus datos: copia de seguridad y exportación](#backup)
9. [Varios ordenadores](#devices)
   - [¿Qué opción necesito?](#devices-choice) ·
     [Sincronizar un perfil entre PC](#sync) ·
     [Compartir la instalación](#installation) ·
     [El dispositivo principal](#master) ·
     [Dispositivo perdido o sustituido](#remove-device)
10. [Configuración y uso diario](#settings)
11. [Problemas y respuestas](#faq)
12. [Dónde guarda MedReminder los datos](#data)
13. [Lo que MedReminder no hace](#limits)

---

<a id="start"></a>
## 1. Primeros pasos

<a id="first-start"></a>
### Primer inicio

1. Ejecuta `MedReminder.exe`.
2. Se abre la ventana **Bienvenido a MedReminder**. Elige:
   - **Crear perfil** — el caso normal en tu primer ordenador. Escribe
     tu nombre y, si quieres, un PIN. Este primer perfil es el
     **administrador**: gestiona el correo, la copia de seguridad y los
     demás perfiles (ver [Varias personas](#profiles)).
   - **Unirse a una instalación existente…** — solo si MedReminder ya se
     usa en otro ordenador tuyo y quieres que este también forme parte
     (ver [Compartir la instalación](#installation)).
3. Se abre la ventana principal. El icono de MedReminder en el área de
   notificación de Windows (junto al reloj) sigue visible mientras la
   aplicación está en marcha.

**Windows SmartScreen.** El programa no está firmado digitalmente. En el
primer inicio Windows puede mostrar una ventana azul "Windows protegió
su PC": haz clic en **Más información** y luego en **Ejecutar de todas
formas**. Windows recuerda la elección. Si instalas desde el paquete
MSI, la ventana de permisos indica "Editor desconocido" por el mismo
motivo.

<a id="main-window"></a>
### La ventana principal

- **Menús** arriba: **Archivo**, **Terapia**, **Stock**,
  **Herramientas** y **?** (ayuda).
- **Barra de herramientas** bajo los menús: *Nuevo medicamento*,
  *Registrar toma* y, a la derecha, un cuadro de búsqueda (**Ctrl+F**)
  que filtra la lista por nombre.
- **Navegación** a la izquierda: *Medicamentos* (esta lista), luego
  *Cronología de la terapia*, *Ficha de terapia*, *Solicitar receta*, *Planificar stock*, *Recetas*, *Vencimientos administrativos*,
  *Instalación* (administradores) y *Configuración*, que se abren en su
  propia ventana. Si la ventana es estrecha, solo muestra los iconos.
- **Resumen** encima de la lista: cuántos medicamentos están
  *Agotados*, *Por agotarse*, *Suspendidos*, y el total. Haz clic en un
  recuadro para ver solo esos medicamentos; un segundo clic los muestra
  todos.
- **Lista de medicamentos** en el centro: una fila por medicamento, con
  el stock, los días restantes y la fecha estimada de agotamiento. La
  columna **Estado** muestra el estado con una etiqueta de color. El
  clic derecho en una fila ofrece los comandos para ese medicamento
  (editar, registrar toma, añadir envase…); doble clic o **F2** lo
  editan.
- **Barra de estado** abajo: el perfil abierto ("Perfil: Ana (admin)"
  para un administrador).

Cerrar la ventana con la **X** no cierra MedReminder: sigue en el área
de notificación, así que los avisos siguen llegando. Para salir, haz
clic derecho en el icono y elige **Salir**.

<a id="where"></a>
### Dónde encontrar cada cosa

| Quiero… | Ir a |
|---|---|
| Añadir un medicamento | **Terapia → Nuevo medicamento…** |
| Cambiar dosis o frecuencia | **Terapia → Cambiar dosis/frecuencia…** |
| Registrar una caja comprada | **Stock → Añadir caja…** o **Stock → Reponer por código de barras…** |
| Ajustar el stock a lo que realmente tengo | **Stock → Contar existencias…** |
| Deshacer un registro erróneo | **Stock → Historial…** |
| Imprimir la terapia para un médico | **Terapia → Ficha de terapia…** |
| Pedir una receta | **Terapia → Solicitar receta…** |
| Seguir una receta hasta la farmacia | **Terapia → Recetas…** |
| Recordar un plan terapéutico, una exención o un control | **Terapia → Vencimientos administrativos…** |
| Ver las próximas fechas en Outlook, Google Calendar o en el teléfono | **Terapia → Exportar al calendario…** |
| Comprobar el stock para un viaje o hasta la próxima visita a la farmacia | **Terapia → Planificar stock…** |
| Configurar correo, idioma, copia de seguridad | **Herramientas → Configuración…** |
| Añadir una persona | **Herramientas → Gestionar perfiles…** (administrador) |
| Usar MedReminder en otro PC | **Herramientas → Sincronización…** y **Herramientas → Instalación…** (administrador) |
| Abrir el perfil de otra persona | **Archivo → Cambiar perfil…** |

---

<a id="medicines"></a>
## 2. Medicamentos

<a id="add-medicine"></a>
### Añadir un medicamento

1. **Terapia → Nuevo medicamento…** (o el botón *Nuevo medicamento*).
2. Empieza a escribir el **nombre**: el catálogo propone los
   medicamentos que coinciden (ver
   [Encontrar un medicamento](#catalogue)). Elegir uno rellena el
   principio activo y la presentación. También puedes hacer clic en
   **Escanear código…**.
3. Rellena los campos obligatorios, marcados con `*`: *Unidad*, *Dosis
   por toma*, *Tomas al día*, *Fecha de inicio de terapia* y *Umbral de
   aviso (días)*.
   - El **umbral de aviso** es cuántos días antes de agotarse quieres
     que te avise. Deja tiempo para conseguir la receta y comprar el
     medicamento, por ejemplo 10 días.
4. **Stock inicial**: cuántos comprimidos (o ml, dosis…) tienes ahora.
5. **Canales de notificación**: marca **Notificación Windows** y/o
   **E-mail**. El correo solo funciona después de configurarlo (ver
   [Notificaciones y correo](#notifications)).
6. Opcional: *Fecha de fin de terapia*, *Médico de referencia*,
   *Notas*, horarios de toma, *Recordarme a la hora de la dosis*.
7. **Guardar**.

A partir de ese momento MedReminder descuenta solo la dosis cada día.
No hace falta registrar cada comprimido que tomas.

<a id="slots"></a>
### Horarios de toma

En **Horarios de toma (opcional)** puedes repartir la dosis diaria:
**Añadir…** abre una ventana donde indicas la dosis, una hora opcional
(*Con hora específica*) y una descripción como "Después del desayuno"
(elige una de las *Descripciones comunes* o escribe la tuya). Los
horarios aparecen en la ficha de terapia y, si tienen hora, pueden
recordarte la dosis.

Sin horarios, el medicamento usa "dosis × tomas al día".

Una dosis que se toma solo cuando hace falta (por ejemplo un analgésico
para el dolor de cabeza) se marca **Si es necesario** en la ventana del
horario: la descripción *Si es necesario* la marca sola. Una dosis si es
necesario nunca se descuenta automáticamente y no forma parte del total
diario: el stock solo baja cuando registras la toma. Si todos los
horarios de un medicamento son si es necesario, se comporta como un
esquema *Si es necesario (PRN)*.

Desde la versión que introdujo esta opción, los horarios descritos como
"Si es necesario" se tratan como tales a partir de ese día. Antes se
descontaban cada día: si el stock mostrado es menor que el real, haz un
[recuento](#count) para realinearlo.

**Terapia → Horarios de las dosis…** muestra los momentos del día con
su hora ("Por la mañana" = 08:00, "Antes de comer" = 13:00, …) y las
horas usadas para los medicamentos sin horarios (1 al día = 08:00, 2
al día = 08:00 y 20:00, …). Puedes cambiar las horas, ocultar los
momentos que no usas y añadir los tuyos; en la ventana del horario, al
elegir un momento ves su hora. Estas horas solo sitúan las dosis en el
día y nunca cambian el stock registrado. Valen para este ordenador: no
se sincronizan.

<a id="regimens"></a>
### Regímenes complejos

No todas las terapias usan la misma cantidad cada día. En la ventana
del medicamento pon **Esquema** en **Avanzado** y elige un **Tipo de
régimen**:

| Tipo de régimen | Ejemplo |
|---|---|
| **Patrón semanal** | Una cantidad distinta para cada día de la semana, por ejemplo un anticoagulante con dosis distintas lun, mié, vie. |
| **Cíclico (N días on / M off)** | Una cantidad durante N días y luego M días sin, por ejemplo 21 días sí y 7 no. |
| **Reducción progresiva** — *Lineal* | La dosis cambia en un paso fijo cada pocos días hasta la dosis final y luego se mantiene. |
| **Reducción progresiva** — *Por etapas* | Una lista de etapas, cada una con su dosis y duración, por ejemplo 4 al día durante 7 días, 2 al día durante 7 días, 1 al día durante 14 días. Usa **Añadir etapa** / **Quitar**; el total aparece bajo la lista. Marca *Mantener la última dosis como mantenimiento* si la última dosis continúa indefinidamente. |
| **A demanda (PRN)** | Sin consumo programado: el stock se sigue, pero no se estima fecha de agotamiento. |

Con **Avanzado**, los campos de dosis de la parte superior de la
ventana no se usan. Vuelve a **Simple** para una dosis diaria fija.

**¿Cambia la terapia?** Usa **Terapia → Cambiar dosis/frecuencia…** y
elige la fecha **Efectivo desde**. El esquema anterior sigue siendo
válido para los días previos. Si la fecha está en el pasado, el consumo
ya descontado desde esa fecha se recalcula con el nuevo esquema.

MedReminder no comprueba dosis máximas, sobredosis ni interacciones
entre medicamentos: solo sigue la terapia que prescribió tu médico.

<a id="dose-reminder"></a>
### Recordatorio a la hora de la dosis

Marca **Recordarme a la hora de la dosis** en la ventana del medicamento
para recibir un aviso ("Es hora de tomar …") en cada horario con hora.
Está disponible cuando el medicamento tiene al menos un horario con
hora y le queda stock.

- El aviso aparece en pantalla; si el medicamento usa el canal
  **E-mail** y el correo está configurado, también llega por correo.
- **Una vez por horario y día**, aunque reinicies la aplicación.
- Si el PC estaba en suspensión a esa hora, el aviso llega igualmente
  dentro de **30 minutos**; pasado ese tiempo se omite.
- Con **stock cero** no se envía ningún aviso.
- La noche en que el reloj se adelanta, un horario dentro de la hora
  saltada no suena.

El aviso no registra si tomaste la dosis ni cambia el stock.

<a id="edit-medicine"></a>
### Editar, desactivar, eliminar

- **Editar**: doble clic en la fila, o **Terapia → Editar**. Puedes
  cambiar todo excepto la dosis y la frecuencia (usa *Cambiar
  dosis/frecuencia…*).
- **Desactivar**: **Terapia → Desactivar** cuando terminas una terapia.
  El medicamento se oculta y ya no recibe avisos; su historial se
  conserva. **Terapia → Mostrar medicamentos desactivados** lo vuelve a
  mostrar; para reactivarlo, ábrelo con **Editar** y marca **Activo**.
  Los días en que estuvo inactivo no cuentan como consumo.
- **Eliminar**: **Terapia → Eliminar…** quita un medicamento introducido
  por error. Solo funciona mientras no se haya registrado nada (ningún
  stock, ni siquiera la cantidad inicial, ninguna toma, ningún
  recuento). Si no, desactívalo. Con la sincronización activa,
  desaparece también de los demás ordenadores.

---

<a id="catalogue"></a>
## 3. Encontrar un medicamento: catálogo y código de barras

### El catálogo de referencia

MedReminder incluye los listados oficiales de medicamentos de
**Italia** (AIFA), **España** (AEMPS), **Francia** (ANSM) y los
medicamentos autorizados para toda la **Unión Europea** (EMA).

- En la ventana del medicamento, escribe parte del **nombre** o del
  **principio activo**: aparecen hasta 20 resultados. Elegir uno
  rellena los demás campos.
- Un **círculo rojo** junto a una fila indica que el producto está
  suspendido o retirado. Puedes elegirlo igualmente.
- **¿No está en la lista?** Escribe el nombre y guarda: el medicamento
  funciona igual, solo que sin vínculo con el catálogo.
- **¿Qué país?** **Herramientas → Configuración… → General → País de
  referencia** (por defecto: Italia). Solo un administrador puede
  cambiarlo: se aplica a todos los perfiles y, con una instalación
  compartida, a todos los dispositivos. Con IT, ES o FR la lista
  contiene también los medicamentos de la UE; con **EU**, solo esos. Un
  medicamento puede aparecer dos veces (nacional y UE): elige el que
  coincide con tu caja.
- **Actualización automática.** Cuando **Buscar actualizaciones
  automáticamente (GitHub)** está activado (Configuración → General),
  MedReminder descarga al iniciar, y una vez al día mientras siga
  abierto, la última lista mensual de tu país y la de la UE, si son más
  recientes. Sin conexión no cambia nada. Con varios perfiles solo se
  actualiza el abierto; los demás, la primera vez que se abren.

**Fuentes.** Datos abiertos de AIFA (CC BY 4.0); datos EMA EPAR (aviso
jurídico de la EMA, Decisión de la Comisión 2011/833/UE); AEMPS CIMA
(Ley 37/2007 sobre reutilización de la información del sector
público); ANSM BDPM (Licence Ouverte Etalab 2.0). Las atribuciones
completas están en **? → Acerca de MedReminder…** y en
`THIRD-PARTY-NOTICES.md`.

### Escanear el código de barras

Con un lector de códigos de barras USB o una webcam puedes rellenar un
medicamento sin escribir.

1. En la ventana del medicamento haz clic en **Escanear código…**.
2. Escanea el código de barras de la caja, o escribe el código impreso
   debajo y pulsa **Intro**.
3. Si el código está en el catálogo, el formulario se rellena. Si no, la
   ventana muestra el código leído y no cambia nada.

Consejos:

- Haz clic en **Escanear código…** *antes* de escanear; si no, el código
  se escribe en el campo que tiene el cursor.
- En las cajas italianas el código que se lee es el código de barras
  **AIC** (`A` seguida de 9 cifras). Si el lector no lo reconoce, activa
  la simbología **Code 32** (Pharmacode italiano) en su configuración.
  El código cuadrado (DataMatrix) necesita un lector 2D y a menudo no
  está en el catálogo.
- Configura el lector con la misma distribución de teclado que Windows.

**Con la webcam.** Haz clic en **Usar la webcam** en la ventana de
escaneo. Sujeta la caja a 10–20 cm, con el código dentro del marco y
buena luz. La cámara se apaga cuando lee un código, cuando haces clic
en **Usar el lector**, cuando cierras la ventana o tras 30 segundos. Si
Windows bloquea la cámara, haz clic en **Abrir configuración de
privacidad**, activa *Permitir que las aplicaciones de escritorio
accedan a la cámara* y luego **Reintentar**. No se guarda ni se envía
ninguna imagen.

**Reponer escaneando.** **Stock → Reponer por código de barras…**:
escanea la nueva caja y el medicamento correspondiente se abre en la
ventana de stock, ya en *Nueva caja* con la cantidad habitual. Si ningún
medicamento tiene ese código, puedes añadir un medicamento nuevo o
vincular el código a uno existente.

### Medicamentos en desabastecimiento (Italia)

Con Italia como país de referencia, MedReminder descarga la lista AIFA
de medicamentos en desabastecimiento junto con el catálogo (al inicio y
una vez al día, si **Buscar actualizaciones automáticamente** está
activo). Un medicamento cuya caja (código AIC, rellenado desde el
catálogo o el código de barras) está en la lista lo muestra en la
columna **Disponibilidad** de la lista:

- *En desabastecimiento*: AIFA indica la caja como difícil de
  encontrar;
- *Desabastecimiento desde el …*: AIFA anuncia un desabastecimiento
  desde esa fecha.

Pasa el ratón por la celda para leer el inicio, el fin previsto (a
menudo no comunicado, y puede cambiar), el motivo, si AIFA indica
medicamentos equivalentes y la fecha de la lista. También recibes una
notificación por cada desabastecimiento, por los canales del
medicamento.

MedReminder no indica sustitutos: consulta a tu médico o farmacéutico y
solicita la receta a tiempo.

---

<a id="stock"></a>
## 4. Stock

MedReminder reduce el stock solo cada día según el esquema. Solo
registras lo que cambia el stock de otra manera.

Durante el día la columna del stock muestra una estimación: el stock al
inicio del día menos las dosis de hoy cuya hora ya pasó. Los horarios
sin hora usan la hora de su momento (**Terapia → Horarios de las
dosis…**); un horario con descripción libre y sin hora se cuenta al
final del día. Al pasar el ratón por el stock ves el valor al inicio
del día. El stock registrado, el historial y la fecha de agotamiento
se actualizan después de medianoche, mientras que los días restantes
siguen el stock mostrado; si registras una toma, ese día
cuenta la cantidad registrada.

<a id="add-package"></a>
### Añadir una caja

1. Selecciona el medicamento.
2. **Stock → Añadir caja…**.
3. Elige el tipo:
   - **Nueva caja** — tras una compra (el caso habitual);
   - **Añadido manual** — por ejemplo muestras del médico;
   - **Corrección positiva** — habías contado de menos.
4. Introduce la cantidad y confirma.

Una caja nueva reinicia el ciclo de aviso: cuando el stock vuelve a
bajar del umbral, recibes un aviso nuevo.

<a id="intake"></a>
### Registrar una toma

**Terapia → Registrar toma…** (o el botón de la barra) registra una
toma como **Tomada**, **Saltada** o **Cancelada**, con el día y la
cantidad. Los días normales no hace falta. Úsalo cuando un día es
distinto del esquema: en cuanto registras una toma para un día, el
descuento automático de ese día se sustituye por lo que registraste.

Para una dosis además del esquema, por ejemplo una dosis si es
necesario, marca **Dosis extra si es necesario**: la cantidad se
descuenta y las dosis programadas del día se mantienen. La opción solo
aparece para medicamentos con un esquema y ya viene marcada si el
medicamento tiene un horario si es necesario.

<a id="correct"></a>
### Corregir el stock

Si tienes menos de lo que muestra la aplicación (un comprimido perdido,
un frasco derramado): **Stock → Corregir stock…**, deja el tipo
**Corrección negativa** e introduce la cantidad que restar. El stock no
puede bajar de cero.

<a id="count"></a>
### Contar existencias

Cuando lo que hay en tu botiquín no coincide con la aplicación, cuenta y
deja que la aplicación corrija:

1. Selecciona el medicamento y luego **Stock → Contar existencias…**.
2. Escribe la **Cantidad contada**. La ventana muestra el stock
   esperado, la diferencia y cómo cambia la fecha de agotamiento.
3. En **Ya tomado hoy**, indica lo que ya habías tomado hoy cuando
   contaste (la aplicación propone las dosis cuya hora ya pasó).
4. Haz clic en **Registrar recuento**.

La aplicación registra una corrección para que el stock sea igual a lo
que contaste. La diferencia es solo un dato de stock: no se interpreta
como dosis olvidadas o de más.

<a id="history"></a>
### Historial y registros erróneos

**Stock → Historial…** (Ctrl+H) lista cajas, correcciones, tomas,
recuentos y suspensiones del medicamento seleccionado, del más reciente
al más antiguo. Selecciona un registro erróneo y haz clic en
**Eliminar**: el stock y el consumo se recalculan. Solo se pueden
eliminar los registros posteriores al último recuento (para corregir
los anteriores, vuelve a contar); los registros de versiones anteriores
a esta función no se pueden eliminar, corrígelos con una corrección.

---

<a id="documents"></a>
## 5. Cronología, ficha de terapia y solicitud de receta

### Cronología de la terapia

**Terapia → Cronología de la terapia…** (Ctrl+T) muestra una fila por
medicamento sobre un calendario (60 días atrás, 120 adelante):

- **barra sólida**: terapia en curso; **barra rayada**: suspensión;
- **rombo relleno**: empieza una nueva dosis o régimen; **rombo
  hueco**: siguiente etapa de una reducción por etapas;
- **triángulo**: fecha estimada de agotamiento; **línea discontinua**:
  hoy;
- fila gris: medicamento desactivado.

**Antes** / **Después** mueven 30 días, **Hoy** vuelve al presente; las
flechas del teclado mueven una semana. El recuadro de **detalles**
describe con palabras el medicamento seleccionado. **Mostrar en la
lista** (o Intro) lo selecciona en la lista principal. La cronología no
cambia nada; las fechas de agotamiento son estimaciones.

### Ficha de terapia (impresión y PDF)

**Terapia → Ficha de terapia…** (Ctrl+P) prepara una ficha de los
medicamentos activos para un médico, urgencias o un farmacéutico:
principio activo, posología, periodo de terapia, médico.

- **Incluir las notas** está desactivado por defecto: las notas pueden
  ser privadas.
- **Papel**: A4 o Letter.
- **Imprimir…** muestra una vista previa; **Guardar como PDF…** usa la
  impresora de Windows "Microsoft Print to PDF" (si se eliminó, la
  ventana explica cómo añadirla); **Guardar en archivo…** y **Copiar al
  portapapeles** dan el texto simple.

MedReminder no guarda ninguna copia de lo que guardas o imprimes.

### Planificar el stock (viaje o farmacia)

**Terapia → Planificar stock…** responde a la pregunta «¿tengo
suficiente hasta…?». Elige el periodo con **Desde** y **Hasta**, por
ejemplo los días de un viaje o los días hasta tu próxima visita a la
farmacia (por defecto: los próximos 14 días, hoy incluido). Para cada
medicamento activo la ventana muestra:

- **Necesario en el periodo**: la cantidad que se consume en el periodo,
  según la pauta, las suspensiones, la fecha de fin de la terapia y los
  horarios de toma;
- **Stock al inicio**: el stock de hoy menos el consumo previsto hasta
  el inicio del periodo (*se agota antes* si no quedará nada);
- **Falta**: lo que se necesita además de ese stock, o *cubierto*;
- **Cajas a conseguir**: cuántas cajas cubren lo que falta, del tamaño
  de la última caja nueva registrada (— si no se registró ninguna).

Los medicamentos no cubiertos aparecen primero. Los medicamentos a
demanda se listan pero no se calculan, porque su consumo no está
planificado. **Imprimir…**, **Guardar como PDF…** y **Copiar al
portapapeles** funcionan como en la ficha de terapia. La ventana no
modifica nada: las cifras son estimaciones.

### Solicitar una receta

Selecciona un medicamento y luego **Terapia → Solicitar receta…**.
MedReminder prepara un mensaje breve con el nombre del medicamento, la
presentación, el código del producto y tu nombre; con un médico de
referencia, el saludo usa su nombre. No se incluyen posología ni notas.
Puedes modificarlo todo antes de enviarlo.

- **Copiar** — para pegarlo en un webmail, una aplicación de mensajería
  o un portal del paciente.
- **Abrir en el programa de correo** — un correo nuevo en tu programa de
  correo habitual.
- **Enviar…** — lo envía con la cuenta de correo de MedReminder, tras
  una confirmación. Disponible cuando el correo está configurado y el
  **E-mail del médico** está rellenado (Configuración →
  Notificaciones). Con una instalación compartida solo envía el
  [dispositivo principal](#master): en los demás dispositivos usa
  **Abrir en el programa de correo**.

MedReminder nunca envía una solicitud por sí solo.

### Seguir una receta hasta la farmacia

**Terapia → Recetas…** lista las recetas registradas, primero las que
están por retirar. Para cada una puedes anotar, cuando los conozcas:

- **Solicitada el**: cuándo se la pediste al médico. **Marcar como
  solicitada** en la ventana de solicitud la registra por ti con la
  fecha de hoy;
- **Emitida el**, **Código de la receta** y **Cajas**: de la receta que
  emitió el médico;
- **Válida hasta**: el último día en que la farmacia la acepta. Se
  rellena para 30 días desde la fecha de emisión, la validez habitual
  de la receta electrónica italiana; compruébala en tu receta y
  corrígela si es distinta;
- **Retirada el**: cuándo la llevaste a la farmacia. **Retirada hoy**
  lo hace con un clic. Cuando añades una caja nueva de un medicamento
  con una receta aún por retirar, MedReminder pregunta si la caja viene
  de ella.

Una receta emitida y no retirada está *Por retirar*; tras su último día
de validez está *Caducada*. Desde 3 días antes de ese día recibes un
recordatorio, una vez, por los canales de notificación del medicamento
(el correo solo desde el [dispositivo principal](#master) si la
instalación es compartida). El recordatorio no incluye el código.

Las recetas se copian a los otros PC de un perfil sincronizado y se
incluyen en la exportación cifrada.

### Vencimientos administrativos

**Terapia → Vencimientos administrativos…** reúne las fechas que no
tienen que ver con el stock: la renovación de un plan terapéutico o de
una exención, un control periódico o cualquier otra cosa que
describas. Para cada vencimiento:

- **Tipo** y **Descripción**: la descripción es opcional, salvo para el
  tipo *Otro*;
- **Medicamento**: el medicamento al que se refiere, o *(ninguno)* para
  un vencimiento de todo el perfil;
- **Fecha** y **Avisar días antes**: el recordatorio empieza ese número
  de días antes de la fecha (14 por defecto);
- **Repetir cada … meses**: para un vencimiento que vuelve, como una
  renovación anual;
- **Avisar por**: notificación de Windows y/o correo electrónico.

MedReminder no aplica reglas propias a estas fechas: los plazos de validez
cambian según el plan y la región, así que introduce la fecha que
figura en tus documentos.

Desde la antelación fijada recibes un recordatorio por fecha, por los
canales elegidos (el correo solo desde el [dispositivo maestro](#master)
cuando la instalación es compartida); un vencimiento pasado se muestra
en rojo. **Hecho** cierra un vencimiento único; uno recurrente pasa a
su fecha siguiente, contada desde la fecha anterior y no desde el día
en que lo marcaste.

Los vencimientos se copian en los demás PC de un perfil sincronizado y
se incluyen en la exportación cifrada.

### Exportar las fechas a un calendario

**Terapia → Exportar al calendario…** guarda un archivo `.ics` con las
próximas fechas: para cada medicamento activo el día en que solicitar
la receta (la fecha de agotamiento menos el umbral de aviso) y la fecha
de agotamiento, el último día para recoger cada receta y los
vencimientos administrativos abiertos. Abre el archivo con Outlook,
Google Calendar o el calendario del teléfono. Los eventos son
recordatorios, no citas: no te marcan como ocupado. Al exportar de
nuevo más adelante se actualizan los mismos eventos, sin copias.

Los calendarios suelen guardarse en línea en otra empresa, así que los
eventos solo dicen qué hacer ("MedReminder: se agota un
medicamento"). Marca **Incluir los nombres de los medicamentos y las
descripciones de los vencimientos** si quieres los nombres en el
calendario; la opción se pregunta en cada exportación.

Cada correo de stock bajo lleva también la fecha de agotamiento como
archivo de calendario (`medreminder.ics`), con el mismo título
genérico.

---

<a id="notifications"></a>
## 6. Notificaciones y correo

### Cómo funcionan los avisos

- Cada 30 minutos MedReminder revisa los medicamentos. Cuando un
  medicamento baja de su **umbral de aviso**, te avisa **una vez**, por
  los canales elegidos para ese medicamento: una notificación de
  Windows y/o un correo.
- Si cuando los días restantes llegan a **la mitad del umbral** no se ha
  añadido una caja nueva, sigue un **segundo aviso** por los mismos
  canales (con un umbral de 10 días: primer aviso a los 10 días, segundo
  a los 5). Un medicamento que ya está por debajo de la mitad en la
  primera revisión recibe solo el segundo aviso. Tras una caja nueva, el
  ciclo vuelve a empezar.
- **Desde la notificación de Windows**: un clic abre MedReminder en ese
  medicamento (en las recetas, para un recordatorio de receta; en los vencimientos, para un recordatorio de vencimiento). Un
  aviso de stock tiene **Preparar la solicitud**, que abre la solicitud
  al médico; un recordatorio de dosis tiene **Recuérdamelo en 15
  minutos**, que lo repite más tarde, aunque MedReminder se haya cerrado
  entretanto. Las tomas no se registran desde la notificación: usa
  **Terapia → Registrar toma…**.
- **Herramientas → Comprobar ahora** (**Ctrl+R**, o el menú del icono)
  ejecuta la comprobación enseguida.
- MedReminder tiene que estar en marcha para enviar avisos. Activa el
  inicio automático (ver [Configuración](#settings)).

### Paso 1 — la cuenta de correo (administrador)

**Herramientas → Configuración… → E-mail SMTP**:

| Campo | Qué introducir |
|---|---|
| **Servidor** | El servidor de salida de tu proveedor, por ejemplo `smtp.gmail.com` |
| **Puerto** | Normalmente `587` (con *Usar StartTLS*) o `465` |
| **Nombre de usuario** / **Nueva contraseña** | Tu cuenta de correo. La contraseña se guarda cifrada y nunca aparece en los registros |
| **Remitente (from)** / **Nombre del remitente** | De quién llegan los correos |
| **Timeout (s)** | Segundos antes de desistir |

Haz clic en **Probar conexión** (inicia sesión sin enviar nada) y luego
en **Guardar configuración SMTP**.

*Ejemplo con Gmail:* activa la verificación en dos pasos en tu cuenta de
Google, crea una contraseña de aplicación en
`myaccount.google.com/apppasswords` y usa Servidor `smtp.gmail.com`,
Puerto `587`, StartTLS activado, tu dirección de Gmail como nombre de
usuario y la contraseña de aplicación como contraseña. Los proveedores
cambian sus reglas: si la prueba falla, consulta las instrucciones de
tu proveedor.

### Paso 2 — los destinatarios (cada perfil)

**Herramientas → Configuración… → Notificaciones**, para el perfil
abierto:

- **Destinatario (to)** — quién recibe los avisos de este perfil.
- **E-mail del cuidador (opcional)** — un familiar o cuidador que recibe
  una copia de los avisos, en el mismo correo (ambas direcciones son
  visibles para los dos). Debe ser distinta del destinatario. En
  **Copia al cuidador** eliges qué avisos recibe (todos mientras no lo
  cambies): stock bajo, recordatorios de dosis, de recetas y de
  vencimientos, avisos de desabastecimiento. **Enviar al cuidador un
  resumen semanal del stock** añade, cada 7 días, un correo solo al
  cuidador con el stock, el estado y la fecha de agotamiento de cada
  medicamento activo, y nada sobre las dosis tomadas. Lo envía el PC
  que manda los correos, una vez por perfil aunque el perfil esté
  sincronizado en varios PC.
- **E-mail del médico (opcional)** — se usa solo para las solicitudes de
  receta que envías tú; los avisos automáticos nunca van ahí.

Haz clic en **Guardar destinatarios**. En la misma sección, **Mi PIN**
permite establecer o cambiar el PIN de tu propio perfil.

---

<a id="profiles"></a>
## 7. Varias personas: perfiles y roles

Un solo MedReminder puede seguir los medicamentos de varias personas,
por ejemplo tú y uno de tus padres. Cada persona tiene un **perfil** con
sus medicamentos y sus destinatarios.

### Roles

| | Administrador | Usuario |
|---|---|---|
| Sus medicamentos, stock, destinatarios | sí | sí |
| Cuenta de correo, copia de seguridad, país de referencia | sí | no |
| Crear, renombrar, eliminar perfiles; PIN de todos | sí | no |
| Herramientas → Sincronización… y Herramientas → Instalación… | sí | no |

Siempre hay al menos un administrador.

### Gestionar perfiles (administrador)

**Herramientas → Gestionar perfiles…**:

- **Nuevo perfil** — nombre, rol (por defecto *Usuario*), PIN opcional.
- **Renombrar** — cambia el nombre mostrado.
- **Cambiar PIN** — establece, cambia o borra el PIN de un perfil.
- **Cambiar rol…** — convierte un perfil en administrador o usuario. El
  rol del perfil abierto no se puede cambiar (abre antes otro perfil
  administrador). Antes de hacer administrador a un perfil sin PIN,
  plantéate añadirle un PIN.
- **Eliminar** — escribe el nombre del perfil para confirmar. Los datos
  en disco se conservan salvo que marques *Eliminar también los datos
  del perfil del disco*. No se pueden eliminar el perfil abierto ni el
  último administrador.

### Cambiar de perfil

**Archivo → Cambiar perfil…**, elige el perfil y confirma. MedReminder
se reinicia con ese perfil (y pide su PIN, si lo tiene). Al iniciar
Windows se abre el último perfil usado.

### Sobre el PIN

El PIN evita abrir por error el perfil equivocado. **No** es una
protección: no cifra nada, y cualquiera que use la misma cuenta de
Windows puede leer los archivos de todos los perfiles. Tres intentos
fallidos cierran la aplicación. Para una privacidad real, da a cada
persona su propia cuenta de Windows. Si se olvida un PIN, un
administrador lo borra con **Cambiar PIN**; si lo olvidó el único
administrador, ver [Problemas y respuestas](#faq).

---

<a id="backup"></a>
## 8. Proteger tus datos: copia de seguridad y exportación

| Opción | Para qué sirve | Dónde |
|---|---|---|
| **Copia de seguridad automática diaria** | Una copia de todos los perfiles, cada día, en una carpeta tuya | Configuración → Copia de seguridad / Restaurar |
| **Exportación cifrada** | Un único archivo portátil, para pasar a un PC nuevo o guardarlo | Configuración → Copia de seguridad / Restaurar → Exportar todos los datos (cifrados)… |
| **Copia en la nube** | Una copia cifrada diaria en OneDrive, Google Drive o una carpeta sincronizada | Configuración → Copia de seguridad / Restaurar → Copia de seguridad a carpeta sincronizada |
| **Sincronización / Instalación** | Varios PC trabajando continuamente sobre los mismos datos | [Varios ordenadores](#devices) |

La configuración de copia de seguridad la gestiona un administrador.

### Copia de seguridad automática diaria

1. **Herramientas → Configuración… → Copia de seguridad / Restaurar**.
2. Marca **Copia de seguridad automática diaria**, elige la **Carpeta de
   copia de seguridad** (mejor un disco externo), la **Hora preferida**
   y la **Retención (días)**.
3. **Guardar configuración de copia**. **Ejecutar copia ahora** hace una
   enseguida.

Se guarda cada perfil, como `medreminder-<perfil>-<fecha>-<hora>.db`.
MedReminder tiene que estar en marcha a la hora elegida; si el PC
estaba apagado, la copia se hace en el siguiente inicio. No elijas una
carpeta sincronizada en la nube para esta copia: los archivos no están
cifrados (MedReminder te avisa).

- **Exportar a carpeta específica…** — una copia ahora, donde quieras.
- **Restaurar copia…** — elige un archivo `.db` y el perfil que lo
  recibe. Los datos actuales se apartan como
  `medreminder.db.bak-<fecha>`. Si restauras en el perfil abierto,
  MedReminder se reinicia.

### Exportación e importación cifradas

Una exportación es **un único archivo cifrado** (`.mrz`) con todos los
datos de un perfil. No está ligado a tu PC: es la forma recomendada de
pasar a un ordenador nuevo.

**Exportar** — **Configuración → Copia de seguridad / Restaurar →
Exportar todos los datos (cifrados)…**:

1. Elige el archivo de destino.
2. Elige una **frase de contraseña** de al menos 12 caracteres y
   escríbela dos veces.
3. Si quieres, incluye la contraseña SMTP, las preferencias de copia y
   las preferencias de usuario (idioma, país del catálogo). La
   contraseña SMTP se cifra con tu frase de contraseña.
4. **Exportar**.

Un administrador con varios perfiles puede marcar **Exportar todos los
perfiles (un archivo cifrado por perfil)** y elegir una carpeta.

> **La frase de contraseña no se puede recuperar.** Sin ella, el
> archivo no se podrá volver a leer. Anótala en un lugar seguro.

**Importar** — **Configuración → Copia de seguridad / Restaurar →
Importar desde una exportación…**: elige el archivo (MedReminder muestra
lo que contiene), escribe la frase de contraseña, marca *Entiendo que
esto sobrescribirá los datos del perfil actual*, haz clic en
**Importar** y reinicia cuando se te pida. La importación **sustituye**
los datos del perfil abierto; se conserva una copia de seguridad. Una
frase incorrecta, un archivo dañado o un archivo de una versión más
reciente detienen la importación sin tocar tus datos. El formato es
público (`docs/EXPORT-FORMAT.md`): tus datos nunca quedan atrapados.

### Copia en la nube

Una copia cifrada de todos los perfiles, una vez al día, en la nube.

1. **Configuración → Copia de seguridad / Restaurar → Copia de seguridad
   a carpeta sincronizada (cifrada)**: marca la casilla.
2. **Almacenamiento**:
   - **OneDrive (carpeta de aplicación)** o **Google Drive (carpeta
     MedReminder/backups)** — haz clic en **Iniciar sesión…** y entra con
     tu cuenta;
   - **Carpeta** — una carpeta ya sincronizada por OneDrive, Dropbox,
     iCloud o Google Drive en este PC.
3. **Instantáneas que conservar** (30 por defecto).
4. **Frase de contraseña de copia de seguridad → Establecer /
   cambiar…**: al menos 12 caracteres. Se queda en este PC y nunca se
   envía.
5. Guarda.

**Restaurar** — **Configuración → Copia de seguridad / Restaurar →
Restaurar desde carpeta en la nube…**: elige la carpeta o la cuenta,
elige una copia (fecha, perfil, dispositivo), escribe la frase de
contraseña, marca la confirmación y haz clic en **Restaurar**. La copia
sustituye el perfil **abierto**: para restaurar otro perfil, ábrelo
antes.

Conviene saber:

- Perder la frase de contraseña de copia es perder las copias.
- Las copias no contienen la contraseña SMTP ni las preferencias: para
  eso usa la exportación cifrada.
- Quien conoce la frase de contraseña puede leer la copia de cada
  perfil, incluidos los protegidos con PIN.
- Tu proveedor de nube puede guardar los archivos eliminados en su
  papelera.
- Es una **copia de seguridad, no una sincronización**: para trabajar en
  varios PC usa la [sincronización](#sync).
- Con una instalación compartida, la copia en la nube la hace solo el
  [dispositivo principal](#master).

---

<a id="devices"></a>
## 9. Varios ordenadores

<a id="devices-choice"></a>
### ¿Qué opción necesito?

| Situación | Usa |
|---|---|
| Un solo PC | Nada que hacer. Ten una [copia de seguridad](#backup). |
| Pasar una vez a un PC nuevo | [Exportación cifrada](#backup) en el PC antiguo, importación en el nuevo. |
| El **mismo perfil** en dos o más PC, siempre al día | [Sincronización](#sync) (Herramientas → Sincronización…). |
| **Toda la configuración familiar** (perfiles, roles, PIN, correo, copias) en varios PC, con **un solo** PC que envía los correos | [Instalación](#installation) (Herramientas → Instalación…), además de la sincronización. |

Ambas opciones necesitan un almacenamiento al que lleguen todos los PC:
**OneDrive**, **Google Drive** o una **carpeta compartida** (una carpeta
sincronizada por Dropbox o similar, o un recurso de red). Los datos ahí
están siempre cifrados. No interviene ningún servidor de MedReminder.

Todos los PC de un grupo deben tener la misma versión de MedReminder:
actualízalos juntos.

<a id="sync"></a>
### Sincronizar un perfil entre PC

La sincronización mantiene **un perfil** idéntico en varios PC:
medicamentos, stock, tomas, nombre del perfil y destinatarios. Lo que
registras en un PC aparece en los demás en pocos minutos. Se configura
**para cada perfil**, con el perfil abierto, por un administrador.

**En el primer PC**

1. Abre el perfil y luego **Herramientas → Sincronización… → Activar
   sincronización…**.
2. Elige dónde vive el grupo:
   - **OneDrive** o **Google Drive**: inicia sesión en la ventana del
     navegador. Todos los PC deben usar la **misma** cuenta. MedReminder
     solo usa su propia carpeta de aplicación;
   - **una carpeta compartida**: elígela.
3. Escribe un nombre para este PC y una **frase de contraseña de
   sincronización** (al menos 10 caracteres, dos veces). No es la de la
   copia de seguridad. Guárdala bien: no se puede recuperar.

**En cada uno de los demás PC**

1. Crea un perfil (con cualquier nombre: se sustituirá), o abre el que
   quieras sustituir.
2. **Herramientas → Sincronización… → Unirse a un grupo…**, elige el
   mismo almacenamiento, escribe un nombre para este PC y la misma frase
   de contraseña. En lugar de la frase puedes usar **Unirse con un
   código de vinculación…** (ver abajo).
3. Confirma: **los datos de este perfil en este PC se sustituyen** por
   los del grupo (se guarda una copia). MedReminder se reinicia.

Con una carpeta compartida, espera antes a que esté totalmente
descargada en el PC nuevo. Con OneDrive o Google Drive la unión puede
tardar un minuto. Si la frase abre varios grupos (varios perfiles
sincronizados con la misma frase), MedReminder pregunta a cuál unirse.

**Código de vinculación en lugar de la frase.** En un PC que ya está en
el grupo, **Herramientas → Sincronización… → Vincular un dispositivo…**
y haz clic en **Mostrar el código** cuando el otro PC esté listo. En el
otro PC, **Unirse con un código de vinculación…** y escribe el código.
El código vale 10 minutos y solo mientras su ventana está abierta.
Quien lo vea puede leer los datos: no lo envíes nunca por correo o
mensaje, y muéstralo solo cuando haga falta (las herramientas de
asistencia remota también lo ven).

**Uso diario**

- La sincronización se hace unos segundos después de cada cambio, cada
  5 minutos y con **Sincronizar ahora**. La sección **Dispositivos**
  muestra los PC y cuándo se vio cada uno por última vez.
- Si dos PC cambiaron lo mismo antes de sincronizarse, gana el cambio
  más reciente y el caso aparece en **Conflictos**: **Restaurar valor
  perdido** recupera el otro valor, **Descartar** quita la entrada.
- Un correo de stock bajo se envía **una vez por grupo**, no una por PC.
  (Dos PC que comprueban antes de haberse sincronizado pueden enviarlo
  los dos; un [dispositivo principal](#master) elimina ese caso.)
- Importar una exportación o restaurar una copia en un perfil
  sincronizado inicia una nueva **generación**: los demás PC reciben un
  aviso y deben usar **Reconstruir desde el grupo…**.
- **Desactivar sincronización…** detiene la sincronización en este PC y
  conserva sus datos.
- Si la sesión de OneDrive o Google Drive caduca (cambio de contraseña,
  larga inactividad), haz clic en **Volver a iniciar sesión en
  OneDrive** / **Volver a iniciar sesión en Google Drive**; no se pierde
  nada.

**Un PC se ha perdido o la frase se ha filtrado.** En la sección
**Dispositivos** selecciona el PC y haz clic en **Quitar
dispositivo…**, o usa **Cambiar clave y frase de contraseña…**. Elige
una nueva frase de sincronización: el PC quitado no podrá leer nada de
lo que se escriba desde entonces. Cierra también la sesión de ese PC en
la configuración de seguridad de tu cuenta de Microsoft o Google. En
cada uno de los demás PC haz clic en **Introducir la nueva clave…** y
escribe la nueva frase o un código de vinculación: sus cambios se
conservan y MedReminder se reinicia.

<a id="installation"></a>
### Compartir la instalación

La sincronización funciona perfil por perfil. La **instalación** añade
todo lo que rodea a los perfiles, para que cada PC esté configurado
igual:

| Compartido por todos los dispositivos | Propio de cada dispositivo |
|---|---|
| Perfiles: nombres, roles, PIN | Qué perfiles contiene el dispositivo |
| Cuenta de correo (SMTP, contraseña incluida) | Idioma de la interfaz, tamaño del texto |
| Reglas de la copia en la nube (almacenamiento, número de copias) | Frase de contraseña e inicio de sesión de la copia en la nube |
| País de referencia | Copia de seguridad automática local |

Cada dispositivo contiene **solo los perfiles que un administrador le
asigna**: el PC de un abuelo puede contener solo su perfil, mientras que
el PC familiar los contiene todos.

**Antes de empezar**

- Un perfil administrador, en el PC que será el principal.
- **La sincronización activada en cada perfil** que quieras compartir
  (ver [Sincronización](#sync)); un perfil sin sincronización no se
  puede asignar a otro dispositivo.

**Paso 1 — Publicar (en el PC principal)**

1. **Herramientas → Instalación… → Publicar la instalación…**.
2. Elige el **mismo almacenamiento** que contiene los grupos de
   sincronización de los perfiles.
3. Elige una **frase de contraseña de la instalación**. Permite a un
   administrador añadir un dispositivo y recuperar todos los perfiles
   cuando no hay ningún otro dispositivo a mano. Resérvala para los
   administradores; no se puede recuperar.

Este PC pasa a ser el [dispositivo principal](#master).

**Paso 2 — Añadir un dispositivo**

1. En el PC principal: **Herramientas → Instalación… → Dispositivos →
   Añadir un dispositivo…**, marca los perfiles para el nuevo
   dispositivo y luego **Mostrar el código**.
2. En el PC nuevo:
   - si MedReminder nunca se ha usado ahí: en la ventana de bienvenida
     elige **Unirse a una instalación existente…**;
   - si no: **Herramientas → Instalación… → Unirse a una instalación
     existente…**.
3. Elige **Con un código** y escribe el código. El código dura 10
   minutos, o hasta que se cierra su ventana.
4. La ventana lista cada perfil ("añadido a este dispositivo", "ya está
   en este dispositivo", …). MedReminder se reinicia con los perfiles
   nuevos y la configuración de la instalación. Los perfiles que ya
   había en el PC nuevo se añaden a la instalación.

*¿No hay otro dispositivo a mano?* Elige **Con la frase de contraseña de
la instalación**, selecciona el almacenamiento y escribe la frase. Un
administrador elige luego su perfil, escribe su PIN y selecciona los
perfiles para este dispositivo.

**Uso diario**

- La instalación se sincroniza sola cada 15 minutos.
- Un cambio de perfil, rol, PIN, cuenta de correo, reglas de la copia en
  la nube o país de referencia hecho en un dispositivo llega a los
  demás.
- Un perfil creado más tarde: activa su sincronización (Herramientas →
  Sincronización…) y luego asígnalo a otros dispositivos con **Añadir un
  dispositivo…** desde un dispositivo que lo contenga.
- **Herramientas → Instalación… → Estado** muestra el almacenamiento, el
  dispositivo principal y las acciones pendientes.

<a id="master"></a>
### El dispositivo principal

En una instalación compartida **un solo dispositivo, el principal**,
envía todos los correos (avisos de stock bajo y recordatorios de dosis,
de todos los perfiles que contiene, también los que no están abiertos)
y hace la copia en la nube. Los demás dispositivos muestran sus avisos
solo en pantalla. Así cada correo llega una sola vez.

- El dispositivo que publica la instalación es el principal. La sección
  **Dispositivos** lo indica en la columna **Función**.
- Elige como principal un dispositivo **que esté encendido a menudo** y
  con MedReminder en marcha.
- En los demás dispositivos las solicitudes de receta se abren en el
  programa de correo, y **Probar conexión** solo funciona en el
  principal. La configuración de correo se puede editar en todos y llega
  a todos los dispositivos.
- Un principal que no sincroniza la instalación desde hace **24 horas**
  deja de enviar correos hasta que vuelva a sincronizar.
- Las instalaciones publicadas antes de esta versión no tienen principal
  hasta que un administrador elige uno; mientras tanto, envía cada
  dispositivo.

**Pasar el principal a otro dispositivo**

1. **Herramientas → Instalación… → Dispositivos**, selecciona el nuevo
   dispositivo, **Hacer principal…**, confirma. Ningún dispositivo envía
   correo hasta que se completa el relevo.
2. En el nuevo dispositivo, con un perfil administrador abierto, la
   ventana **Relevo del dispositivo principal** se abre sola (o más tarde
   desde **Herramientas → Instalación… → Completar el relevo…**).
   Muestra la configuración y te pide:
   - **Probar la conexión de correo desde este dispositivo**;
   - **iniciar sesión** en el almacenamiento de la copia en la nube con
     la misma cuenta;
   - volver a escribir la **frase de contraseña de la copia en la nube**
     (nunca se copia entre dispositivos);
   - opcionalmente escribir la frase de la instalación, para traer los
     perfiles que ningún dispositivo a mano contiene.
3. **Confirmar**. El nuevo dispositivo toma el relevo cuando el antiguo
   principal cede el rol en su siguiente sincronización. Los perfiles
   nuevos aparecen en el siguiente inicio.

**El principal está averiado o perdido.** Haz lo mismo desde otro
dispositivo: el nuevo principal toma el relevo solo cuando el antiguo
lleva 25 horas sin aparecer. Después quita el antiguo (abajo).

<a id="remove-device"></a>
### Dispositivo perdido o sustituido

Cuando un dispositivo se pierde, se vende o se regala:

1. En el **principal** (contiene todos los perfiles): **Herramientas →
   Instalación… → Dispositivos**, selecciona el dispositivo, **Quitar
   dispositivo…**.
2. Elige una **nueva frase de contraseña de la instalación**. El
   dispositivo quitado conserva lo que ya tiene pero no recibe nada
   nuevo; los perfiles que contenía también reciben claves nuevas.
3. MedReminder ofrece mostrar un código. Los demás dispositivos dejan de
   sincronizarse hasta recibir la nueva clave: en cada uno, un
   administrador abre **Herramientas → Instalación… → Introducir la
   nueva clave…** y escribe la nueva frase o el código. Los cambios
   hechos ahí mientras tanto se conservan.
4. Las nuevas claves de los perfiles llegan solas a los demás
   dispositivos; un perfil abierto en ese momento pide reiniciar
   MedReminder.

Quitar el principal desde otro dispositivo convierte a ese dispositivo
en el principal. Cierra también la sesión del dispositivo perdido en tu
cuenta de Microsoft o Google.

---

<a id="settings"></a>
## 10. Configuración y uso diario

Todo en **Herramientas → Configuración…**. Las secciones aparecen a la
izquierda; **Ctrl+Tab** pasa a la siguiente. La ventana se puede
redimensionar.

- **General → Idioma de la interfaz**: inglés, italiano, francés,
  español o alemán. Los correos y la ficha de terapia también lo usan.
  MedReminder se reinicia.
- **General → Tamaño del texto (este perfil)**: Normal, Grande, Muy
  grande, para cada perfil. MedReminder sigue también el escalado y los
  temas de contraste de Windows. En una pantalla pequeña, mejor Grande.
- **General → Apariencia (este perfil)**: Como Windows, Claro u Oscuro,
  para cada perfil en este equipo. «Como Windows» solo es oscuro en
  Windows 11 con el modo oscuro activado; con un tema de contraste alto
  de Windows se usan sus colores. Se aplica tras reiniciar. En Oscuro,
  los campos de fecha siguen claros.
- **General → Buscar actualizaciones automáticamente (GitHub)**: busca
  una versión nueva al iniciar (nada se instala solo) y actualiza el
  catálogo al iniciar y una vez al día. **? →
  Buscar actualizaciones…** busca ahora.
- **General → Registrar las consultas de la base de datos
  (diagnóstico)**: solo administradores. Escribe en el archivo de
  registro cada comando de la base de datos, sin los valores, para
  diagnosticar problemas. Se aplica en seguida; el registro crece
  rápido, así que desactívala después.
- **Inicio → Iniciar MedReminder al iniciar sesión en Windows**: arranca
  oculto en el área de notificación. No necesita derechos de
  administrador.

**Icono del área de notificación.** El doble clic abre la ventana; el
clic derecho ofrece *Abrir MedReminder*, *Comprobar ahora*,
*Configuración…*, *Salir*.

**Apoyar el desarrollo.** Si está activado, **? → Apoyar el
desarrollo…** abre en el navegador una página de contribución
voluntaria (Stripe o PayPal). MedReminder nunca ve tus datos de pago.

---

<a id="faq"></a>
## 11. Problemas y respuestas

**No recibo correos.**
Comprueba, por orden: *Probar conexión* en Configuración → E-mail SMTP;
el *Destinatario* en Configuración → Notificaciones; el canal
**E-mail** marcado en el medicamento; MedReminder en marcha. Con una
instalación compartida solo envía el principal: mira **Herramientas →
Instalación… → Estado**.

**"Este dispositivo es el principal pero no sincroniza la instalación
desde hace más de 24 horas".**
El principal no llega al almacenamiento. Comprueba la conexión a
internet y la sesión de OneDrive / Google Drive, y luego **Sincronizar
ahora**.

**"La clave de la instalación se cambió en otro dispositivo".**
Se quitó un dispositivo. Abre **Herramientas → Instalación… →
Introducir la nueva clave…** y escribe la nueva frase, o un código
mostrado por un dispositivo que ya la tiene.

**"Se quitó un dispositivo … este perfil tiene una nueva clave.
¿Reiniciar ahora?"**
Responde sí: el perfil toma su nueva clave al reiniciar MedReminder.

**"Este dispositivo se quitó de la instalación".**
El dispositivo conserva sus datos pero ya no recibe nada. Para volver a
usarlo, un administrador lo añade como dispositivo nuevo.

**El relevo del principal no termina.**
El antiguo principal cede el rol en su siguiente sincronización. Si está
apagado para siempre, el nuevo principal toma el relevo 25 horas después
de la última vez que se vio al antiguo.

**"El grupo de sincronización de este perfil pertenece a otra
instalación".**
El perfil se publicó desde otra instalación. Únete a esa instalación
(**Unirse a una instalación existente…**).

**He olvidado un PIN.**
Un administrador lo borra con **Herramientas → Gestionar perfiles… →
Cambiar PIN**. Si nadie más puede: cierra MedReminder, abre
`%LOCALAPPDATA%\MedReminder\profiles.json` con el Bloc de notas y, para
ese perfil, borra los valores de `PinHash` y `PinSalt` y pon
`PinIterations` a `0`. MedReminder registra el cambio en el siguiente
inicio; con una instalación compartida llega a los demás dispositivos.

**He olvidado una frase de contraseña.**
Las de exportación, copia de seguridad, sincronización e instalación no
se pueden recuperar. Puedes establecer otras nuevas (nueva exportación,
nueva frase de copia en la nube, *Cambiar clave y frase de
contraseña…*), pero los archivos cifrados con la antigua siguen siendo
ilegibles.

**"Ya se está ejecutando".**
MedReminder ya está abierto: busca su icono en el área de notificación.

**Los datos parecen dañados.**
Restaura una copia (Configuración → Copia de seguridad / Restaurar →
Restaurar copia…) o una exportación. Los archivos de registro (abajo)
ayudan a entender qué ocurrió.

---

<a id="data"></a>
## 12. Dónde guarda MedReminder los datos

Todo está en `%LOCALAPPDATA%\MedReminder\` (pégalo en la barra de
direcciones del Explorador de archivos). MedReminder no escribe en
ningún otro sitio, salvo los archivos de copia y exportación que colocas
tú.

```
%LOCALAPPDATA%\MedReminder\
├── profiles.json              lista de perfiles, roles, PIN (hash)
├── smtp.settings.json         cuenta de correo (sin contraseña)
├── smtp.protected             contraseña de correo, cifrada por Windows
├── backup.settings.json       configuración de copia de seguridad
├── cloud-backup.protected     frase de la copia en la nube, cifrada por Windows
├── user.settings.json         idioma, país de referencia, búsqueda de actualizaciones, registro de consultas
├── household\                 instalación compartida (solo si se usa)
├── logs\medreminder-AAAAMMDD.log
└── profiles\
    └── <perfil>\
        ├── medreminder.db     medicamentos y stock del perfil
        ├── notifications.settings.json   destinatarios
        ├── ui.settings.json   tamaño del texto, apariencia, tamaño de la ventana
        └── sync.*             configuración de sincronización (solo si se usa)
```

- Los **registros** anotan lo que hizo la aplicación (comprobaciones,
  correos enviados, errores). Nunca contienen contraseñas, texto de
  correos ni notas médicas.
- La base de datos no está cifrada: la protege tu cuenta de Windows. Las
  exportaciones y las copias en la nube están cifradas.
- **Actualización desde una versión muy antigua** (un solo
  `medreminder.db` directamente en la carpeta): el primer inicio lo
  mueve a un perfil llamado *User* y guarda una copia en
  `backups\pre-migration-…`, que puedes borrar cuando todo esté bien.

---

<a id="limits"></a>
## 13. Lo que MedReminder no hace

- No lleva la cuenta de las dosis tomadas ni avisa de dosis olvidadas
  (el recordatorio a la hora de la dosis es solo un aviso).
- No da indicaciones terapéuticas ni comprueba dosis o interacciones
  entre medicamentos.
- No pide medicamentos ni contacta con tu médico por sí solo.
- No combina los datos restaurados de una copia: restaurar e importar
  siempre sustituyen.

Su finalidad es avisarte a tiempo de que necesitas una receta nueva.
