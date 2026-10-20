namespace AgenteFirma.Modelos;

/// <summary>Equivalente web de CertificadoBean.</summary>
public record CertificadoDto(
    string Thumbprint,
    string Titular,
    string? Documento,
    string? Organizacion,
    string? Cargo,
    string Emisor,
    string NumeroSerie,
    DateTime ValidoDesde,
    DateTime ValidoHasta,
    bool Vigente,
    bool CadenaConfiable,
    bool AptoFirma,
    bool DeFirma,
    string[] UsoClave,
    string Almacen,
    string Algoritmo);

/// <summary>Equivalente web de MotivoBean + PosicionFirmaService.</summary>
public class FirmaOpciones
{
    public string? Motivo { get; set; }
    public string? Ubicacion { get; set; }
    public string? Contacto { get; set; }

    /// <summary>Firma visible (con recuadro en una página) o invisible.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>Página 1..N; 0 o negativo = última página.</summary>
    public int Pagina { get; set; } = 1;

    /// <summary>
    /// "manual" usa X/Y; si no: inferior-derecha, inferior-izquierda, inferior-centro,
    /// superior-derecha, superior-izquierda (calculado según el tamaño de la página).
    /// </summary>
    public string Posicion { get; set; } = "manual";

    /// <summary>Coordenadas en puntos PDF, origen en la esquina inferior izquierda.</summary>
    public float X { get; set; } = 360;
    public float Y { get; set; } = 40;
    public float Ancho { get; set; } = 200;
    public float Alto { get; set; } = 60;

    /// <summary>Texto libre para el recuadro; si es nulo se genera uno estándar.</summary>
    public string? TextoPersonalizado { get; set; }

    /// <summary>Imagen (PNG/JPG en base64) de la rúbrica o logo a mostrar junto al texto.</summary>
    public string? ImagenBase64 { get; set; }

    /// <summary>
    /// Diseño del sello: "horizontal" = imagen a la izquierda (≈38 % del ancho) y texto a la derecha;
    /// "vertical" = imagen arriba y texto abajo (p. ej. para el V° B°).
    /// </summary>
    public string Disposicion { get; set; } = "horizontal";

    public bool UsarSelloTiempo { get; set; }
    public string? TsaUrl { get; set; }
    public string? TsaUsuario { get; set; }
    public string? TsaClave { get; set; }

    /// <summary>Incrustar respuestas OCSP / CRL (equivalente a FirmarPDFOcsp).</summary>
    public bool IncluirRevocacion { get; set; }

    /// <summary>Firma de certificación: cierra el documento a cambios posteriores.</summary>
    public bool Certificar { get; set; }
}

public class FirmarRequest
{
    public string Thumbprint { get; set; } = "";
    public string PdfBase64 { get; set; } = "";
    public string? NombreArchivo { get; set; }
    public FirmaOpciones Opciones { get; set; } = new();
}

public record FirmarResponse(
    string PdfBase64,
    string NombreArchivo,
    string Firmante,
    string? DocumentoFirmante,
    string Emisor,
    string NumeroSerie,
    DateTime FechaFirma,
    bool SelloTiempo,
    string HashSha256);

public class FirmarCarpetaRequest
{
    public string Thumbprint { get; set; } = "";
    public string CarpetaOrigen { get; set; } = "";
    public string? CarpetaDestino { get; set; }
    public string Sufijo { get; set; } = "[FIRMADO]";
    public bool Recursivo { get; set; }
    public FirmaOpciones Opciones { get; set; } = new();
}

public record ResultadoArchivo(string Origen, string? Destino, bool Ok, string? Error);

public record FirmarCarpetaResponse(int Total, int Firmados, int Errores, List<ResultadoArchivo> Resultados);

public class VerificarRequest
{
    public string PdfBase64 { get; set; } = "";
}

public record FirmaVerificada(
    string NombreCampo,
    string Firmante,
    string? Documento,
    string Emisor,
    string NumeroSerie,
    DateTime? FechaFirma,
    string? Motivo,
    string? Ubicacion,
    bool IntegridadValida,
    bool CubreTodoDocumento,
    int Revision,
    int TotalRevisiones,
    bool TieneSelloTiempo,
    bool SelloTiempoValido,
    DateTime? FechaSelloTiempo,
    bool CertificadoVigenteAlFirmar,
    DateTime CertValidoDesde,
    DateTime CertValidoHasta,
    string AlgoritmoHash,
    string AlgoritmoFirma,
    string? Error);

public record VerificarResponse(int TotalFirmas, bool TodasValidas, List<FirmaVerificada> Firmas);

public record ErrorDto(string Mensaje, string? Detalle = null);
