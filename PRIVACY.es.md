# MedReminder — Política de privacidad

Última actualización: 5 de octubre de 2026

Traducción de [PRIVACY.md](PRIVACY.md). En caso de discrepancia,
prevalece la versión inglesa.

Esta política describe cómo la aplicación de escritorio MedReminder
para Windows trata los datos personales. Se aplica a todas las
distribuciones de la aplicación: los paquetes ZIP, los instaladores MSI
y Microsoft Store.

## 1. En resumen

- MedReminder guarda sus datos en su PC. El desarrollador no gestiona
  ningún servidor y no recibe ninguno de sus datos.
- No hay cuenta con el desarrollador, ni telemetría, ni estadísticas de
  uso, ni publicidad, ni seguimiento.
- Los datos solo salen de su PC mediante funciones que usted activa:
  avisos por correo electrónico, copia de seguridad en la nube,
  sincronización y búsqueda de actualizaciones. Cada una envía datos
  solo al servicio que usted elige.

## 2. Quién es el responsable

MedReminder es software libre y de código abierto (Apache License 2.0)
desarrollado por vger70. Como el desarrollador no recoge ni recibe sus
datos, usted mantiene el control sobre ellos: la aplicación los procesa
en su propio dispositivo, por cuenta de usted.

Contacto: info@medreminder26.org, o una incidencia en
https://github.com/vger70/MedReminder/issues (no publique datos de
salud en una incidencia pública).

## 3. Datos guardados en su PC

MedReminder guarda todo en `%LOCALAPPDATA%\MedReminder\`, dentro de su
cuenta de Windows:

- perfiles: nombre, función, PIN opcional (guardado como hash);
- medicamentos, dosis, pautas, existencias, tomas, recetas, plazos
  administrativos y notas, una base de datos por perfil;
- configuración del correo: servidor SMTP, nombre de usuario y
  contraseña (la contraseña se cifra con Windows DPAPI), direcciones
  del remitente y de los destinatarios, incluida una posible dirección
  del médico o del cuidador;
- tokens de inicio de sesión de OneDrive o Google Drive, si los
  conecta;
- configuración, copias de seguridad que usted configure y archivos de
  registro.

Los datos se refieren a su salud (medicamentos y tratamiento). Las
bases de datos no están cifradas: cualquier persona que pueda usar su
cuenta de Windows puede leerlas. El PIN del perfil evita abrir por
error el perfil equivocado; no es una protección. Para separar los
datos de otras personas, dé a cada una su propia cuenta de Windows.

Los archivos de registro nunca contienen contraseñas, textos de correos
ni notas médicas.

Desinstalar MedReminder no borra esta carpeta. Elimínela para borrar
todos los datos.

## 4. Datos que salen de su PC

Solo estas funciones envían datos, y solo cuando usted las usa:

| Función | Qué se envía | Adónde |
|---|---|---|
| Avisos por correo y solicitudes de receta | El correo que ve en la aplicación: nombres de los medicamentos, existencias, fechas; una solicitud de receta incluye además el código del producto y su nombre. Ni posología ni notas | El servidor SMTP de la cuenta de correo que configure y, después, los destinatarios que indique |
| Copia de seguridad en la nube (opcional) | Una copia diaria de sus perfiles, cifrada en su PC con una frase de contraseña que nunca sale del PC | Su propio OneDrive (carpeta de la aplicación) o Google Drive (carpeta MedReminder), o una carpeta de su elección |
| Sincronización entre PC (opcional) | Medicamentos, existencias, tomas, nombre del perfil y destinatarios, cifrados de extremo a extremo | Su propio OneDrive, Google Drive o carpeta compartida |
| Exportación al calendario | Un archivo `.ics`, con títulos genéricos salvo que elija incluir los nombres de los medicamentos | Guardado donde usted elija; los correos de existencias bajas lo adjuntan |
| Búsqueda de actualizaciones y actualización del catálogo (activada por defecto, se puede desactivar) | Una petición de la última versión y de las listas públicas de medicamentos; ningún dato personal. GitHub ve su dirección IP y la versión de la aplicación | GitHub (`api.github.com`, `raw.githubusercontent.com`) |

Cuando conecta OneDrive o Google Drive, MedReminder solo pide acceso a
su propia carpeta (OneDrive `Files.ReadWrite.AppFolder`; Google Drive
`drive.file` y `drive.appdata`), no a sus demás archivos. Desactivar la
copia de seguridad en la nube o la sincronización detiene todo envío;
el acceso concedido se revoca desde la configuración de su cuenta de
Microsoft o de Google. Estos servicios tratan los datos según sus
propias políticas de privacidad, al igual que su proveedor de correo.

## 5. Cámara web

El lector de códigos de barras puede usar su cámara web. Las imágenes
se decodifican en su PC y nunca se guardan ni se envían. La cámara se
apaga cuando se lee un código, cuando cierra la ventana de lectura o
tras 30 segundos.

## 6. Enlaces que abren el navegador

Algunos comandos abren una página web en su navegador predeterminado:
el prospecto o la ficha informativa del medicamento, la guía del
usuario, la página del proyecto y, si usted lo elige, una página de
donación de Stripe o PayPal. MedReminder no envía datos a estos sitios;
lo hace su navegador, según las políticas de dichos sitios.
MedReminder nunca ve los datos de pago.

## 7. Menores

MedReminder no está dirigido a menores y no recoge datos de nadie.

## 8. Sus derechos

Todos los datos están en su PC, bajo su control: puede consultarlos,
corregirlos, exportarlos (Configuración → Copia de seguridad /
Restaurar → Exportar todos los datos) y borrarlos en cualquier
momento. Para los datos de su cuenta de correo, OneDrive o Google
Drive, diríjase a esos proveedores.

## 9. Cambios

Los cambios de esta política se publican en este archivo, con una nueva
fecha de "Última actualización". El historial está disponible en el
repositorio:
https://github.com/vger70/MedReminder/commits/main/PRIVACY.es.md
