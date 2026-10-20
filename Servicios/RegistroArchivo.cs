using System.Collections.Concurrent;

namespace AgenteFirma.Servicios;

/// <summary>
/// Registro en archivo (la aplicación no tiene consola): %LOCALAPPDATA%\AgenteFirma\logs\agente-AAAAMMDD.log.
/// Conserva los últimos 14 días.
/// </summary>
public sealed class RegistroArchivoProvider : ILoggerProvider
{
    public static string Carpeta { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgenteFirma", "logs");

    private readonly ConcurrentDictionary<string, RegistroArchivo> _registros = new();
    private readonly object _candado = new();

    public RegistroArchivoProvider()
    {
        Directory.CreateDirectory(Carpeta);
        foreach (var viejo in new DirectoryInfo(Carpeta).GetFiles("agente-*.log")
                     .Where(f => f.LastWriteTime < DateTime.Now.AddDays(-14)))
        {
            try { viejo.Delete(); } catch { /* en uso: se intentará la próxima vez */ }
        }
    }

    public static string ArchivoDeHoy => Path.Combine(Carpeta, $"agente-{DateTime.Now:yyyyMMdd}.log");

    public ILogger CreateLogger(string categoria) => _registros.GetOrAdd(categoria, c => new RegistroArchivo(c, this));

    internal void Escribir(string linea)
    {
        lock (_candado)
        {
            try { File.AppendAllText(ArchivoDeHoy, linea + Environment.NewLine); }
            catch { /* el registro nunca debe interrumpir una firma */ }
        }
    }

    public void Dispose() { }

    private sealed class RegistroArchivo(string categoria, RegistroArchivoProvider proveedor) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel nivel) =>
            nivel >= (categoria.StartsWith("Microsoft", StringComparison.Ordinal) ? LogLevel.Warning : LogLevel.Information);

        public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? ex, Func<TState, Exception?, string> formato)
        {
            if (!IsEnabled(nivel)) return;
            var linea = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{nivel}] {categoria}: {formato(estado, ex)}";
            if (ex != null) linea += Environment.NewLine + ex;
            proveedor.Escribir(linea);
        }
    }
}
