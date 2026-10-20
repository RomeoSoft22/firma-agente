using System.Reflection;
using System.Security.Cryptography;
using AgenteFirma;
using AgenteFirma.Modelos;
using AgenteFirma.Servicios;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

// Agente local de firma: expone en 127.0.0.1 (solo la propia PC) las operaciones que el
// navegador no puede hacer por sí mismo: leer el almacén de certificados / token y firmar.

var reiniciar = args.Contains(Instancia.ArgumentoReiniciar, StringComparer.OrdinalIgnoreCase);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    // "--reiniciar" no es configuración: se quita antes de pasar los argumentos.
    Args = args.Where(a => !a.Equals(Instancia.ArgumentoReiniciar, StringComparison.OrdinalIgnoreCase)).ToArray(),
    ContentRootPath = AppContext.BaseDirectory
});
builder.Logging.AddProvider(new RegistroArchivoProvider());

var puerto = builder.Configuration.GetValue("Agente:Puerto", 9876);
var sitioWeb = builder.Configuration["Agente:SitioWeb"];
var origenes = builder.Configuration.GetSection("Agente:OrigenesPermitidos").Get<string[]>() ?? Array.Empty<string>();

// Una sola instancia por sesión de Windows. Si la anterior está colgada (no responde) o se pidió
// reiniciar, se termina y esta toma su lugar; si la anterior funciona, esta se cierra.
using var instancia = Instancia.Tomar(reiniciar, puerto, origenes.FirstOrDefault());
if (instancia == null) return;
var maxBytes = builder.Configuration.GetValue("Agente:TamanoMaximoMb", 200) * 1024L * 1024L;

builder.WebHost.UseUrls($"http://127.0.0.1:{puerto}");
builder.Services.Configure<KestrelServerOptions>(o =>
{
    o.Limits.MaxRequestBodySize = maxBytes;
    o.AddServerHeader = false; // no revelar el servidor web
});
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxBytes);
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(origenes)
    .AllowAnyHeader()
    .AllowAnyMethod()));

builder.Services.AddSingleton<CertificadoService>();
builder.Services.AddSingleton<PdfFirmaService>();
builder.Services.AddSingleton<PdfVerificacionService>();

var app = builder.Build();

// Bitácora de cada petición: desde qué sitio llega, qué pide, cómo terminó y cuánto tardó.
// (Las consultas de autodiagnóstico del propio ícono de la bandeja no se registran.)
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Headers.ContainsKey(Bandeja.EncabezadoAutodiagnostico))
    {
        await next();
        return;
    }
    var reloj = System.Diagnostics.Stopwatch.StartNew();
    await next();
    reloj.Stop();

    var origen = ctx.Request.Headers.Origin.ToString();
    var desde = string.IsNullOrEmpty(origen) ? "sin encabezado Origin" : origen;
    var operacion = DescribirRuta(ctx.Request.Method, ctx.Request.Path);
    var codigo = ctx.Response.StatusCode;
    var detalle = $"{ctx.Request.Method} {ctx.Request.Path}{ctx.Request.QueryString} desde {desde} → HTTP {codigo} en {reloj.ElapsedMilliseconds} ms";
    if (ctx.Request.ContentLength is > 0) detalle += $" · cuerpo {Bitacora.Tamano(ctx.Request.ContentLength.Value)}";
    if (HttpMethods.IsOptions(ctx.Request.Method))
    {
        var pna = ctx.Request.Headers.ContainsKey("Access-Control-Request-Private-Network") ? " (con permiso de red privada)" : "";
        Bitacora.Info("Verificación previa del navegador", $"El navegador consultó si puede llamar al agente{pna}. {detalle}");
        return;
    }
    if (codigo < 400) Bitacora.Exito(operacion, detalle);
    else if (codigo < 500) Bitacora.Aviso(operacion, detalle);
    else Bitacora.Error(operacion, detalle);
});

// Cabeceras de seguridad + Private Network Access (Chrome lo exige para que una web llame a 127.0.0.1).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Headers.ContainsKey("Access-Control-Request-Private-Network"))
        ctx.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Cache-Control"] = "no-store";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseCors();

