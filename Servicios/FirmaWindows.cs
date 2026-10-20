using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using iText.Signatures;

namespace AgenteFirma.Servicios;

/// <summary>
/// Firma criptográfica con la clave privada del almacén de Windows (equivalente a ServiceFirma2.SignHashed).
/// La clave nunca sale del token: Windows/minidriver pide el PIN cuando corresponde.
/// </summary>
public class FirmaWindows : IExternalSignature
{
    private readonly X509Certificate2 _certificado;

    public FirmaWindows(X509Certificate2 certificado)
    {
        _certificado = certificado;
    }

    public string GetHashAlgorithm() => DigestAlgorithms.SHA256;

    public string GetEncryptionAlgorithm() =>
        _certificado.GetKeyAlgorithm() == "1.2.840.10045.2.1" ? "ECDSA" : "RSA";

    public byte[] Sign(byte[] message)
    {
        using (var rsa = _certificado.GetRSAPrivateKey())
        {
            if (rsa != null)
                return rsa.SignData(message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        using (var ecdsa = _certificado.GetECDsaPrivateKey())
        {
            if (ecdsa != null)
                return ecdsa.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        }

        throw new CryptographicException("No se pudo acceder a la clave privada del certificado (¿token desconectado o PIN cancelado?).");
    }
}
