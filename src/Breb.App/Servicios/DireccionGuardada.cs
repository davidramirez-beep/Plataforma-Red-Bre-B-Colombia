namespace Breb.App.Servicios;

/// <summary>
/// La URL de la API vive en la bóveda, aparte del JWT. Cerrar sesión no la borra.
/// </summary>
public sealed class DireccionGuardada(IBovedaSegura boveda, DireccionActiva activa)
{
    public async Task CargarAsync()
    {
        var guardada = await boveda.LeerAsync(DireccionActiva.ClaveAlmacen);
        if (string.IsNullOrWhiteSpace(guardada)) return;

        try { activa.Aplicar(guardada); }
        catch (ArgumentException) { /* una URL vieja ilegible no impide entrar */ }
    }

    public async Task GuardarAsync(string texto)
    {
        var normalizada = DireccionActiva.Normalizar(texto);
        await boveda.GuardarAsync(DireccionActiva.ClaveAlmacen, normalizada);
        activa.Aplicar(normalizada);
    }
}
