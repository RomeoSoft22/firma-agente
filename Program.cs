using System.Reflection;
using System.Security.Cryptography;
using AgenteFirma;
using AgenteFirma.Modelos;
using AgenteFirma.Servicios;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

// Agente local de firma: expone en 127.0.0.1 (solo la propia PC) las operaciones que el
// navegador no puede hacer por sí mismo: leer el almacén de certificados / token y firmar.

// Una sola instancia por sesión de Windows (el instalador lo inicia al iniciar sesión).
using var instancia = new Mutex(true, @"Local\AgenteFirmaDigital", out var esPrimera);
if (!esPrimera) return;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Logging.AddProvider(new RegistroArchivoProvider());

var puerto = builder.Configuration.GetValue("Agente:Puerto", 9876);
var sitioWeb = builder.Configuration["Agente:SitioWeb"];
var origenes = builder.Configuration.GetSection("Agente:OrigenesPermitidos").Get<string[]>() ?? Array.Empty<string>();
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
    MessageBox.Show(
        $"El Agente de Firma no pudo iniciarse: el puerto {puerto} está siendo usado por otro programa.",
        "Agente de Firma Digital", MessageBoxButtons.OK, MessageBoxIcon.Error);
    return;
}

app.Logger.LogInformation("Agente de firma v{Version} escuchando en http://127.0.0.1:{Puerto} — orígenes permitidos: {Origenes}",
    version, puerto, string.Join(", ", origenes));

// Ícono en la bandeja del sistema (Windows Forms requiere un hilo STA propio).
var hiloBandeja = new Thread(() =>
{
    Application.EnableVisualStyles();
    Application.Run(new Bandeja(puerto, sitioWeb, () => app.Lifetime.StopApplication()));
});
hiloBandeja.SetApartmentState(ApartmentState.STA);
hiloBandeja.IsBackground = true;
hiloBandeja.Start();

await app.WaitForShutdownAsync();
