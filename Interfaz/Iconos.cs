using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AgenteFirma.Interfaz;

/// <summary>Íconos dibujados en tiempo de ejecución: verde = agente activo, amarillo = sin respuesta.</summary>
public static class Iconos
{
    public static readonly Color Verde = Color.FromArgb(30, 158, 90);
    public static readonly Color Amarillo = Color.FromArgb(232, 168, 0);
    public static readonly Color Azul = Color.FromArgb(31, 95, 191);

    public static Icon Bandeja(bool activo) => Crear(activo ? Verde : Amarillo, 32);

    /// <summary>Logo "FD" en un cuadrado redondeado del color indicado.</summary>
    public static Bitmap Logo(Color color, int tamano)
    {
        var bmp = new Bitmap(tamano, tamano);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);
        var radio = Math.Max(2, (int)(tamano * 0.22));
        using (var ruta = Redondeado(new Rectangle(0, 0, tamano - 1, tamano - 1), radio))
        using (var relleno = new SolidBrush(color))
            g.FillPath(relleno, ruta);
        using var fuente = new Font("Segoe UI", tamano * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("FD", fuente, Brushes.White, new RectangleF(0, 0, tamano, tamano), formato);
        return bmp;
    }

    public static GraphicsPath Redondeado(Rectangle r, int radio)
    {
        var d = radio * 2;
        var ruta = new GraphicsPath();
        ruta.AddArc(r.X, r.Y, d, d, 180, 90);
        ruta.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        ruta.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        ruta.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        ruta.CloseFigure();
        return ruta;
    }

    private static Icon Crear(Color color, int tamano)
    {
        using var bmp = Logo(color, tamano);
        var handle = bmp.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
