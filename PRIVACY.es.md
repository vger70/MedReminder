# MedReminder — Política de privacidad

Última actualización: 3 de noviembre de 2026

Traducción de [PRIVACY.md](PRIVACY.md). En caso de discrepancia, prevalece la versión inglesa.

Esta política describe cómo MedReminder trata los datos personales en Windows (ordenador), Android e iOS (iPhone). Se aplica a las versiones y funciones disponibles en cada plataforma; es posible que algunas funciones aún no estén disponibles en todas las versiones. La aplicación de Windows se distribuye en paquetes ZIP, instaladores MSI y mediante Microsoft Store. Android se distribuye a través de Google Play, incluidos sus canales de prueba. iOS se distribuye a través de la App Store de Apple, incluido TestFlight.

## 1. En resumen

- MedReminder guarda sus datos en su dispositivo. El desarrollador no gestiona servidores ni recibe sus datos.
- No hay cuentas con el desarrollador, telemetría, estadísticas de uso, publicidad ni seguimiento.
- Los datos solo salen del dispositivo mediante funciones que usted utiliza o activa, como el correo electrónico, la exportación o el uso compartido, la copia de seguridad en la nube, la sincronización y la descarga de catálogos. Cada función envía datos únicamente al servicio o destino descrito a continuación. Los servicios específicos de cada plataforma, como las actualizaciones mediante Google Play y la App Store, se indican por separado.

## 2. Responsable

MedReminder es software libre y de código abierto (Apache License 2.0), desarrollado por vger70. Como el desarrollador no recoge ni recibe sus datos, usted mantiene el control: la aplicación los procesa en su dispositivo, por cuenta de usted.

Contacto: info@medreminder26.org, o una incidencia en https://github.com/vger70/MedReminder/issues (no publique datos de salud en una incidencia pública).

## 3. Datos guardados en su dispositivo

### Windows (ordenador)

