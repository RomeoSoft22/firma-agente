using AgenteFirma.Modelos;
using iText.Kernel.Pdf;
using iText.Signatures;
using Org.BouncyCastle.Asn1.X509;

namespace AgenteFirma.Servicios;

/// <summary>Validación de documentos firmados (equivalente a ServiceValidationSignedDoc).</summary>
public class PdfVerificacionService
{
    public VerificarResponse Verificar(byte[] pdf)
    {
        using var lector = new PdfReader(new MemoryStream(pdf));
        using var documento = new PdfDocument(lector);
        var util = new SignatureUtil(documento);
        var nombres = util.GetSignatureNames();
        var total = util.GetTotalRevisions();

        var firmas = new List<FirmaVerificada>();
        foreach (var nombre in nombres)
        {
            try
            {
                var pkcs7 = util.ReadSignatureData(nombre);
                var cert = pkcs7.GetSigningCertificate();
                var integridad = pkcs7.VerifySignatureIntegrityAndAuthenticity();

                var tieneTsa = pkcs7.GetTimeStampDate() != TimestampConstants.UNDEFINED_TIMESTAMP_DATE;
                var tsaValido = tieneTsa && pkcs7.VerifyTimestampImprint();
                DateTime? fechaTsa = tieneTsa ? pkcs7.GetTimeStampDate() : null;
                DateTime? fechaFirma = pkcs7.GetSignDate() == TimestampConstants.UNDEFINED_TIMESTAMP_DATE
                    ? null : pkcs7.GetSignDate();
                // BouncyCastle entrega la vigencia en UTC; la fecha de firma puede venir en hora local.
                var referencia = (fechaTsa ?? fechaFirma ?? DateTime.Now).ToUniversalTime();
                var desde = DateTime.SpecifyKind(cert.NotBefore, DateTimeKind.Utc);
                var hasta = DateTime.SpecifyKind(cert.NotAfter, DateTimeKind.Utc);

                firmas.Add(new FirmaVerificada(
                    NombreCampo: nombre,
                    Firmante: Valor(cert.SubjectDN, X509Name.CN) ?? cert.SubjectDN.ToString(),
                    Documento: Valor(cert.SubjectDN, X509Name.SerialNumber),
                    Emisor: Valor(cert.IssuerDN, X509Name.CN) ?? cert.IssuerDN.ToString(),
                    NumeroSerie: cert.SerialNumber.ToString(16).ToUpperInvariant(),
                    FechaFirma: fechaFirma,
                    Motivo: pkcs7.GetReason(),
                    Ubicacion: pkcs7.GetLocation(),
                    IntegridadValida: integridad,
                    CubreTodoDocumento: util.SignatureCoversWholeDocument(nombre),
                    Revision: util.GetRevision(nombre),
                    TotalRevisiones: total,
                    TieneSelloTiempo: tieneTsa,
                    SelloTiempoValido: tsaValido,
                    FechaSelloTiempo: fechaTsa,
                    CertificadoVigenteAlFirmar: referencia >= desde && referencia <= hasta,
                    CertValidoDesde: desde,
                    CertValidoHasta: hasta,
                    AlgoritmoHash: pkcs7.GetHashAlgorithm(),
                    AlgoritmoFirma: pkcs7.GetEncryptionAlgorithm(),
                    Error: null));
            }
            catch (Exception ex)
            {
                firmas.Add(new FirmaVerificada(nombre, "(no se pudo leer)", null, "", "", null, null, null,
                    false, false, 0, total, false, false, null, false, DateTime.MinValue, DateTime.MinValue,
                    "", "", ex.Message));
            }
        }

        var todasValidas = firmas.Count > 0 && firmas.All(f => f.IntegridadValida && f.CertificadoVigenteAlFirmar);
        return new VerificarResponse(firmas.Count, todasValidas, firmas);
    }

    private static string? Valor(X509Name nombre, Org.BouncyCastle.Asn1.DerObjectIdentifier oid)
    {
        var valores = nombre.GetValueList(oid);
        return valores.Count > 0 ? valores[0]?.ToString() : null;
    }
}
