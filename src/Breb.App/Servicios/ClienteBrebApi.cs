using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Breb.App.Modelos;

namespace Breb.App.Servicios;

/// <summary>
/// Envuelve el HttpClient registrado en MauiProgram.cs. Un solo lugar que
/// sabe hablar con Breb.Cuentas; el ViewModel no conoce rutas ni JSON.
/// </summary>
public sealed class ClienteBrebApi(HttpClient http)
{
    public async Task<RespuestaToken> ObtenerTokenAsync(string usuario, string clave)
    {
        using var respuesta = await http.PostAsJsonAsync("/token", new SolicitudToken(usuario, clave));

        // 401 aquí es un camino esperado (clave mala), no una excepción de red.
        if (!respuesta.IsSuccessStatusCode)
            throw await ExcepcionDeApi.DesdeAsync(respuesta, "login");

        return await respuesta.Content.ReadFromJsonAsync<RespuestaToken>()
               ?? throw new InvalidOperationException("Respuesta de /token vacía.");
    }

    public async Task<RespuestaSaldo> ConsultarSaldoAsync(Guid cuentaId, string token)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"/cuentas/{cuentaId}/saldo");
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var respuesta = await http.SendAsync(solicitud);
        if (!respuesta.IsSuccessStatusCode)
            throw await ExcepcionDeApi.DesdeAsync(respuesta, "saldo");

        return await respuesta.Content.ReadFromJsonAsync<RespuestaSaldo>()
               ?? throw new InvalidOperationException("Respuesta de /saldo vacía.");
    }

    /// <summary>
    /// Dispara la transferencia: POST /cuentas/{id}/retener?montoUVB=&amp;transferenciaId=.
    ///
    /// Contrato real de Breb.Cuentas:
    ///   · el monto va en la query, no en el cuerpo;
    ///   · la política es Operador, o sea el claim de rol "operador"
    ///     (ana.operadora). Un token "consulta" responde 403, no 401;
    ///   · <paramref name="transferenciaId"/> la pone el cliente y se reutiliza
    ///     en cada reintento. Si el servidor ya la vio, responde la retención
    ///     original con repetida=true y no aparta otra vez;
    ///   · no existe otro endpoint que "transfiera con destino". Este es el
    ///     que publica FondosRetenidos y nace la saga. El abono lo confirma
    ///     después POST /transferencias/{id}/confirmar-abono, y solo con el
    ///     rol banco. Sin esa confirmación, a los 15 s la saga compensa y
    ///     el dinero vuelve. El conciliador de 5 minutos no interviene ahí.
    /// </summary>
    public async Task<RespuestaRetencion> RetenerAsync(
        Guid cuentaId, decimal montoUvb, string token, Guid transferenciaId)
    {
        var monto = Uri.EscapeDataString(montoUvb.ToString(CultureInfo.InvariantCulture));
        using var solicitud = new HttpRequestMessage(
            HttpMethod.Post,
            $"/cuentas/{cuentaId}/retener?montoUVB={monto}&transferenciaId={transferenciaId}");
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var respuesta = await http.SendAsync(solicitud);
        if (!respuesta.IsSuccessStatusCode)
            throw await ExcepcionDeApi.DesdeAsync(respuesta, "transferencia");

        return await respuesta.Content.ReadFromJsonAsync<RespuestaRetencion>()
               ?? throw new InvalidOperationException("Respuesta de /retener vacía.");
    }

    /// <summary>
    /// No mueve plata. Sirve para, al reabrir, saber si esa llave sigue
    /// pendiente, ya salió una vez o ya volvió.
    /// </summary>
    public async Task<RespuestaRetencion> ConsultarEstadoAsync(Guid transferenciaId, string token)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, $"/transferencias/{transferenciaId}");
        solicitud.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var respuesta = await http.SendAsync(solicitud);
        if (!respuesta.IsSuccessStatusCode)
            throw await ExcepcionDeApi.DesdeAsync(respuesta, "transferencia");

        return await respuesta.Content.ReadFromJsonAsync<RespuestaRetencion>()
               ?? throw new InvalidOperationException("Respuesta de estado vacía.");
    }
}
