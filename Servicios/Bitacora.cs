using System.Collections.Concurrent;

namespace AgenteFirma.Servicios;

public enum NivelBitacora { Info, Exito, Aviso, Error }

public sealed record EntradaBitacora(DateTime Hora, NivelBitacora Nivel, string Operacion, string Detalle);

/// <summary>
/// Bitácora de actividad del agente: lo que se ve en la ventana "Bitácora" y se guarda en el
/// registro diario (%LOCALAPPDATA%\AgenteFirma\logs). Pensada para diagnosticar problemas:
/// qué peticiones llegan, desde qué sitio, qué se firmó y por qué falló algo.
/// </summary>
public static class Bitacora
{
    private const int Maximo = 3000;
    private static readonly ConcurrentQueue<EntradaBitacora> _entradas = new();
    private static readonly object _candadoArchivo = new();

    /// <summary>Se dispara (en un hilo cualquiera) cada vez que se registra una entrada.</summary>
    public static event Action<EntradaBitacora>? Nueva;

    public static IReadOnlyList<EntradaBitacora> Entradas => _entradas.ToArray();

    public static void Info(string operacion, string detalle) => Registrar(NivelBitacora.Info, operacion, detalle);
    public static void Exito(string operacion, string detalle) => Registrar(NivelBitacora.Exito, operacion, detalle);
    public static void Aviso(string operacion, string detalle) => Registrar(NivelBitacora.Aviso, operacion, detalle);
    public static void Error(string operacion, string detalle) => Registrar(NivelBitacora.Error, operacion, detalle);

    public static void Registrar(NivelBitacora nivel, string operacion, string detalle)
    {
        var entrada = new EntradaBitacora(DateTime.Now, nivel, operacion, detalle);
        _entradas.Enqueue(entrada);
        while (_entradas.Count > Maximo && _entradas.TryDequeue(out _)) { }

        lock (_candadoArchivo)
        {
            try
            {
                File.AppendAllText(RegistroArchivoProvider.ArchivoDeHoy,
                    $"{entrada.Hora:yyyy-MM-dd HH:mm:ss.fff} [{nivel}] {operacion}: {detalle}{Environment.NewLine}");
            }
            catch { /* la bitácora nunca debe interrumpir una firma */ }
        }

        try { Nueva?.Invoke(entrada); } catch { /* ventana cerrándose */ }
    }

    public static void Limpiar()
    {
        while (_entradas.TryDequeue(out _)) { }
    }

    public static string Tamano(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.##} MB"
    };
}
