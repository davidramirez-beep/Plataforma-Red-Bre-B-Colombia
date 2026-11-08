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
                "Escriba la dirección de la API. En el teléfono es https://IP-DEL-SERVIDOR.");

        if (!t.Contains("://", StringComparison.Ordinal))
            t = "http://" + t;

        if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException(
                "La dirección de la API no es válida. Ejemplo: https://192.168.1.20");

        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new ArgumentException("La dirección tiene que empezar por http:// o https://.");

        // Solo cuenta el puerto escrito en el texto. Uri.Port no: con una IP
        // puede informar un puerto que la persona no puso, y el 5080 del
        // emulador se colaba en un https.
        var puerto = PuertoEscrito(t, uri)
                     ?? (uri.Scheme == "https" ? 443 : EleccionDeDireccion.Puerto);
        if (puerto is < 1 or > 65535)
            throw new ArgumentException("El puerto de la API no es válido.");

        return $"{uri.Scheme}://{uri.IdnHost}:{puerto}";
    }

    private static int? PuertoEscrito(string texto, Uri uri)
    {
        var marca = texto.IndexOf("://", StringComparison.Ordinal);
        if (marca < 0) return null;
        var resto = texto[(marca + 3)..];

        if (resto.StartsWith('['))
        {
            var fin = resto.IndexOf(']');
            if (fin < 0 || fin + 1 >= resto.Length || resto[fin + 1] != ':')
                return null;
            return LeerPuerto(resto[(fin + 2)..]);
        }

        var host = resto.StartsWith(uri.IdnHost, StringComparison.OrdinalIgnoreCase)
            ? uri.IdnHost
            : uri.Host;
        if (!resto.StartsWith(host, StringComparison.OrdinalIgnoreCase))
            return null;

        var despues = resto[host.Length..];
        if (!despues.StartsWith(':'))
            return null;
        return LeerPuerto(despues[1..]);
    }

    private static int? LeerPuerto(string despues)
    {
        var corte = despues.IndexOfAny(['/', '?', '#']);
        var numero = corte >= 0 ? despues[..corte] : despues;
        if (numero.Length == 0) return null;
        return int.TryParse(numero, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var puerto)
            ? puerto
            : null;
    }
}
