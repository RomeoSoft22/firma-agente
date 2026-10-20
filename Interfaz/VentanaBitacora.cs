using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using AgenteFirma.Servicios;

namespace AgenteFirma.Interfaz;

/// <summary>
/// Bitácora del agente: cada evento con hora, operación y detalle (peticiones recibidas y desde qué
/// sitio, pasos de la firma, errores, reinicios, autodiagnóstico). Se actualiza en vivo y permite buscar.
/// </summary>
public sealed class VentanaBitacora : Form
{
    private const int MaximoVisibles = 1500;
    private static readonly Color ColorHora = Color.FromArgb(120, 200, 120);
    private static readonly Color ColorInfo = Color.FromArgb(150, 205, 150);
    private static readonly Color ColorExito = Color.FromArgb(95, 215, 120);
    private static readonly Color ColorAviso = Color.FromArgb(230, 190, 80);
    private static readonly Color ColorError = Color.FromArgb(255, 120, 110);
    private static readonly Color ColorSeparador = Color.FromArgb(70, 70, 70);
    private static readonly Font FuenteSeparador = new("Consolas", 6f);

    private readonly RichTextBox _texto;
    private readonly ToolStripTextBox _buscar;
    private readonly ToolStripStatusLabel _resumen;
    private string _filtro = "";

    public event EventHandler? Reiniciar;
    public event EventHandler? AbrirSitio;
    public event EventHandler? Diagnosticar;

    public VentanaBitacora(string version)
    {
        Text = $"Bitácora del Agente de Firma {version}";
        Icon = Iconos.Bandeja(true);
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1000, 700);
        MinimumSize = new Size(640, 400);
        Font = new Font("Segoe UI", 10f);
        AutoScaleMode = AutoScaleMode.Dpi;

        // ------------------------------------------------------------ Menú
        var menu = new MenuStrip { Renderer = new ToolStripProfessionalRenderer(new ColoresOscuros()), BackColor = Tema.Tarjeta, ForeColor = Tema.Texto, Padding = new Padding(8, 4, 8, 4) };
        var mBitacora = Menu("Bitácora",
            ("Copiar todo", (_, _) => Clipboard.SetText(TextoPlano())),
            ("Guardar como…", (_, _) => Guardar()),
            ("Limpiar vista", (_, _) => { Bitacora.Limpiar(); Redibujar(); }),
            ("Abrir carpeta de registros", (_, _) => Abrir(RegistroArchivoProvider.Carpeta)));
        var mSistema = Menu("Sistema",
            ("Diagnóstico de conexión", (_, _) => Diagnosticar?.Invoke(this, EventArgs.Empty)),
            ("Reiniciar agente", (_, _) => Reiniciar?.Invoke(this, EventArgs.Empty)),
            ("Abrir sitio de firma", (_, _) => AbrirSitio?.Invoke(this, EventArgs.Empty)));
        var mAyuda = Menu("Ayuda",
            ("¿La web dice que no conecta?", (_, _) => MostrarAyudaConexion()),
            ("Acerca de", (_, _) => MessageBox.Show(this,
                $"Agente de Firma Digital {version}\nFirma PAdES con el certificado del usuario (token, DNIe o almacén de Windows).\n\nRegistro: {RegistroArchivoProvider.Carpeta}",
                "Acerca de", MessageBoxButtons.OK, MessageBoxIcon.Information)));
        _buscar = new ToolStripTextBox
        {
            Alignment = ToolStripItemAlignment.Right,
            BackColor = Tema.Fondo,
            ForeColor = Tema.Texto,
            BorderStyle = BorderStyle.FixedSingle,
            AutoSize = false,
            Width = 220
        };
        _buscar.TextChanged += (_, _) => { _filtro = _buscar.Text.Trim(); Redibujar(); };
        menu.Items.AddRange([mBitacora, mSistema, mAyuda, _buscar]);
        MainMenuStrip = menu;

