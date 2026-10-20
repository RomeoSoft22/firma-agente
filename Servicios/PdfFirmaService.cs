using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using AgenteFirma.Modelos;
using iText.IO.Image;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Signatures;
using Org.BouncyCastle.Security;
using BcX509 = Org.BouncyCastle.X509.X509Certificate;

namespace AgenteFirma.Servicios;

/// <summary>
/// Incrusta la firma PAdES en el PDF (equivalente a ServiceFirma.FirmarPDFitext / FirmarContenidoV2 / firmarPdf,
/// ServiceTsa.GetTimeStampToken y ServiceFirma.FirmarPDFOcsp de la aplicación de escritorio).
/// </summary>
public class PdfFirmaService
{
    private readonly CertificadoService _certificados;
    private readonly IConfiguration _config;
    private readonly ILogger<PdfFirmaService> _log;

    public PdfFirmaService(CertificadoService certificados, IConfiguration config, ILogger<PdfFirmaService> log)
    {
        _certificados = certificados;
        _config = config;
        _log = log;
    }

    public FirmarResponse Firmar(FirmarRequest req)
    {
        var cert = _certificados.Obtener(req.Thumbprint);
        var entrada = Convert.FromBase64String(req.PdfBase64);
        var (salida, fecha) = FirmarBytes(entrada, cert, req.Opciones);

        var nombre = NombreFirmado(req.NombreArchivo ?? "documento.pdf", "[FIRMADO]");
        return new FirmarResponse(
            PdfBase64: Convert.ToBase64String(salida),
            NombreArchivo: nombre,
            Firmante: CertificadoService.Titular(cert),
            DocumentoFirmante: CertificadoService.DocumentoIdentidad(cert),
            Emisor: CertificadoService.Emisor(cert),
            NumeroSerie: cert.SerialNumber,
            FechaFirma: fecha,
            SelloTiempo: req.Opciones.UsarSelloTiempo,
            HashSha256: Convert.ToHexString(SHA256.HashData(salida)).ToLowerInvariant());
    }

