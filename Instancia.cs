using System.Diagnostics;

namespace AgenteFirma;

/// <summary>
/// Control de la instancia única del agente: detecta si la instancia anterior está colgada,
/// la termina y permite reiniciar el agente (menú de la bandeja o acceso "Reiniciar" del menú Inicio).
/// </summary>
public static class Instancia
{
    public const string ArgumentoReiniciar = "--reiniciar";
    private const string NombreMutex = @"Local\AgenteFirmaDigital";

    /// <summary>
    /// Toma la instancia única. Si ya hay otra que responde, devuelve null (no hace falta otra).
    /// Si la otra no responde o se pidió reiniciar, la termina y toma su lugar.
    /// </summary>
    public static Mutex? Tomar(bool reiniciar, int puerto, string? origen)
    {
        if (reiniciar) TerminarOtras();

        var mutex = new Mutex(false, NombreMutex);
        if (Adquirir(mutex, TimeSpan.Zero)) return mutex;

        if (Responde(puerto, origen))
        {
            mutex.Dispose();
            return null; // ya hay un agente sano en ejecución
        }

        // La instancia anterior está colgada: se termina y se toma su lugar.
        TerminarOtras();
        if (Adquirir(mutex, TimeSpan.FromSeconds(10))) return mutex;
        mutex.Dispose();
        return null;
    }

    /// <summary>Lanza una instancia nueva con --reiniciar (que terminará a esta).</summary>
    public static void Reiniciar()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        Process.Start(new ProcessStartInfo(exe, ArgumentoReiniciar) { UseShellExecute = false });
    }

    private static bool Adquirir(Mutex mutex, TimeSpan espera)
    {
        try { return mutex.WaitOne(espera); }
        catch (AbandonedMutexException) { return true; } // la instancia anterior terminó sin liberarlo
    }

    private static void TerminarOtras()
    {
        var actual = Environment.ProcessId;
        foreach (var p in Process.GetProcessesByName("AgenteFirma").Where(p => p.Id != actual))
        {
            try
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
            catch { /* de otro usuario o ya terminado */ }
            finally { p.Dispose(); }
        }
    }

    private static bool Responde(int puerto, string? origen)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            using var pedido = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{puerto}/api/estado");
            if (!string.IsNullOrEmpty(origen)) pedido.Headers.Add("Origin", origen);
            using var respuesta = http.Send(pedido);
            return respuesta.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
