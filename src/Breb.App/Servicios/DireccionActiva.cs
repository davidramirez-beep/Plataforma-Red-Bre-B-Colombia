namespace Breb.App.Servicios;

/// <summary>
/// La URL que el HttpClient usa en cada llamada. Cambia si en Entrar
/// escriben otra, sin crear un cliente nuevo.
/// </summary>
public sealed class DireccionActiva
{
    public const string ClaveAlmacen = "breb.api.base";

    public DireccionActiva(string inicial)
    {
        Base = Normalizar(inicial);
        Nota = Base;
    }

    public string Base { get; private set; }

    public string Nota { get; set; }

    public bool FaltaLaIpDelTelefono { get; set; }

    public void Aplicar(string texto) => Base = Normalizar(texto);

    public static string Normalizar(string texto)
    {
        var t = (texto ?? string.Empty).Trim();
        while (t.EndsWith('/') && !t.EndsWith("://", StringComparison.Ordinal))
            t = t[..^1];

        if (t.Length == 0)
            throw new ArgumentException(
                "Escriba la dirección de la API. En el teléfono es http://IP-DEL-SERVIDOR:5080.");

        if (!t.Contains("://", StringComparison.Ordinal))
            t = "http://" + t;

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException(
                "La dirección de la API no es válida. Ejemplo: http://192.168.1.20:5080");

        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new ArgumentException("La dirección tiene que empezar por http:// o https://.");

        var puerto = uri.IsDefaultPort ? EleccionDeDireccion.Puerto : uri.Port;
        return $"{uri.Scheme}://{uri.Host}:{puerto}";
    }
}