        // ------------------------------------------------------------ Contenido
        _texto = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = ColorInfo,
            Font = new Font("Consolas", 11f),
            DetectUrls = false,
            WordWrap = true,
            HideSelection = false
        };

        // ------------------------------------------------------------ Pie con logo
        var pie = new Panel { Dock = DockStyle.Bottom, Height = 84, BackColor = Tema.Fondo };
        var logo = new PictureBox { Image = Iconos.Logo(Iconos.Azul, 46), SizeMode = PictureBoxSizeMode.CenterImage, Size = new Size(50, 50) };
        var marca = new Label { Text = "Agente de Firma", Font = new Font("Georgia", 20f), AutoSize = true };
        pie.Controls.AddRange([logo, marca]);
        pie.Resize += (_, _) =>
        {
            var total = logo.Width + 12 + marca.Width;
            logo.Location = new Point((pie.Width - total) / 2, (pie.Height - logo.Height) / 2);
            marca.Location = new Point(logo.Right + 12, (pie.Height - marca.Height) / 2);
        };

        var estado = new StatusStrip { BackColor = Tema.Tarjeta, ForeColor = Tema.TextoSuave, SizingGrip = true };
        _resumen = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        estado.Items.Add(_resumen);

        Controls.Add(_texto);
        Controls.Add(pie);
        Controls.Add(estado);
        Controls.Add(menu);

        Bitacora.Nueva += AlNuevaEntrada;
        Shown += (_, _) =>
        {
            SendMessage(_buscar.TextBox.Handle, 0x1501, 1, "Buscar…"); // EM_SETCUEBANNER
            Redibujar();
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Tema.AplicarVentana(this, redondeada: false);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Bitacora.Nueva -= AlNuevaEntrada;
        base.OnFormClosed(e);
    }

    private void AlNuevaEntrada(EntradaBitacora entrada)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (!Coincide(entrada)) return;
            var alFinal = _texto.SelectionStart >= _texto.TextLength - 1;
            Escribir(entrada);
            ActualizarResumen();
            if (alFinal) { _texto.SelectionStart = _texto.TextLength; _texto.ScrollToCaret(); }
        });
    }

    private bool Coincide(EntradaBitacora e) =>
        _filtro.Length == 0
        || e.Operacion.Contains(_filtro, StringComparison.OrdinalIgnoreCase)
        || e.Detalle.Contains(_filtro, StringComparison.OrdinalIgnoreCase);

    private void Redibujar()
    {
        _texto.SuspendLayout();
        _texto.Clear();
        foreach (var e in Bitacora.Entradas.Where(Coincide).TakeLast(MaximoVisibles)) Escribir(e);
        _texto.SelectionStart = _texto.TextLength;
        _texto.ScrollToCaret();
        _texto.ResumeLayout();
        ActualizarResumen();
    }

    private void Escribir(EntradaBitacora e)
    {
        var color = e.Nivel switch
        {
            NivelBitacora.Exito => ColorExito,
            NivelBitacora.Aviso => ColorAviso,
            NivelBitacora.Error => ColorError,
            _ => ColorInfo
        };
        Agregar(new string('─', 160) + "\n", ColorSeparador, FuenteSeparador);
        Agregar($"{e.Hora:HH:mm:ss.fff}", ColorHora);
        Agregar($"          {e.Operacion}", color);
        if (e.Nivel is NivelBitacora.Aviso or NivelBitacora.Error) Agregar($"  [{(e.Nivel == NivelBitacora.Error ? "ERROR" : "AVISO")}]", color);
        Agregar("\n" + e.Detalle + "\n", color == ColorInfo ? ColorInfo : Color.FromArgb(Math.Min(255, color.R + 20), Math.Min(255, color.G + 20), Math.Min(255, color.B + 20)));
    }

    private void Agregar(string texto, Color color, Font? fuente = null)
    {
        _texto.SelectionStart = _texto.TextLength;
        _texto.SelectionLength = 0;
        _texto.SelectionColor = color;
        _texto.SelectionFont = fuente ?? _texto.Font;
        _texto.AppendText(texto);
    }

    private void ActualizarResumen()
    {
        var todas = Bitacora.Entradas;
        var errores = todas.Count(e => e.Nivel == NivelBitacora.Error);
        var avisos = todas.Count(e => e.Nivel == NivelBitacora.Aviso);
        var visibles = _filtro.Length == 0 ? todas.Count : todas.Count(Coincide);
        _resumen.Text = $"{visibles} evento(s){(_filtro.Length > 0 ? $" que contienen «{_filtro}»" : "")} · {errores} error(es) · {avisos} aviso(s) · registro diario en {RegistroArchivoProvider.Carpeta}";
    }

    private string TextoPlano()
    {
        var sb = new StringBuilder();
        foreach (var e in Bitacora.Entradas.Where(Coincide))
            sb.AppendLine($"{e.Hora:yyyy-MM-dd HH:mm:ss.fff}  [{e.Nivel}]  {e.Operacion}").AppendLine(e.Detalle).AppendLine();
        return sb.Length == 0 ? "(bitácora vacía)" : sb.ToString();
    }

    private void Guardar()
    {
        using var dialogo = new SaveFileDialog
        {
            FileName = $"bitacora-agente-{DateTime.Now:yyyyMMdd-HHmm}.txt",
            Filter = "Texto (*.txt)|*.txt"
        };
        if (dialogo.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialogo.FileName, TextoPlano(), Encoding.UTF8);
    }

    private void MostrarAyudaConexion() => MessageBox.Show(this,
        "Si la página de firma dice que no puede conectarse con el agente:\n\n" +
        "1. Mire esta bitácora mientras pulsa «Actualizar» o «Probar conexión» en la web.\n" +
        "   • Si NO aparece una línea «Consulta de estado … desde https://su-sitio», la llamada nunca llegó al agente: " +
        "el navegador la bloqueó. Desactive el bloqueador de anuncios / escudos para ese sitio y permita el " +
        "«acceso a la red local / dispositivos locales» (candado de la barra de direcciones).\n" +
        "   • Si aparece «Origen no autorizado», el sitio no está en Agente:OrigenesPermitidos.\n" +
        "   • Si aparece un error, el detalle explica la causa (certificado, PIN, PDF, sello de tiempo…).\n\n" +
        "2. Use Sistema → Diagnóstico de conexión para comprobar el agente por dentro.\n" +
        "3. Si el ícono está amarillo, use Reiniciar.",
        "¿La web no conecta con el agente?", MessageBoxButtons.OK, MessageBoxIcon.Information);

    private static ToolStripMenuItem Menu(string titulo, params (string Texto, EventHandler Accion)[] opciones)
    {
        var menu = new ToolStripMenuItem(titulo) { ForeColor = Tema.Texto };
        foreach (var (texto, accion) in opciones)
            menu.DropDownItems.Add(new ToolStripMenuItem(texto, null, accion) { ForeColor = Tema.Texto, BackColor = Tema.Tarjeta });
        return menu;
    }

    private static void Abrir(string ruta)
    {
        try { Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true }); } catch { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);

    private sealed class ColoresOscuros : ProfessionalColorTable
    {
        public override Color MenuStripGradientBegin => Tema.Tarjeta;
        public override Color MenuStripGradientEnd => Tema.Tarjeta;
        public override Color MenuItemSelected => Tema.TarjetaHover;
        public override Color MenuItemBorder => Tema.Borde;
        public override Color MenuBorder => Tema.Borde;
        public override Color MenuItemSelectedGradientBegin => Tema.TarjetaHover;
        public override Color MenuItemSelectedGradientEnd => Tema.TarjetaHover;
        public override Color MenuItemPressedGradientBegin => Tema.TarjetaPresionada;
        public override Color MenuItemPressedGradientEnd => Tema.TarjetaPresionada;
        public override Color ToolStripDropDownBackground => Tema.Tarjeta;
        public override Color ImageMarginGradientBegin => Tema.Tarjeta;
        public override Color ImageMarginGradientMiddle => Tema.Tarjeta;
        public override Color ImageMarginGradientEnd => Tema.Tarjeta;
        public override Color SeparatorDark => Tema.Borde;
        public override Color StatusStripGradientBegin => Tema.Tarjeta;
        public override Color StatusStripGradientEnd => Tema.Tarjeta;
    }
}