// Solo se atiende a las aplicaciones web autorizadas: el navegador siempre envía Origin en las
// llamadas entre orígenes, así una página ajena (o un rebinding de DNS, ya filtrado por AllowedHosts)
// no puede listar certificados ni firmar aunque logre llegar al puerto.
app.Use(async (ctx, next) =>
{
    if (!HttpMethods.IsOptions(ctx.Request.Method))
    {
        var origen = ctx.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origen) || !origenes.Contains(origen, StringComparer.OrdinalIgnoreCase))
        {
            Bitacora.Aviso("Origen no autorizado", string.IsNullOrEmpty(origen)
                ? $"Se rechazó {ctx.Request.Method} {ctx.Request.Path}: llegó sin encabezado Origin (se abrió la dirección " +
                  "directamente en el navegador o la llamó otro programa). Es normal si usted abrió http://127.0.0.1:9876."
                : $"Se rechazó {ctx.Request.Method} {ctx.Request.Path} desde '{origen}': ese sitio no está en " +
                  $"Agente:OrigenesPermitidos ({string.Join(", ", origenes)}). Agréguelo en appsettings.json si es un sitio propio.");
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new ErrorDto("Origen no autorizado."));
            return;
        }
    }
    await next();
});

// Errores controlados → JSON con mensaje para el usuario; el detalle técnico solo va al log local.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (Exception ex)
    {
        // Solo es un fallo del TSA si la excepción se originó dentro del cliente TSA (no basta con que
        // el método SignDetached reciba un parámetro ITSAClient).
        var enTsa = new System.Diagnostics.StackTrace(ex).GetFrames()
            .Any(f => f.GetMethod()?.DeclaringType?.Name == nameof(iText.Signatures.TSAClientBouncyCastle));
        var (estado, mensaje) = ex switch
        {
            _ when enTsa => (StatusCodes.Status502BadGateway,
                "No se pudo obtener el sello de tiempo del servidor TSA configurado. Verifique la URL del TSA o desactive el sello de tiempo."),
            KeyNotFoundException or DirectoryNotFoundException => (StatusCodes.Status404NotFound, ex.Message),
            InvalidOperationException => (StatusCodes.Status422UnprocessableEntity, ex.Message),
            FormatException => (StatusCodes.Status422UnprocessableEntity, "El contenido enviado no es válido."),
            iText.Kernel.Exceptions.PdfException or iText.IO.Exceptions.IOException
                => (StatusCodes.Status422UnprocessableEntity, "El archivo no es un PDF válido o está dañado."),
            CryptographicException
                => (StatusCodes.Status409Conflict, "No se pudo usar la clave del certificado (¿PIN cancelado o token desconectado?)."),
            _ => (StatusCodes.Status500InternalServerError, "Ocurrió un error inesperado al procesar la solicitud.")
        };
        app.Logger.LogError(ex, "Error procesando {Ruta}", ctx.Request.Path);
        var interno = ex.InnerException != null ? $" | Causa: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
        Bitacora.Error("Error en " + DescribirRuta(ctx.Request.Method, ctx.Request.Path),
            $"Mensaje mostrado al usuario: «{mensaje}». Detalle técnico: {ex.GetType().Name}: {ex.Message}{interno}");
        ctx.Response.StatusCode = estado;
        await ctx.Response.WriteAsJsonAsync(new ErrorDto(mensaje));
    }
});

// Un token sólo admite una operación de firma a la vez (y un solo diálogo de PIN).
var candadoFirma = new SemaphoreSlim(1, 1);
var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

app.MapGet("/api/estado", () => Results.Ok(new
{
    nombre = "Agente de Firma Digital",
    version,
    equipo = Environment.MachineName,
    hora = DateTime.Now
}));

app.MapGet("/api/certificados", (bool? todos, CertificadoService svc) =>
    Results.Ok(svc.Listar(todos ?? false)));

