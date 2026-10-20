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

## Ejecución

```powershell
dotnet run
```

Distribución a los usuarios como un único ejecutable autocontenido:

```powershell
dotnet publish -c Release   # bin\Release\net8.0-windows\win-x64\publish\AgenteFirma.exe
```

Debe ejecutarse **en la sesión del usuario** (no como servicio de Windows) para ver su almacén
personal y mostrar el diálogo de PIN.

## Configuración (`appsettings.json`)

| Clave | Descripción |
| --- | --- |
| `Agente:Puerto` | Puerto local (por defecto 9876) |
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
