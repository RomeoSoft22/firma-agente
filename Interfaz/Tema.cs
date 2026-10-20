using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace AgenteFirma.Interfaz;

/// <summary>Colores, fuentes y utilidades del tema oscuro de las ventanas del agente.</summary>
public static class Tema
{
    public static readonly Color Fondo = Color.FromArgb(32, 32, 32);
    public static readonly Color Tarjeta = Color.FromArgb(43, 43, 43);
    public static readonly Color TarjetaHover = Color.FromArgb(58, 58, 58);
    public static readonly Color TarjetaPresionada = Color.FromArgb(68, 68, 68);
    public static readonly Color Borde = Color.FromArgb(60, 60, 60);
    public static readonly Color Texto = Color.FromArgb(240, 240, 240);
    public static readonly Color TextoSuave = Color.FromArgb(160, 160, 160);

    /// <summary>Fuente de íconos de Windows 11 (o la de Windows 10 si no existe).</summary>
    public static readonly string FuenteIconos =
        new InstalledFontCollection().Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

    // Glifos de Segoe MDL2 / Fluent Icons
    public const string GlifoLista = "";
    public const string GlifoActualizar = "";
    public const string GlifoReiniciar = "";
    public const string GlifoApagar = "";
    public const string GlifoFlecha = "";
    public const string GlifoGlobo = "";

    /// <summary>Esquinas redondeadas y marco oscuro (Windows 11; en Windows 10 se ignora).</summary>
    public static void AplicarVentana(Form ventana, bool redondeada)
    {
        try
        {
            var oscuro = 1;
            DwmSetWindowAttribute(ventana.Handle, 20, ref oscuro, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            if (redondeada)
            {
                var esquina = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(ventana.Handle, 33, ref esquina, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE
            }
        }
        catch { /* versión de Windows sin soporte */ }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int atributo, ref int valor, int tamano);
}

/// <summary>Botón plano del panel: ícono a la izquierda y texto, con estados hover/presionado.</summary>
public sealed class BotonPanel : Control
{
    private bool _encima;
    private bool _presionado;

    public string Glifo { get; set; } = "";

    public BotonPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
        ForeColor = Tema.Texto;
        Font = new Font("Segoe UI", 11f);
        Height = 50;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { _encima = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _encima = false; _presionado = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _presionado = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _presionado = false; Invalidate(); base.OnMouseUp(e); }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) OnClick(EventArgs.Empty);
        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var fondo = _presionado ? Tema.TarjetaPresionada : _encima || Focused ? Tema.TarjetaHover : Tema.Tarjeta;
        using (var ruta = Iconos.Redondeado(new Rectangle(0, 0, Width - 1, Height - 1), 6))
        using (var pincel = new SolidBrush(fondo))
        using (var borde = new Pen(Tema.Borde))
        {
            g.FillPath(pincel, ruta);
            g.DrawPath(borde, ruta);
        }

        var alto = Height;
        if (!string.IsNullOrEmpty(Glifo))
        {
            using var fuenteGlifo = new Font(Tema.FuenteIconos, 16f, GraphicsUnit.Point);
            TextRenderer.DrawText(g, Glifo, fuenteGlifo, new Rectangle(14, 0, 34, alto), ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        TextRenderer.DrawText(g, Text, Font, new Rectangle(56, 0, Width - 64, alto), ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
