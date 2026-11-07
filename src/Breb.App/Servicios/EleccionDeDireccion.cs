namespace Breb.App.Servicios;

/// <summary>
/// Elige la URL de la API sin preguntarle a MAUI en qué aparato corre.
/// <see cref="ConfiguracionApi"/> le pasa esos datos. Las pruebas llaman aquí.
/// </summary>
public static class EleccionDeDireccion
{
    public const int Puerto = 5080;
    public const string AliasDelEmulador = "10.0.2.2";
    public const string MismaMaquina = "127.0.0.1";

    public static bool Falta(string? ipWifi, bool esAndroid, bool esEmulador) =>
        esAndroid && !esEmulador && string.IsNullOrWhiteSpace(ipWifi);

    public static string Elegir(string? ipWifi, bool esAndroid, bool esEmulador)
    {
        if (!string.IsNullOrWhiteSpace(ipWifi))
            return $"http://{ipWifi.Trim()}:{Puerto}";
        if (esAndroid)
            return $"http://{AliasDelEmulador}:{Puerto}";
        return $"http://{MismaMaquina}:{Puerto}";
    }

    public static string Describir(string? ipWifi, bool esAndroid, bool esEmulador)
    {
        var direccion = Elegir(ipWifi, esAndroid, esEmulador);
        if (!string.IsNullOrWhiteSpace(ipWifi))
            return $"{direccion} · IP configurada";
        if (!esAndroid)
            return $"{direccion} · esta misma máquina";
        return esEmulador
            ? $"{direccion} · alias del anfitrión (solo emulador)"
            : $"{direccion} · teléfono físico sin IP. Escriba la de su servidor en Entrar.";
    }
}
