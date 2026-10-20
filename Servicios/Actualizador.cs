using System.Diagnostics;
using System.Net.Http.Json;

namespace AgenteFirma.Servicios;

/// <summary>
/// Busca una versión nueva del agente publicada en el sitio de firma
/// (&lt;SitioWeb&gt;/descargas/agente-version.json) y, si existe, descarga y ejecuta el instalador.
/// </summary>
public static class Actualizador
{
    private sealed record Publicacion(string Version, string? Archivo);

    public sealed record Resultado(bool HayNueva, string? VersionNueva, string? UrlInstalador, string Mensaje);

    public static async Task<Resultado> Buscar(string? sitioWeb, string versionActual)
    {
        if (string.IsNullOrWhiteSpace(sitioWeb))
            return new Resultado(false, null, null, "No hay un sitio de firma configurado (Agente:SitioWeb).");

        var url = $"{sitioWeb.TrimEnd('/')}/descargas/agente-version.json";
        Bitacora.Info("Buscar actualizaciones", $"Consultando {url} (versión instalada {versionActual}).");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var publicada = await http.GetFromJsonAsync<Publicacion>($"{url}?t={DateTime.Now.Ticks}");
            if (publicada == null || !Version.TryParse(publicada.Version, out var nueva))
                return Informar(new Resultado(false, null, null, "El sitio no informó una versión válida."), NivelBitacora.Aviso);

            if (nueva <= Version.Parse(versionActual))
                return Informar(new Resultado(false, publicada.Version, null,
                    $"Ya tiene la versión más reciente ({versionActual})."), NivelBitacora.Exito);

            var instalador = publicada.Archivo ?? "/descargas/AgenteFirmaSetup.exe";
            if (instalador.StartsWith('/')) instalador = sitioWeb.TrimEnd('/') + instalador;
            return Informar(new Resultado(true, publicada.Version, instalador,
                $"Hay una versión nueva: {publicada.Version} (instalada: {versionActual})."), NivelBitacora.Info);
        }
        catch (Exception ex)
        {
            return Informar(new Resultado(false, null, null,
                $"No se pudo consultar las actualizaciones: {ex.GetType().Name}: {ex.Message}"), NivelBitacora.Aviso);
        }
    }

    /// <summary>Descarga el instalador y lo ejecuta (el instalador cierra este agente y abre el nuevo).</summary>
    public static async Task Instalar(string urlInstalador, string version)
    {
        var destino = Path.Combine(Path.GetTempPath(), $"AgenteFirmaSetup-{version}.exe");
        Bitacora.Info("Actualización", $"Descargando {urlInstalador} en {destino}.");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
        await using (var origen = await http.GetStreamAsync(urlInstalador))
        await using (var archivo = File.Create(destino))
        {
            await origen.CopyToAsync(archivo);
        }
        Bitacora.Info("Actualización", $"Descarga completa ({Bitacora.Tamano(new FileInfo(destino).Length)}). Se inicia el instalador.");
        Process.Start(new ProcessStartInfo(destino) { UseShellExecute = true });
    }

    private static Resultado Informar(Resultado r, NivelBitacora nivel)
    {
        Bitacora.Registrar(nivel, "Buscar actualizaciones", r.Mensaje);
        return r;
    }
}
