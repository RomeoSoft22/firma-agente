using System.Diagnostics;
using System.Reflection;
using AgenteFirma.Servicios;

namespace AgenteFirma;

/// <summary>Ícono en la bandeja del sistema: indica que el agente está activo y permite cerrarlo.</summary>
public sealed class Bandeja : ApplicationContext
{
    private readonly NotifyIcon _icono;
    private readonly Action _detener;

    public Bandeja(int puerto, string? sitioWeb, Action detener)
    {
        _detener = detener;
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem($"Agente de Firma activo (puerto {puerto})") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        if (!string.IsNullOrWhiteSpace(sitioWeb))
            menu.Items.Add("Abrir el sitio de firma", null, (_, _) => Abrir(sitioWeb!));
        menu.Items.Add("Ver registro", null, (_, _) => Abrir(RegistroArchivoProvider.Carpeta));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Salir());

        _icono = new NotifyIcon
        {
            Icon = CargarIcono(),
            Text = "Agente de Firma Digital",
            ContextMenuStrip = menu,
            Visible = true
        };
        if (!string.IsNullOrWhiteSpace(sitioWeb))
            _icono.DoubleClick += (_, _) => Abrir(sitioWeb!);
    }

    public void Notificar(string titulo, string texto) =>
        _icono.ShowBalloonTip(4000, titulo, texto, ToolTipIcon.Info);

    private void Salir()
    {
        _icono.Visible = false;
        _detener();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _icono.Dispose();
        base.Dispose(disposing);
    }

    private static void Abrir(string destino)
    {
        try { Process.Start(new ProcessStartInfo(destino) { UseShellExecute = true }); }
        catch { /* sin navegador / carpeta inexistente */ }
    }

    private static Icon CargarIcono()
    {
        using var recurso = Assembly.GetExecutingAssembly().GetManifestResourceStream("AgenteFirma.firma.ico");
        return recurso != null ? new Icon(recurso) : SystemIcons.Application;
    }
}
