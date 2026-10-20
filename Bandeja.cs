using System.Diagnostics;
using AgenteFirma.Interfaz;
using AgenteFirma.Servicios;
using Timer = System.Windows.Forms.Timer;

namespace AgenteFirma;

/// <summary>
/// Ícono del agente en la bandeja del sistema. Verde = el agente responde; amarillo = no responde.
/// Clic izquierdo abre el panel (estado, bitácora, actualizaciones, reiniciar, cerrar); clic derecho, un menú.
/// </summary>
public sealed class Bandeja : ApplicationContext
{
    /// <summary>Marca las consultas del propio autodiagnóstico para no llenar la bitácora.</summary>
    public const string EncabezadoAutodiagnostico = "X-Agente-Autodiagnostico";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(4) };

    private readonly NotifyIcon _icono;
    private readonly Icon _iconoVerde = Iconos.Bandeja(true);
    private readonly Icon _iconoAmarillo = Iconos.Bandeja(false);
    private readonly Timer _temporizador = new() { Interval = 10_000 };
    private readonly PanelAgente _panel;
    private readonly int _puerto;
    private readonly string? _sitioWeb;
    private readonly string? _origen;
    private readonly string _version;
    private readonly Action _detener;
    private VentanaBitacora? _bitacora;
    private bool? _activo;
    private int _fallosSeguidos;
    private bool _comprobando;

    public Bandeja(int puerto, string? sitioWeb, string? origen, string version, Action detener)
    {
        _puerto = puerto;
        _sitioWeb = sitioWeb;
        _origen = origen;
        _version = version;
        _detener = detener;

        _panel = new PanelAgente(version, puerto);
        _panel.VerBitacora += (_, _) => { _panel.Hide(); MostrarBitacora(); };
        _panel.BuscarActualizaciones += async (_, _) => { _panel.Hide(); await BuscarActualizaciones(); };
        _panel.Reiniciar += (_, _) => ReiniciarAgente();
        _panel.Cerrar += (_, _) => Salir();
        _panel.AbrirSitio += (_, _) => { _panel.Hide(); AbrirSitio(); };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir panel", null, (_, _) => _panel.MostrarJuntoABandeja());
        menu.Items.Add("Ver bitácora", null, (_, _) => MostrarBitacora());
        if (!string.IsNullOrWhiteSpace(sitioWeb)) menu.Items.Add("Abrir el sitio de firma", null, (_, _) => AbrirSitio());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Reiniciar agente", null, (_, _) => ReiniciarAgente());
        menu.Items.Add("Cerrar Agente de Firma", null, (_, _) => Salir());

        _icono = new NotifyIcon
        {
            Icon = _iconoAmarillo,
            Text = "Agente de Firma — comprobando…",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icono.MouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left) return;
            if (_panel.Visible) _panel.Hide();
            else _panel.MostrarJuntoABandeja();
        };

        _temporizador.Tick += async (_, _) => await Comprobar();
        _temporizador.Start();
        _ = Comprobar();
        _ = CapturaDiagnostico();
    }

    // ---------------------------------------------------------------- Autodiagnóstico

    /// <summary>Consulta al propio agente como lo haría el sitio web (con su Origin).</summary>
    private async Task Comprobar()
    {
        if (_comprobando) return;
        _comprobando = true;
        var reloj = Stopwatch.StartNew();
        string detalle;
        bool activo;
        try
        {
            using var pedido = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{_puerto}/api/estado");
            if (!string.IsNullOrEmpty(_origen)) pedido.Headers.Add("Origin", _origen);
            pedido.Headers.Add(EncabezadoAutodiagnostico, "1");
            using var respuesta = await Http.SendAsync(pedido);
            activo = respuesta.IsSuccessStatusCode;
            detalle = activo
                ? $"Responde en 127.0.0.1:{_puerto} · {reloj.ElapsedMilliseconds} ms"
                : $"El agente respondió HTTP {(int)respuesta.StatusCode}";
        }
        catch (Exception ex)
        {
            activo = false;
            detalle = ex is TaskCanceledException ? "No respondió en 4 segundos" : $"Sin conexión: {ex.Message}";
        }
        finally
        {
            _comprobando = false;
        }

        _fallosSeguidos = activo ? 0 : _fallosSeguidos + 1;
        _panel.MostrarEstado(activo, detalle);
        _icono.Icon = activo ? _iconoVerde : _iconoAmarillo;
        _icono.Text = Recortar(activo ? $"Agente de Firma v{_version} — activo" : "Agente de Firma — sin respuesta (use Reiniciar)");

        if (_activo != activo)
        {
            if (activo) Bitacora.Exito("Autodiagnóstico", $"El agente funciona correctamente. {detalle}.");
            else
            {
                Bitacora.Error("Autodiagnóstico", $"El agente no responde: {detalle}. Ícono en amarillo; use «Reiniciar».");
                _icono.ShowBalloonTip(5000, "Agente de Firma sin respuesta",
                    "El agente no responde. Abra el panel y pulse «Reiniciar».", ToolTipIcon.Warning);
            }
            _activo = activo;
        }
    }

    /// <summary>Prueba completa desde la bitácora (Sistema → Diagnóstico de conexión).</summary>
    private async Task Diagnosticar()
    {
        Bitacora.Info("Diagnóstico de conexión", $"Inicio del diagnóstico (agente v{_version}, puerto {_puerto}).");
        await Comprobar();
        Bitacora.Registrar(_activo == true ? NivelBitacora.Exito : NivelBitacora.Error, "Diagnóstico de conexión",
            _activo == true
                ? $"El agente responde en http://127.0.0.1:{_puerto} a las peticiones del sitio autorizado ({_origen})."
                : $"El agente NO responde en http://127.0.0.1:{_puerto}. Use «Reiniciar»; si persiste, otro programa puede estar usando el puerto.");
        Bitacora.Info("Diagnóstico de conexión",
            "Ahora pulse «Actualizar» (pantalla Firmar) o «Probar conexión» (Configuración) en la web. " +
            "Si en esta bitácora aparece «Consulta de estado … desde " + (_sitioWeb ?? "su sitio") + "», la conexión funciona. " +
            "Si NO aparece ninguna línea nueva, el navegador está bloqueando la llamada antes de que llegue al agente: " +
            "desactive el bloqueador de anuncios / escudos para ese sitio y permita el «acceso a la red local» " +
            "(candado de la barra de direcciones → Configuración del sitio).");
    }

    // ---------------------------------------------------------------- Acciones

    private void MostrarBitacora()
    {
        if (_bitacora == null || _bitacora.IsDisposed)
        {
            _bitacora = new VentanaBitacora(_version);
            _bitacora.Reiniciar += (_, _) => ReiniciarAgente();
            _bitacora.AbrirSitio += (_, _) => AbrirSitio();
            _bitacora.Diagnosticar += async (_, _) => await Diagnosticar();
        }
        _bitacora.Show();
        if (_bitacora.WindowState == FormWindowState.Minimized) _bitacora.WindowState = FormWindowState.Normal;
        _bitacora.Activate();
    }

    private async Task BuscarActualizaciones()
    {
        var r = await Actualizador.Buscar(_sitioWeb, _version);
        if (!r.HayNueva || r.UrlInstalador == null)
        {
            MessageBox.Show(r.Mensaje, "Buscar actualizaciones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var respuesta = MessageBox.Show($"{r.Mensaje}\n\n¿Descargar e instalar ahora? El agente se cerrará y volverá a abrirse.",
            "Actualización disponible", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (respuesta != DialogResult.Yes) return;
        try
        {
            await Actualizador.Instalar(r.UrlInstalador, r.VersionNueva ?? "nueva");
        }
        catch (Exception ex)
        {
            Bitacora.Error("Actualización", $"No se pudo descargar o iniciar el instalador: {ex.Message}");
            MessageBox.Show($"No se pudo actualizar: {ex.Message}", "Actualización", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AbrirSitio()
    {
        if (string.IsNullOrWhiteSpace(_sitioWeb)) return;
        try { Process.Start(new ProcessStartInfo(_sitioWeb) { UseShellExecute = true }); } catch { }
    }

    /// <summary>La nueva instancia (con --reiniciar) termina a esta y vuelve a abrir el puerto.</summary>
    private void ReiniciarAgente()
    {
        Bitacora.Info("Reinicio", "Reinicio solicitado desde el panel / la bitácora.");
        _icono.Visible = false;
        Instancia.Reiniciar();
    }

    private void Salir()
    {
        Bitacora.Info("Cierre del agente", "Cerrado por el usuario desde el panel.");
        _temporizador.Stop();
        _icono.Visible = false;
        _bitacora?.Close();
        _panel.Dispose();
        _detener();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _temporizador.Dispose();
            _icono.Dispose();
            _iconoVerde.Dispose();
            _iconoAmarillo.Dispose();
        }
        base.Dispose(disposing);
    }

    private static string Recortar(string texto) => texto.Length <= 63 ? texto : texto[..63]; // límite de NotifyIcon.Text

    /// <summary>
    /// Solo para soporte: con la variable de entorno AGENTE_CAPTURA=&lt;carpeta&gt;, guarda una imagen del panel
    /// dibujado en memoria (sin mostrarlo y SIN capturar la pantalla del usuario).
    /// </summary>
    private async Task CapturaDiagnostico()
    {
        var carpeta = Environment.GetEnvironmentVariable("AGENTE_CAPTURA");
        if (string.IsNullOrWhiteSpace(carpeta)) return;
        await Task.Delay(3000); // espera el primer autodiagnóstico
        Capturar(_panel, Path.Combine(carpeta, "panel.png"));
        using var bitacora = new VentanaBitacora(_version) { StartPosition = FormStartPosition.Manual };
        Capturar(bitacora, Path.Combine(carpeta, "bitacora.png"));
    }

    private static void Capturar(Form ventana, string archivo)
    {
        try
        {
            // Se dibuja fuera del área visible y sin activarla: no aparece en pantalla ni se toma la pantalla.
            var posicion = ventana.Location;
            ventana.Location = new Point(-20000, -20000);
            ventana.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(ventana.Width, ventana.Height);
            ventana.DrawToBitmap(bmp, new Rectangle(Point.Empty, ventana.Size)); // solo la ventana del agente
            ventana.Hide();
            ventana.Location = posicion;
            bmp.Save(archivo, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (Exception ex)
        {
            Bitacora.Aviso("Captura de diagnóstico", $"No se pudo guardar {archivo}: {ex.Message}");
        }
    }
}
