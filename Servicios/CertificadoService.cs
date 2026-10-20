using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AgenteFirma.Modelos;

namespace AgenteFirma.Servicios;

/// <summary>
/// Lectura del almacén de certificados de Windows (incluye los de tokens / DNIe,
/// que el minidriver de la tarjeta publica en CurrentUser\My).
/// Reemplaza a ServiceFirma.SeleccionarCertificado + ServiceParceX509ToCertBean + ValidarCertificado.
/// </summary>
public class CertificadoService
{
    private const string OidSerialNumber = "2.5.4.5";
    private const string OidOrganizacion = "2.5.4.10";
    private const string OidCargo = "2.5.4.12";
    private const string OidUnidad = "2.5.4.11";

    public List<CertificadoDto> Listar(bool incluirNoAptos)
    {
        var resultado = new List<CertificadoDto>();
        foreach (var ubicacion in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, ubicacion);
            try { store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly); }
            catch (CryptographicException) { continue; }

            foreach (var cert in store.Certificates)
            {
                var dto = ADto(cert, ubicacion.ToString());
                // Por defecto solo certificados de firma (excluye p. ej. "localhost" o los de dispositivo de Windows).
                if (incluirNoAptos || (dto.AptoFirma && dto.DeFirma))
                    resultado.Add(dto);
            }
        }

        return resultado
            .GroupBy(c => c.Thumbprint)
            .Select(g => g.First())
            .OrderByDescending(c => c.AptoFirma && c.DeFirma)
            .ThenByDescending(c => c.AptoFirma)
            .ThenBy(c => c.Titular)
            .ToList();
    }

    /// <summary>Busca el certificado (con clave privada) por huella digital.</summary>
    public X509Certificate2 Obtener(string thumbprint)
    {
        var huella = (thumbprint ?? "").Replace(" ", "").ToUpperInvariant();
        foreach (var ubicacion in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, ubicacion);
            try { store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly); }
            catch (CryptographicException) { continue; }

            var encontrados = store.Certificates.Find(X509FindType.FindByThumbprint, huella, false);
            if (encontrados.Count > 0)
            {
                var cert = encontrados[0];
                if (!cert.HasPrivateKey)
                    throw new InvalidOperationException("El certificado seleccionado no tiene clave privada asociada.");
                ValidarParaFirma(cert);
                return cert;
            }
        }
        throw new KeyNotFoundException("No se encontró el certificado. Verifique que el token / DNIe esté conectado.");
    }

    /// <summary>Equivalente a ValidarCertificado.paraFirmaDigital.</summary>
    public void ValidarParaFirma(X509Certificate2 cert)
    {
        var ahora = DateTime.Now;
        if (ahora < cert.NotBefore)
            throw new InvalidOperationException($"El certificado aún no es válido (desde {cert.NotBefore:dd/MM/yyyy}).");
        if (ahora > cert.NotAfter)
            throw new InvalidOperationException($"El certificado venció el {cert.NotAfter:dd/MM/yyyy}.");
        var usos = UsosClave(cert);
        if (usos.Length > 0 && !usos.Contains("DigitalSignature") && !usos.Contains("NonRepudiation"))
            throw new InvalidOperationException("El certificado no está habilitado para firma digital (uso de clave).");
    }

    /// <summary>Cadena de certificación (hoja → raíz) para incrustar en la firma CMS.</summary>
    public X509Certificate2[] Cadena(X509Certificate2 cert)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.Build(cert);
        var lista = chain.ChainElements.Select(e => e.Certificate).ToArray();
        return lista.Length > 0 ? lista : new[] { cert };
    }

    public static string Titular(X509Certificate2 cert) =>
        cert.GetNameInfo(X509NameType.SimpleName, false) is { Length: > 0 } cn ? cn : cert.Subject;

    public static string Emisor(X509Certificate2 cert) =>
        cert.GetNameInfo(X509NameType.SimpleName, true) is { Length: > 0 } cn ? cn : cert.Issuer;

    /// <summary>Atributo SERIALNUMBER del sujeto (en DNIe / RENIEC suele contener el DNI).</summary>
    public static string? DocumentoIdentidad(X509Certificate2 cert) => Atributo(cert.SubjectName, OidSerialNumber);

    private CertificadoDto ADto(X509Certificate2 cert, string almacen)
    {
        var ahora = DateTime.Now;
        var vigente = ahora >= cert.NotBefore && ahora <= cert.NotAfter;
        var usos = UsosClave(cert);
        var usoFirma = usos.Length == 0 || usos.Contains("DigitalSignature") || usos.Contains("NonRepudiation");

        bool confiable;
        using (var chain = new X509Chain())
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            confiable = chain.Build(cert);
        }

        // Certificado emitido para firma de documentos: No repudio, o EKU de correo seguro / firma de documentos / PDF.
        var ekus = cert.Extensions.OfType<X509EnhancedKeyUsageExtension>()
            .SelectMany(e => e.EnhancedKeyUsages.Cast<Oid>())
            .Select(o => o.Value)
            .ToHashSet();
        var deFirma = usos.Contains("NonRepudiation")
            || ekus.Overlaps(new[] { "1.3.6.1.5.5.7.3.4", "1.3.6.1.4.1.311.10.3.12", "1.2.840.113583.1.1.5", "2.5.29.37.0" });

        string algoritmo = cert.GetKeyAlgorithm() switch
        {
            "1.2.840.113549.1.1.1" => "RSA",
            "1.2.840.10045.2.1" => "ECDSA",
            var otro => otro
        };

        return new CertificadoDto(
            Thumbprint: cert.Thumbprint,
            Titular: Titular(cert),
            Documento: DocumentoIdentidad(cert),
            Organizacion: Atributo(cert.SubjectName, OidOrganizacion),
            Cargo: Atributo(cert.SubjectName, OidCargo) ?? Atributo(cert.SubjectName, OidUnidad),
            Emisor: Emisor(cert),
            NumeroSerie: cert.SerialNumber,
            ValidoDesde: cert.NotBefore,
            ValidoHasta: cert.NotAfter,
            Vigente: vigente,
            CadenaConfiable: confiable,
            AptoFirma: cert.HasPrivateKey && vigente && usoFirma,
            DeFirma: deFirma,
            UsoClave: usos,
            Almacen: almacen,
            Algoritmo: algoritmo);
    }

    private static string[] UsosClave(X509Certificate2 cert)
    {
        var ext = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (ext == null) return Array.Empty<string>();
        return Enum.GetValues<X509KeyUsageFlags>()
            .Where(f => f != X509KeyUsageFlags.None && ext.KeyUsages.HasFlag(f))
            .Select(f => f.ToString())
            .ToArray();
    }

    private static string? Atributo(X500DistinguishedName nombre, string oid)
    {
        foreach (var rdn in nombre.EnumerateRelativeDistinguishedNames())
        {
            if (rdn.GetSingleElementType().Value == oid)
                return rdn.GetSingleElementValue();
        }
        return null;
    }
}
