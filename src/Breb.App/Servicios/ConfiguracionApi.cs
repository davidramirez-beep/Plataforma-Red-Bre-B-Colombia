namespace Breb.App.Servicios;

/// <summary>
/// Dirección por defecto según el aparato. En un teléfono físico
/// <see cref="IpEnLaWifi"/> puede quedar vacía: Entrar deja escribir la URL
/// y la guarda, así que no hace falta recompilar cuando se conozca la IP.
/// </summary>
public static class ConfiguracionApi
{
    /// <summary>
    /// IP de la máquina donde corre la API, si ya se conoce al compilar.
    /// Vacía: el emulador usa 10.0.2.2 y el teléfono pide la URL en Entrar.
    /// </summary>
    public const string IpEnLaWifi = "";

    public static string Direccion =>
        EleccionDeDireccion.Elegir(IpEnLaWifi, EsAndroid, EsEmulador);

    public static bool FaltaConfigurar =>
        EleccionDeDireccion.Falta(IpEnLaWifi, EsAndroid, EsEmulador);

    public static string Descripcion =>
        EleccionDeDireccion.Describir(IpEnLaWifi, EsAndroid, EsEmulador);

    private static bool EsAndroid => DeviceInfo.Platform == DevicePlatform.Android;

    private static bool EsEmulador => DeviceInfo.DeviceType == DeviceType.Virtual;
}