    /// <summary>Firma masiva de una carpeta local (equivalente a ServiceFirmaCarpeta / ServiceFirmaMasiva).</summary>
    public FirmarCarpetaResponse FirmarCarpeta(FirmarCarpetaRequest req)
    {
        if (!Directory.Exists(req.CarpetaOrigen))
            throw new DirectoryNotFoundException($"No existe la carpeta: {req.CarpetaOrigen}");

        var cert = _certificados.Obtener(req.Thumbprint);
        var destino = string.IsNullOrWhiteSpace(req.CarpetaDestino)
            ? System.IO.Path.Combine(req.CarpetaOrigen, "firmados")
            : req.CarpetaDestino!;
        Directory.CreateDirectory(destino);

        var opcion = req.Recursivo ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var destinoCompleto = System.IO.Path.GetFullPath(destino);
        var archivos = Directory.EnumerateFiles(req.CarpetaOrigen, "*.pdf", opcion)
            .Where(f => !System.IO.Path.GetFullPath(f).StartsWith(destinoCompleto, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var resultados = new List<ResultadoArchivo>();
        foreach (var archivo in archivos)
        {
            try
            {
                var relativo = System.IO.Path.GetRelativePath(req.CarpetaOrigen, archivo);
                var salidaRuta = System.IO.Path.Combine(destino,
                    System.IO.Path.GetDirectoryName(relativo) ?? "",
                    NombreFirmado(System.IO.Path.GetFileName(archivo), req.Sufijo));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(salidaRuta)!);

                var (salida, _) = FirmarBytes(File.ReadAllBytes(archivo), cert, req.Opciones);
                File.WriteAllBytes(salidaRuta, salida);
                resultados.Add(new ResultadoArchivo(archivo, salidaRuta, true, null));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Error firmando {Archivo}", archivo);
                resultados.Add(new ResultadoArchivo(archivo, null, false, ex.Message));
            }
        }

        var ok = resultados.Count(r => r.Ok);
        return new FirmarCarpetaResponse(resultados.Count, ok, resultados.Count - ok, resultados);
    }

    private (byte[] Pdf, DateTime Fecha) FirmarBytes(byte[] pdf, X509Certificate2 cert, FirmaOpciones op)
    {
        var cadenaNet = _certificados.Cadena(cert);
        var cadena = cadenaNet.Select(c => DotNetUtilities.FromX509Certificate(c)).ToArray();
        var fecha = DateTime.Now;

        ITSAClient? tsa = null;
        if (op.UsarSelloTiempo)
        {
            var url = string.IsNullOrWhiteSpace(op.TsaUrl) ? _config["Agente:TsaUrlPorDefecto"] : op.TsaUrl;
            tsa = new TSAClientBouncyCastle(url, op.TsaUsuario, op.TsaClave);
        }

        // Las CRL se descargan una sola vez para conocer su tamaño y no volver a pedirlas en cada intento.
        ICollection<ICrlClient>? crls = null;
        IOcspClient? ocsp = null;
        var bytesCrl = 0;
        if (op.IncluirRevocacion)
        {
            ocsp = new OcspClientBouncyCastle(null);
            var listas = DescargarCrls(cadena);
            bytesCrl = listas.Sum(l => l.Length);
            crls = listas.Select(l => (ICrlClient)new CrlClientOffline(l)).ToList();
        }

        // Espacio reservado en el PDF para la firma (contenedor CMS). El valor por defecto de iText (8 KB)
        // no alcanza con cadenas de certificados reales + sello de tiempo + OCSP/CRL.
        var espacio = 8192
            + cadenaNet.Sum(c => c.RawData.Length) * 2
            + (tsa != null ? 16384 : 0)
            + (op.IncluirRevocacion ? 16384 + bytesCrl * 2 : 0);

        for (var intento = 1; ; intento++)
        {
            try
            {
                return (FirmarIntento(pdf, cert, op, fecha, cadena, crls, ocsp, tsa, espacio), fecha);
            }
            catch (iText.Kernel.Exceptions.PdfException ex) when (ex.Message.Contains("Append mode requires") && intento < 3)
            {
                // PDF con errores de estructura (xref dañada, etc.): se reescribe limpio y se reintenta.
                pdf = Reparar(pdf);
            }
            catch (IOException ex) when (ex.Message.Contains("Not enough space") && intento < 3)
            {
                _log.LogWarning("Espacio de firma insuficiente ({Bytes} bytes); se reintenta con el doble.", espacio);
                espacio *= 2;
            }
        }
    }

    /// <summary>
    /// Reescribe un PDF con errores de estructura. Solo si no tiene firmas: reescribirlo invalidaría las existentes.
    /// </summary>
    private byte[] Reparar(byte[] pdf)
    {
        using (var lector = new PdfReader(new MemoryStream(pdf)))
        using (var documento = new PdfDocument(lector))
        {
            if (new SignatureUtil(documento).GetSignatureNames().Count > 0)
                throw new InvalidOperationException(
                    "El PDF tiene errores de estructura y ya contiene firmas: no se puede firmar sin invalidarlas. " +
                    "Solicite una versión corregida del documento.");
        }

        _log.LogWarning("El PDF tenía errores de estructura; se reescribe antes de firmar.");
        using var salida = new MemoryStream();
        using (var lector = new PdfReader(new MemoryStream(pdf)))
        using (var escritor = new PdfWriter(salida))
        using (new PdfDocument(lector, escritor))
        {
        }
        return salida.ToArray();
    }

    private List<byte[]> DescargarCrls(Org.BouncyCastle.X509.X509Certificate[] cadena)
    {
        var listas = new List<byte[]>();
        var cliente = new CrlClientOnline(cadena);
        foreach (var certificado in cadena)
        {
            try
            {
                var encontradas = cliente.GetEncoded(certificado, null);
                if (encontradas != null) listas.AddRange(encontradas);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo descargar la CRL de {Sujeto}", certificado.SubjectDN);
            }
        }
        return listas.Distinct(new ComparadorBytes()).ToList();
    }

    private sealed class ComparadorBytes : IEqualityComparer<byte[]>
    {
        public bool Equals(byte[]? a, byte[]? b) => a != null && b != null && a.AsSpan().SequenceEqual(b);
        public int GetHashCode(byte[] obj) => obj.Length;
    }

    private byte[] FirmarIntento(byte[] pdf, X509Certificate2 cert, FirmaOpciones op, DateTime fecha,
        Org.BouncyCastle.X509.X509Certificate[] cadena, ICollection<ICrlClient>? crls, IOcspClient? ocsp,
        ITSAClient? tsa, int espacio)
    {
        using var lector = new PdfReader(new MemoryStream(pdf));
        using var salida = new MemoryStream();

        // Modo "append": conserva firmas previas (firmas múltiples / co-firma).
        var firmador = new PdfSigner(lector, salida, new StampingProperties().UseAppendMode());
        firmador.SetFieldName($"Firma{fecha:yyyyMMddHHmmssfff}");
        firmador.SetSignDate(fecha);
        if (op.Certificar)
            firmador.SetCertificationLevel(PdfSigner.CERTIFIED_NO_CHANGES_ALLOWED);

        var apariencia = firmador.GetSignatureAppearance()
            .SetReason(op.Motivo ?? "")
            .SetLocation(op.Ubicacion ?? "")
            .SetContact(op.Contacto ?? "")
            .SetReuseAppearance(false);

        if (op.Visible)
        {
            var totalPaginas = firmador.GetDocument().GetNumberOfPages();
            var pagina = op.Pagina <= 0 || op.Pagina > totalPaginas ? totalPaginas : op.Pagina;
            var recuadro = Recuadro(firmador.GetDocument().GetPage(pagina).GetPageSize(), op);
            apariencia.SetPageNumber(pagina).SetPageRect(recuadro);

            // Apariencia propia (capa n2): imagen del motivo + texto, con el diseño del motivo.
            byte[]? imagen = string.IsNullOrWhiteSpace(op.ImagenBase64)
                ? null
                : Convert.FromBase64String(QuitarPrefijoDataUrl(op.ImagenBase64!));
            SelloFirma.Dibujar(apariencia.GetLayer2(), firmador.GetDocument(), recuadro.GetWidth(), recuadro.GetHeight(),
                imagen, op.Disposicion, LineasSello(cert, op, fecha));
        }

        firmador.SignDetached(new FirmaWindows(cert), cadena, crls, ocsp, tsa, espacio, PdfSigner.CryptoStandard.CADES);
        return salida.ToArray();
    }

    /// <summary>Equivalente a PosicionFirmaService: recuadro manual o anclado a una esquina.</summary>
    private static Rectangle Recuadro(Rectangle pagina, FirmaOpciones op)
    {
        const float margen = 30;
        var ancho = Math.Clamp(op.Ancho, 20, pagina.GetWidth() - 2 * margen);
        var alto = Math.Clamp(op.Alto, 20, pagina.GetHeight() - 2 * margen);
        float izquierda = pagina.GetLeft() + margen;
        float derecha = pagina.GetRight() - margen - ancho;
        float centro = pagina.GetLeft() + (pagina.GetWidth() - ancho) / 2;
        float abajo = pagina.GetBottom() + margen;
        float arriba = pagina.GetTop() - margen - alto;

        var (x, y) = (op.Posicion ?? "manual").ToLowerInvariant() switch
        {
            "inferior-derecha" => (derecha, abajo),
            "inferior-izquierda" => (izquierda, abajo),
            "inferior-centro" => (centro, abajo),
            "superior-derecha" => (derecha, arriba),
            "superior-izquierda" => (izquierda, arriba),
            _ => (pagina.GetLeft() + op.X, pagina.GetBottom() + op.Y)
        };
        return new Rectangle(x, y, ancho, alto);
    }

    /// <summary>Texto del sello: Firmado digitalmente por / Motivo / DNI / Fecha.</summary>
    private static string[] LineasSello(X509Certificate2 cert, FirmaOpciones op, DateTime fecha)
    {
        if (!string.IsNullOrWhiteSpace(op.TextoPersonalizado))
            return op.TextoPersonalizado!.Replace("\r", "").Split('\n');

        var lineas = new List<string> { $"Firmado digitalmente por: {CertificadoService.Titular(cert)}" };
        if (!string.IsNullOrWhiteSpace(op.Motivo)) lineas.Add($"Motivo: {op.Motivo}");
        var dni = SelloFirma.Dni(CertificadoService.DocumentoIdentidad(cert));
        if (dni != null) lineas.Add($"DNI: {dni}");
        var zona = TimeZoneInfo.Local.GetUtcOffset(fecha);
        lineas.Add($"Fecha: {fecha:dd.MM.yyyy HH:mm:ss} {(zona < TimeSpan.Zero ? "-" : "+")}{zona:hh\\:mm}");
        return lineas.ToArray();
    }

    private static string QuitarPrefijoDataUrl(string valor)
    {
        var coma = valor.IndexOf(',');
        return valor.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && coma > 0 ? valor[(coma + 1)..] : valor;
    }

    private static string NombreFirmado(string nombre, string sufijo)
    {
        var baseNombre = System.IO.Path.GetFileNameWithoutExtension(nombre);
        return $"{baseNombre}{sufijo}.pdf";
    }
}
