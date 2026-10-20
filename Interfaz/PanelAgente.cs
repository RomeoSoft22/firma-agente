namespace AgenteFirma.Interfaz;

/// <summary>
/// Panel del agente (clic en el ícono de la bandeja): estado del componente de firma y acciones
/// Ver bitácora, Buscar actualizaciones, Reiniciar y Cerrar.
/// </summary>
public sealed class PanelAgente : Form
{
    private const int Ancho = 380;
    private const int Margen = 16;

    private readonly Label _estado;
    private readonly Label _detalle;
    private readonly Label _pie;

    public event EventHandler? VerBitacora;
    public event EventHandler? BuscarActualizaciones;
    public event EventHandler? Reiniciar;
    public event EventHandler? Cerrar;
    public event EventHandler? AbrirSitio;

    public PanelAgente(string version, int puerto)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = new Font("Segoe UI", 10f);
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(Ancho, 448);
        Text = "Agente de Firma";

        // ------------------------------------------------------------ Encabezado
        var logo = new PictureBox
        {
            Image = Iconos.Logo(Iconos.Azul, 52),
            SizeMode = PictureBoxSizeMode.CenterImage,
            Bounds = new Rectangle(Margen + 4, 22, 56, 56)
        };
        var titulo = new Label
        {
            Text = "Agente de Firma",
            Font = new Font("Georgia", 19f),
            AutoSize = true,
            Location = new Point(Margen + 72, 24)
        };
        var subtitulo = new Label
        {
            Text = $"Firma Digital · versión {version}",
            ForeColor = Tema.TextoSuave,
            AutoSize = true,
            Location = new Point(Margen + 75, 58)
        };
        var separador = new Label { BackColor = Tema.Borde, Bounds = new Rectangle(0, 98, Ancho, 1) };

        // ------------------------------------------------------------ Estado del componente
        var componente = new Label
        {
            Text = "Componente de Firma Digital",
            Font = new Font("Segoe UI", 11f),
            AutoSize = true,
            Location = new Point(Margen, 114)
        };
        _estado = new Label { AutoSize = true, Location = new Point(Margen, 140), Font = new Font("Segoe UI Semibold", 10f) };
        _detalle = new Label
        {
            AutoSize = false,
            ForeColor = Tema.TextoSuave,
            Bounds = new Rectangle(Margen, 164, Ancho - 2 * Margen, 20),
            AutoEllipsis = true,
            Text = $"http://127.0.0.1:{puerto}"
        };
        var abrir = new BotonPanel { Glifo = Tema.GlifoFlecha, Bounds = new Rectangle(Ancho - Margen - 44, 120, 44, 44) };
        abrir.Click += (_, _) => AbrirSitio?.Invoke(this, EventArgs.Empty);
        new ToolTip().SetToolTip(abrir, "Abrir el sitio de firma");

        // ------------------------------------------------------------ Acciones
        var y = 200;
        BotonPanel Accion(string glifo, string texto, Action alPulsar)
        {
            var b = new BotonPanel { Glifo = glifo, Text = texto, Bounds = new Rectangle(Margen, y, Ancho - 2 * Margen, 50) };
            b.Click += (_, _) => alPulsar();
            y += 58;
            return b;
        }
        var bitacora = Accion(Tema.GlifoLista, "Ver bitácora", () => VerBitacora?.Invoke(this, EventArgs.Empty));
        var actualizar = Accion(Tema.GlifoActualizar, "Buscar actualizaciones", () => BuscarActualizaciones?.Invoke(this, EventArgs.Empty));
        var reiniciar = Accion(Tema.GlifoReiniciar, "Reiniciar", () => Reiniciar?.Invoke(this, EventArgs.Empty));
        var cerrar = Accion(Tema.GlifoApagar, "Cerrar Agente de Firma", () => Cerrar?.Invoke(this, EventArgs.Empty));

        _pie = new Label
        {
            ForeColor = Tema.TextoSuave,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 8.5f),
            Bounds = new Rectangle(0, y + 2, Ancho, 18)
        };

        Controls.AddRange([logo, titulo, subtitulo, separador, componente, _estado, _detalle, abrir,
            bitacora, actualizar, reiniciar, cerrar, _pie]);
        ClientSize = new Size(Ancho, y + 26);
        MostrarEstado(null, "Comprobando…");
    }

    /// <summary>activo: true = responde, false = no responde, null = comprobando.</summary>
    public void MostrarEstado(bool? activo, string detalle)
    {
        if (InvokeRequired) { BeginInvoke(() => MostrarEstado(activo, detalle)); return; }
        (_estado.Text, _estado.ForeColor) = activo switch
        {
            true => ("●  Activo", Iconos.Verde),
            false => ("●  Sin respuesta", Iconos.Amarillo),
            null => ("●  Comprobando…", Tema.TextoSuave)
        };
        _detalle.Text = detalle;
        _pie.Text = $"Último diagnóstico: {DateTime.Now:HH:mm:ss}";
    }

    /// <summary>Muestra el panel sobre la bandeja del sistema (esquina inferior derecha).</summary>
    public void MostrarJuntoABandeja()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 12, area.Bottom - Height - 12);
        Show();
        Activate();
    }

    // Mostrar no le quita el foco a la aplicación actual; MostrarJuntoABandeja lo activa explícitamente.
    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Tema.AplicarVentana(this, redondeada: true);
    }

    // Se oculta al hacer clic fuera o con Esc, como los paneles de la bandeja de Windows.
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Hide();
        base.OnKeyDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnFormClosing(e);
    }
}