app.MapPost("/api/firmar", async (FirmarRequest req, PdfFirmaService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Thumbprint)) return Results.BadRequest(new ErrorDto("Seleccione un certificado."));
    if (string.IsNullOrWhiteSpace(req.PdfBase64)) return Results.BadRequest(new ErrorDto("Adjunte un PDF."));
    await candadoFirma.WaitAsync();
    try { return Results.Ok(svc.Firmar(req)); }
    finally { candadoFirma.Release(); }
});

app.MapPost("/api/firmar-carpeta", async (FirmarCarpetaRequest req, PdfFirmaService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.Thumbprint)) return Results.BadRequest(new ErrorDto("Seleccione un certificado."));
    if (string.IsNullOrWhiteSpace(req.CarpetaOrigen)) return Results.BadRequest(new ErrorDto("Indique la carpeta de origen."));
    await candadoFirma.WaitAsync();
    try { return Results.Ok(await Task.Run(() => svc.FirmarCarpeta(req))); }
    finally { candadoFirma.Release(); }
});

app.MapPost("/api/verificar", (VerificarRequest req, PdfVerificacionService svc) =>
{
    if (string.IsNullOrWhiteSpace(req.PdfBase64)) return Results.BadRequest(new ErrorDto("Adjunte un PDF."));
    return Results.Ok(svc.Verificar(Convert.FromBase64String(req.PdfBase64)));
});

try
{
    await app.StartAsync();
}
catch (IOException ex)
{
    app.Logger.LogError(ex, "No se pudo abrir el puerto {Puerto}", puerto);
    Bitacora.Error("Inicio del agente", $"No se pudo abrir el puerto {puerto}: {ex.Message}. Otro programa lo está usando.");
    MessageBox.Show(
        $"El Agente de Firma no pudo iniciarse: el puerto {puerto} está siendo usado por otro programa.",
        "Agente de Firma Digital", MessageBoxButtons.OK, MessageBoxIcon.Error);
    return;
}

Bitacora.Exito("Inicio del agente",
    $"Agente de Firma v{version} escuchando en http://127.0.0.1:{puerto}{(reiniciar ? " (reiniciado)" : "")}. " +
    $"Equipo {Environment.MachineName}, usuario {Environment.UserName}, Windows {Environment.OSVersion.Version}.");
Bitacora.Info("Configuración", $"Sitios autorizados: {string.Join(", ", origenes)}. Sitio de firma: {sitioWeb ?? "(no definido)"}. " +
    $"Tamaño máximo de PDF: {maxBytes / 1024 / 1024} MB. Ejecutable: {Environment.ProcessPath}. Registro: {RegistroArchivoProvider.Carpeta}.");
try
{
    var certs = app.Services.GetRequiredService<CertificadoService>().Listar(false);
    Bitacora.Info("Certificados de firma",
        certs.Count == 0
            ? "No se encontró ningún certificado de firma en el almacén personal de Windows. Conecte el token o DNIe."
            : $"{certs.Count} disponible(s): " + string.Join("; ", certs.Select(c => $"{c.Titular} (vence {c.ValidoHasta:dd/MM/yyyy}, emisor {c.Emisor})")));
}
catch (Exception ex)
{
    Bitacora.Aviso("Certificados de firma", $"No se pudo leer el almacén de certificados: {ex.Message}");
}

// Ícono en la bandeja del sistema (Windows Forms requiere un hilo STA propio).
var hiloBandeja = new Thread(() =>
{
    Application.EnableVisualStyles();
    Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
    Application.Run(new Bandeja(puerto, sitioWeb, origenes.FirstOrDefault(), version, () => app.Lifetime.StopApplication()));
});
hiloBandeja.SetApartmentState(ApartmentState.STA);
hiloBandeja.IsBackground = true;
hiloBandeja.Start();

await app.WaitForShutdownAsync();
Bitacora.Info("Cierre del agente", "El agente se detuvo.");

static string DescribirRuta(string metodo, PathString ruta) => ruta.Value?.ToLowerInvariant() switch
{
    "/api/estado" => "Consulta de estado",
    "/api/certificados" => "Lectura de certificados",
    "/api/firmar" => "Firma de documento",
    "/api/firmar-carpeta" => "Firma de carpeta",
    "/api/verificar" => "Verificación de firmas",
    _ => $"Petición {metodo} {ruta}"
};
