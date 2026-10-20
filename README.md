# firma-agente

Agente local de firma digital (.NET 8 + iText 7). Es el único componente que accede al
almacén de certificados de Windows, al token o al DNIe y firma con la clave privada: el
navegador no puede hacerlo por sí mismo. La clave nunca sale del dispositivo y Windows pide
el PIN cuando corresponde.

Forma parte del sistema de firma digital:
[firma-web](https://github.com/RomeoSoft22/firma-web) ·
[firma-api](https://github.com/RomeoSoft22/firma-api) ·
[firma-servicio](https://github.com/RomeoSoft22/firma-servicio) (documentación general).

## Funciones

| Endpoint (`http://127.0.0.1:9876/api`) | Descripción |
| --- | --- |
| `GET /estado` | Versión y equipo |
| `GET /certificados?todos=false` | Certificados de firma del almacén personal (incluye token/DNIe) |
| `POST /firmar` | Firma PAdES de un PDF: sello visible con imagen y diseño por motivo, sello de tiempo (TSA), OCSP/CRL, certificación |
| `POST /firmar-carpeta` | Firma masiva de una carpeta local |
| `POST /verificar` | Integridad, firmante, vigencia y sello de tiempo de cada firma |

## Requisitos

- Windows 10/11 y el middleware/minidriver del token o DNIe.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar (`winget install Microsoft.DotNet.SDK.8`).

## Ejecución (desarrollo)

```powershell
dotnet run
```

Es una aplicación de Windows sin consola: aparece como ícono en la bandeja del sistema (menú:
abrir el sitio de firma, ver registro, salir). Solo se permite una instancia por usuario. El registro
se guarda en `%LOCALAPPDATA%\AgenteFirma\logs` (14 días).

## Instalador para las PCs de los usuarios

```powershell
winget install JRSoftware.InnoSetup          # una sola vez
powershell -ExecutionPolicy Bypass -File instalador\construir.ps1
```

Genera `instalador\salida\AgenteFirmaSetup.exe` (~77 MB, incluye .NET: no hay que instalar nada más):

- Instala por usuario en `%LOCALAPPDATA%\Programs\AgenteFirma`, **sin pedir administrador**.
- Crea acceso en el menú Inicio y **arranca al iniciar sesión** (carpeta Inicio del usuario).
- Al actualizar, detiene la versión en ejecución; se desinstala desde «Aplicaciones instaladas».
- Instalación desatendida: `AgenteFirmaSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`.

Debe ejecutarse **en la sesión del usuario** (no como servicio de Windows) para ver su almacén
personal y mostrar el diálogo de PIN. El instalador no está firmado con un certificado de firma de
código, por lo que SmartScreen puede advertir «editor desconocido».

## Configuración (`appsettings.json`)

| Clave | Descripción |
| --- | --- |
| `Agente:Puerto` | Puerto local (por defecto 9876) |
| `Agente:SitioWeb` | Sitio que abre el ícono de la bandeja (`https://firma.juarezcampos.net`) |
| `Agente:OrigenesPermitidos` | Sitios web autorizados a usar el agente (el front) |
| `Agente:TamanoMaximoMb` | Tamaño máximo de PDF |
| `Agente:TsaUrlPorDefecto` | Servidor de sello de tiempo si la petición no indica uno |
| `AllowedHosts` | `127.0.0.1;localhost` (protección contra DNS rebinding) |

## Seguridad

- Escucha solo en `127.0.0.1`; exige un `Origin` de la lista blanca (403 si no) y filtra `Host`.
- No expone la cabecera `Server`; los errores devuelven mensajes para el usuario y el detalle
  técnico queda solo en el log local.
- Una firma a la vez (un solo diálogo de PIN).

## Licencia de iText

iText 7 es AGPL. Para uso cerrado o comercial se requiere licencia comercial o reemplazarlo.
