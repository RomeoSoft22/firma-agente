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

Es una aplicación de Windows sin consola que vive en la bandeja del sistema:

- **Ícono verde** = el agente responde; **amarillo** = no responde (se autodiagnostica cada 10 s
  consultándose a sí mismo como lo haría el sitio web; avisa con una notificación si deja de responder).
- **Clic izquierdo → panel**: estado del componente de firma, abrir el sitio de firma, *Ver bitácora*,
  *Buscar actualizaciones* (lee `<SitioWeb>/descargas/agente-version.json`), *Reiniciar* y *Cerrar*.
- **Bitácora**: cada evento con hora, operación y detalle — peticiones recibidas (sitio de origen,
  ruta, resultado y tiempo), orígenes rechazados y por qué, pasos de cada firma (certificado, archivo,
  motivo, sello, TSA, CRL/OCSP, reparación de PDF, reintentos), verificaciones, errores con su causa
  técnica, reinicios y autodiagnóstico. Tiene buscador, copiar/guardar, *Sistema → Diagnóstico de
  conexión* y *Ayuda → ¿La web dice que no conecta?*. Si la web no conecta y en la bitácora no aparece
  ninguna petición desde el sitio, el navegador la está bloqueando antes de llegar al agente.
- Una sola instancia por usuario; si la anterior quedó colgada se reemplaza sola (también con
  *Reiniciar* o el acceso «Reiniciar Agente de Firma» del menú Inicio).
- Registro diario en `%LOCALAPPDATA%\AgenteFirma\logs` (14 días).

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