MedReminder guarda los datos en `%LOCALAPPDATA%\MedReminder\`, dentro de su cuenta de Windows. Incluyen:

- perfiles: nombre, función, PIN opcional (guardado como hash);
- medicamentos, dosis, pautas, existencias, tomas, recetas, plazos administrativos y notas, una base de datos por perfil;
- configuración del correo: servidor SMTP, nombre de usuario y contraseña (cifrada con Windows DPAPI), direcciones del remitente y destinatarios, incluida la dirección de un médico o cuidador si la indica;
- tokens de acceso de OneDrive o Google Drive, si los conecta;
- configuración, copias de seguridad que usted configure y archivos de registro.

En Windows, las bases de datos de los perfiles no están cifradas: cualquier persona que pueda usar su cuenta de Windows puede leerlas. El PIN del perfil evita abrir por error el perfil equivocado; no es una protección. Para separar los datos, asigne a cada persona su propia cuenta de Windows.

Los archivos de registro de Windows no contienen contraseñas, contenido de correos ni notas médicas. Desinstalar la aplicación de Windows no elimina su carpeta de datos; elimínela aparte para borrar esos datos.

### Android

MedReminder guarda las bases de datos, la configuración y los registros en el almacenamiento privado de la aplicación en el teléfono. La copia de seguridad automática de los datos de la aplicación Android está desactivada, por lo que el sistema operativo no copia estos datos de salud a una cuenta de copia de seguridad de Android. La frase de contraseña de la copia se guarda mediante el almacenamiento seguro de claves de Android. El acceso a Google Drive lo gestionan los servicios de Google Play, que conservan la autorización y entregan a la aplicación tokens de acceso de corta duración. Los tokens de inicio de sesión de OneDrive los guarda la biblioteca de inicio de sesión de Microsoft en el almacenamiento privado de la aplicación, que otras aplicaciones no pueden leer y que queda excluido de la copia de seguridad de Android. Los registros de Android no contienen contraseñas, contenido de correos ni notas médicas.

Borrar los datos de MedReminder o desinstalar la aplicación elimina su copia local. Antes de hacerlo, use las funciones de exportación o copia de seguridad en la nube si desea conservar una copia.

### iOS (iPhone)

MedReminder guarda sus bases de datos y su configuración en el almacenamiento privado de la aplicación en el iPhone, que otras aplicaciones no pueden leer. Este almacenamiento está excluido de las copias de seguridad de iCloud y del ordenador, por lo que iOS no copia estos datos de salud en una copia de seguridad de iCloud ni en un ordenador, y una copia restaurada en otro iPhone no los contiene. Los secretos locales, como la frase de contraseña de la copia de seguridad, la contraseña del correo y los tokens de acceso a OneDrive y Google Drive, se cifran con una clave guardada en el llavero de iOS. Solo MedReminder en ese iPhone puede leer esta clave, una vez que el iPhone se ha desbloqueado tras encenderlo; la clave nunca se sincroniza con el llavero de iCloud. Usted inicia sesión en OneDrive o Google Drive en la página del proveedor, abierta por iOS; MedReminder nunca ve su contraseña de Microsoft o Google. La aplicación de iOS no escribe archivos de registro.

Eliminar MedReminder del iPhone borra su copia local. iOS puede conservar la clave del llavero de la aplicación tras eliminarla; solo MedReminder puede leerla y, sin los datos eliminados, no abre nada. Antes de eliminar la aplicación, use las funciones de exportación o copia de seguridad en la nube si desea conservar una copia.

Los recordatorios son notificaciones locales programadas en el iPhone; no se usa ningún servicio de notificaciones push. Según la configuración de notificaciones de iOS, su texto, por ejemplo el nombre de un medicamento, puede aparecer en la pantalla bloqueada y en un Apple Watch enlazado.

## 4. Datos que salen de su dispositivo

Estas funciones envían o comparten datos únicamente cuando están disponibles y se utilizan o activan:

| Función | Qué se envía | Destino |
|---|---|---|
| Avisos por correo y solicitudes de receta (si están disponibles y configurados) | El correo que muestra la aplicación: nombres de medicamentos, existencias y fechas; una solicitud de receta también incluye el código del producto y su nombre. Sin posología ni notas | El servidor SMTP que usted configure y, después, los destinatarios que indique |
| Copia de seguridad en la nube (opcional) | Una copia diaria de sus perfiles, cifrada en el dispositivo con su frase de contraseña de copia de seguridad. Esta frase no se envía al proveedor de la nube | Su cuenta de OneDrive o Google Drive; en Windows, también una carpeta de su elección |
| Sincronización (opcional) | Medicamentos, existencias, tomas, nombre del perfil y destinatarios, cifrados de extremo a extremo | Su cuenta de OneDrive o Google Drive; en Windows, también una carpeta compartida |
| Exportación, exportación al calendario e intercambio | Un archivo de perfil `.mrz` cifrado, un archivo de calendario `.ics` o el contenido que usted decida compartir. Los títulos del calendario son genéricos salvo que elija incluir nombres de medicamentos | La ubicación del archivo o la aplicación/el destinatario que elija mediante las funciones de archivos e intercambio de Windows, Android o iOS |
| Actualizaciones de catálogos y datos públicos | Solicitudes de listas públicas de medicamentos y otros datos públicos relacionados; no se envían datos personales de salud | GitHub (`raw.githubusercontent.com`). GitHub recibe su dirección IP y los detalles de la solicitud |
| Comprobación de actualizaciones de Windows | Una solicitud de la última versión de escritorio; no se envían datos personales de salud. GitHub recibe su dirección IP y la versión de la aplicación | GitHub (`api.github.com`) |
| Actualizaciones de Android e iOS | Google Play y la App Store distribuyen las actualizaciones y pueden tratar información de la cuenta, el dispositivo o diagnósticos conforme a sus propias políticas y a su configuración | Google Play; App Store de Apple |

Al conectar OneDrive o Google Drive, MedReminder solicita acceso a los datos de la aplicación y a los archivos de copia de seguridad, no acceso general a sus demás archivos. Desactivar una función detiene sus futuros envíos; puede revocar el acceso concedido desde la configuración de su cuenta de Microsoft o Google. Esto no elimina las copias que ya estén guardadas en el proveedor. Los servicios en la nube y de correo tratan los datos según sus propias políticas de privacidad.

En Windows, la copia de seguridad en la nube y la sincronización también pueden usar las carpetas que usted elija. Un proveedor de nube puede sincronizar esas carpetas o usted puede compartirlas con otras personas; el acceso y el tratamiento dependen del proveedor y de la configuración de la carpeta. En Android, estas funciones usan la cuenta conectada de OneDrive o Google Drive. Android o la configuración de ahorro de batería del dispositivo pueden retrasar las tareas programadas. En el iPhone, la copia de seguridad en la nube y la sincronización usan la cuenta conectada de OneDrive o Google Drive, e iOS decide cuándo se ejecutan las tareas en segundo plano: puede retrasarlas o, en una aplicación que abre poco, no ejecutarlas.

Microsoft Store, Google Play y la App Store pueden proporcionar al desarrollador estadísticas agregadas, como las descargas, e informes de fallos, según la configuración de diagnóstico y uso compartido de su dispositivo (en el iPhone, «Compartir con desarrolladores de apps»). No incluyen los datos guardados en MedReminder.

## 5. Cámara y bloqueo de la aplicación

El lector puede usar la cámara web de Windows o la cámara de Android o del iPhone cuando usted decide escanear. Lee el código de barras de la caja de un medicamento, el código de la receta en una receta y, en Android y en el iPhone, el código QR que muestra otro dispositivo para un círculo. Las imágenes se procesan en el dispositivo y MedReminder no las guarda ni las envía. De una receta solo se conserva el código de la receta, nunca los demás datos que pueda contener su código de barras; el código QR del círculo no se guarda. Cada código también puede escribirse en lugar de escanearse. En Windows, la cámara se apaga cuando se lee un código, cuando cierra la ventana de lectura o después de 30 segundos. En Android y en el iPhone, la cámara solo se usa con el permiso que usted concede en el sistema operativo, y solo mientras una página de lectura está abierta. En el iPhone, la cámara solo se usa mientras una página de lectura está abierta.

En Android y en el iPhone, el bloqueo opcional de la aplicación usa el desbloqueo del teléfono: datos biométricos, como la huella o Face ID, o el PIN, el patrón o el código del bloqueo de pantalla. La comprobación la realiza el sistema operativo; MedReminder solo recibe el resultado, nunca datos biométricos.

## 6. Enlaces que abren el navegador

Algunos comandos abren una página web en el navegador, como el prospecto o la ficha informativa del medicamento, la guía del usuario o la página del proyecto. En Windows, si usted lo elige, también puede abrirse una página de donación de Stripe o PayPal. MedReminder no envía datos a esos sitios; lo hace el navegador, según las políticas de dichos sitios. MedReminder nunca ve los datos de pago.

## 7. Menores

MedReminder no está dirigido a menores. El desarrollador no recoge datos personales de menores a través de la aplicación.

## 8. Sus derechos

Puede consultar, corregir, exportar y borrar los datos de MedReminder. En Windows, use Configuración → Copia de seguridad / Restaurar → Exportar todos los datos; en Android y en el iPhone, use las funciones de exportación y gestión de datos de la aplicación. También puede eliminar los datos locales de la aplicación Android desde los ajustes de Android, o eliminar la aplicación del iPhone. Gestione las copias en correos, archivos exportados o compartidos y cuentas de OneDrive o Google Drive con los destinatarios, aplicaciones o proveedores correspondientes.

## 9. Cambios

Los cambios de esta política se publican en este archivo con una nueva fecha de «Última actualización». El historial está disponible en el repositorio:
https://github.com/vger70/MedReminder/commits/main/PRIVACY.es.md
