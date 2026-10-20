using System.Text.RegularExpressions;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;

namespace AgenteFirma.Servicios;

/// <summary>
/// Dibuja la apariencia visible de la firma (capa n2 del campo) con el diseño institucional:
/// imagen del motivo + texto «Firmado digitalmente por / Motivo / DNI / Fecha».
/// </summary>
public static class SelloFirma
{
    private const float Margen = 2f;
    private const float Interlineado = 1.18f;
    private const float FuenteMaxima = 9f;
    private const float FuenteMinima = 3.5f;

    /// <summary>Proporción del sello que ocupa la imagen: ancho en horizontal, alto en vertical.</summary>
    private const float ProporcionImagenHorizontal = 0.45f;
    private const float ProporcionImagenVertical = 0.55f;

    /// <summary>Margen interno mínimo alrededor del ícono, como fracción de su área.</summary>
    private const float AireImagen = 0.02f;

    public static void Dibujar(PdfFormXObject capa, PdfDocument documento, float ancho, float alto,
        byte[]? imagen, string disposicion, string[] lineas)
    {
        var canvas = new PdfCanvas(capa, documento);
        var fuente = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
        var vertical = string.Equals(disposicion, "vertical", StringComparison.OrdinalIgnoreCase);

        Rectangle areaTexto;
        if (imagen is { Length: > 0 })
        {
            var datos = ImageDataFactory.Create(imagen);
            Rectangle areaImagen;
            if (vertical)
            {
                var altoImagen = alto * ProporcionImagenVertical;
                areaImagen = new Rectangle(Margen, alto - altoImagen, ancho - 2 * Margen, altoImagen - Margen);
                areaTexto = new Rectangle(Margen, Margen, ancho - 2 * Margen, alto - altoImagen - Margen);
            }
            else
            {
                var anchoImagen = ancho * ProporcionImagenHorizontal;
                areaImagen = new Rectangle(Margen, Margen, anchoImagen - Margen, alto - 2 * Margen);
                areaTexto = new Rectangle(anchoImagen + Margen, Margen, ancho - anchoImagen - 2 * Margen, alto - 2 * Margen);
            }
            DibujarImagen(canvas, datos, areaImagen);
        }
        else
        {
            areaTexto = new Rectangle(Margen, Margen, ancho - 2 * Margen, alto - 2 * Margen);
        }

        DibujarTexto(canvas, fuente, lineas, areaTexto, vertical);
        canvas.Release();
    }

    /// <summary>Imagen escalada sin deformar y centrada en su área.</summary>
    private static void DibujarImagen(PdfCanvas canvas, ImageData datos, Rectangle area)
    {
        var escala = Math.Min(area.GetWidth() / datos.GetWidth(), area.GetHeight() / datos.GetHeight()) * (1 - AireImagen);
        var w = datos.GetWidth() * escala;
        var h = datos.GetHeight() * escala;
        var x = area.GetX() + (area.GetWidth() - w) / 2;
        var y = area.GetY() + (area.GetHeight() - h) / 2;
        canvas.AddImageFittedIntoRectangle(datos, new Rectangle(x, y, w, h), false);
    }

    /// <summary>
    /// Texto con salto de línea por palabras y el mayor tamaño de letra que permita que todo entre en el área.
    /// </summary>
    private static void DibujarTexto(PdfCanvas canvas, PdfFont fuente, string[] lineas, Rectangle area, bool vertical)
    {
        var tamano = FuenteMaxima;
        List<string> renglones;
        while (true)
        {
            renglones = lineas.SelectMany(l => Partir(fuente, l, tamano, area.GetWidth())).ToList();
            if (renglones.Count * tamano * Interlineado <= area.GetHeight() || tamano <= FuenteMinima) break;
            tamano -= 0.25f;
        }

        var altoBloque = renglones.Count * tamano * Interlineado;
        // Horizontal: bloque centrado verticalmente junto a la imagen. Vertical: pegado bajo la imagen.
        var arriba = vertical
            ? area.GetTop()
            : area.GetY() + (area.GetHeight() + altoBloque) / 2;
        var y = arriba - tamano;

        canvas.BeginText().SetFontAndSize(fuente, tamano).SetFillColor(ColorConstants.BLACK);
        foreach (var renglon in renglones)
        {
            canvas.SetTextMatrix(area.GetX(), y).ShowText(renglon);
            y -= tamano * Interlineado;
        }
        canvas.EndText();
    }

    private static IEnumerable<string> Partir(PdfFont fuente, string texto, float tamano, float anchoMaximo)
    {
        var actual = "";
        foreach (var palabra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var prueba = actual.Length == 0 ? palabra : actual + " " + palabra;
            if (fuente.GetWidth(prueba, tamano) <= anchoMaximo || actual.Length == 0)
            {
                actual = prueba;
            }
            else
            {
                yield return actual;
                actual = palabra;
            }
        }
        if (actual.Length > 0) yield return actual;
    }

    /// <summary>Número de documento a partir del SERIALNUMBER del certificado (p. ej. "DNI-12345678" → "12345678").</summary>
    public static string? Dni(string? serialNumber)
    {
        if (string.IsNullOrWhiteSpace(serialNumber)) return null;
        var numero = Regex.Match(serialNumber, @"\d{8,}");
        return numero.Success ? numero.Value : serialNumber.Trim();
    }
}
