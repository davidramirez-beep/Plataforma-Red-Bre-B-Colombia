using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Breb.Cuentas.Dominio;
using Breb.Cuentas.Infraestructura;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Breb.Cuentas.Integracion;

/// <summary>
/// PostgreSQL y RabbitMQ reales. Las variables no tienen valor por defecto:
/// BREB_TEST_CONNECTION, BREB_TEST_RABBIT_USER, BREB_TEST_RABBIT_PASSWORD.
/// </summary>
public class CarreraYIdempotenciaTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fixture;

    public CarreraYIdempotenciaTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task La_misma_llave_en_paralelo_aparta_una_sola_vez_y_sobrevive_al_reinicio()
    {
        var cuenta = await _fixture.CrearCuentaAsync(5000m);
        var llave = Guid.NewGuid();
        var token = await _fixture.TokenAsync("ana.operadora", "Operadora-2026");

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            _fixture.RetenerAsync(token, cuenta, 10m, llave)));

        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.Codigo));
        Assert.Single(respuestas, r => r.Repetida == false);
        Assert.Equal(7, respuestas.Count(r => r.Repetida));
        Assert.All(respuestas, r => Assert.Equal(10m, r.Monto));

        var saldo = await _fixture.SaldoAsync(token, cuenta);
        Assert.Equal(4990m, saldo.Disponible);
        Assert.Equal(10m, saldo.Retenido);
        Assert.Equal(1, await _fixture.ContarRetencionesAsync(llave));

        await _fixture.ReiniciarAsync();

        var otra = await _fixture.RetenerAsync(
            await _fixture.TokenAsync("ana.operadora", "Operadora-2026"),
            cuenta, 999m, llave);
        Assert.Equal(HttpStatusCode.OK, otra.Codigo);
        Assert.True(otra.Repetida);
        Assert.Equal(10m, otra.Monto);
        Assert.Equal("pendiente", otra.Estado);

        saldo = await _fixture.SaldoAsync(await _fixture.TokenAsync("luis.consulta", "Consulta-2026"), cuenta);
        Assert.Equal(4990m, saldo.Disponible);
        Assert.Equal(10m, saldo.Retenido);
    }

    [Fact]
    public async Task Ocho_transferencias_concurrentes_se_confirman_una_vez_cada_una()
    {
        var cuenta = await _fixture.CrearCuentaAsync(5000m);
        var operador = await _fixture.TokenAsync("ana.operadora", "Operadora-2026");
        var banco = await _fixture.TokenAsync("core-bancario", "CoreBancario-2026");
        var llaves = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();

        var altas = await Task.WhenAll(llaves.Select(id => _fixture.RetenerAsync(operador, cuenta, 10m, id)));
        Assert.All(altas, r => Assert.Equal(HttpStatusCode.OK, r.Codigo));
        Assert.All(altas, r => Assert.False(r.Repetida));

        var saldo = await _fixture.SaldoAsync(operador, cuenta);
        Assert.Equal(4920m, saldo.Disponible);
        Assert.Equal(80m, saldo.Retenido);

        // Sin esperar: la confirmación puede llegar antes de que la saga exista.
        var confirmaciones = await Task.WhenAll(llaves.Select(id => _fixture.ConfirmarAsync(banco, id)));
        Assert.All(confirmaciones, c => Assert.Equal(HttpStatusCode.Accepted, c));

        var liquidadas = await _fixture.EsperarAsync(async () =>
        {
            var filas = await _fixture.FilasAsync(llaves);
            return filas.Count == 8 && filas.All(f => f.Liquidada && !f.Liberada);
        }, TimeSpan.FromSeconds(20));

        Assert.True(liquidadas, "Alguna transferencia concurrente no quedó liquidada: " +
                                await _fixture.DescribirAsync(llaves));

        saldo = await _fixture.SaldoAsync(operador, cuenta);
        Assert.Equal(4920m, saldo.Disponible);
        Assert.Equal(0m, saldo.Retenido);

        var reintentos = await Task.WhenAll(llaves.Select(id => _fixture.RetenerAsync(operador, cuenta, 10m, id)));
        Assert.All(reintentos, r =>
        {
            Assert.True(r.Repetida);
            Assert.Equal("liquidada", r.Estado);
        });
        saldo = await _fixture.SaldoAsync(operador, cuenta);
        Assert.Equal(4920m, saldo.Disponible);
        Assert.Equal(0m, saldo.Retenido);
    }

    [Fact]
    public async Task Sin_confirmacion_a_los_15s_compensa_y_el_reintento_no_la_da_por_exitosa()
    {
        var cuenta = await _fixture.CrearCuentaAsync(5000m);
        var operador = await _fixture.TokenAsync("ana.operadora", "Operadora-2026");
        var llave = Guid.NewGuid();

        var alta = await _fixture.RetenerAsync(operador, cuenta, 20m, llave);
        Assert.False(alta.Repetida);
        Assert.Equal("pendiente", alta.Estado);

        var compenso = await _fixture.EsperarAsync(async () =>
        {
            var fila = Assert.Single(await _fixture.FilasAsync(new[] { llave }));
            return fila.Liberada && !fila.Liquidada;
        }, TimeSpan.FromSeconds(30));

        Assert.True(compenso, "La saga no compensó dentro de la ventana oficial de 15 s.");

        var saldo = await _fixture.SaldoAsync(operador, cuenta);
        Assert.Equal(5000m, saldo.Disponible);
        Assert.Equal(0m, saldo.Retenido);

        var reintento = await _fixture.RetenerAsync(operador, cuenta, 20m, llave);
        Assert.True(reintento.Repetida);
        Assert.Equal("liberada", reintento.Estado);
        Assert.Equal(20m, reintento.Monto);

        saldo = await _fixture.SaldoAsync(operador, cuenta);
        Assert.Equal(5000m, saldo.Disponible);
        Assert.Equal(0m, saldo.Retenido);
        Assert.Equal(1, await _fixture.ContarRetencionesAsync(llave));
    }
}

public sealed class ApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private readonly string _connection;
    private readonly string _rabbitUser;
    private readonly string _rabbitPassword;

    public ApiFixture()
    {
        _connection = Exigir("BREB_TEST_CONNECTION");
        _rabbitUser = Exigir("BREB_TEST_RABBIT_USER");
        _rabbitPassword = Exigir("BREB_TEST_RABBIT_PASSWORD");
    }

    public async Task InitializeAsync() => _factory = await ArrancarAsync();

    public async Task ReiniciarAsync()
    {
        await _factory.DisposeAsync();
        _factory = await ArrancarAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    public async Task<Guid> CrearCuentaAsync(decimal saldo)
    {
        var id = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasDbContext>();
        db.Cuentas.Add(new Cuenta(id, saldo));
        await db.SaveChangesAsync();
        return id;
    }

    public async Task<string> TokenAsync(string usuario, string clave)
    {
        var respuesta = await _factory.CreateClient().PostAsJsonAsync("/token", new { usuario, clave });
        respuesta.EnsureSuccessStatusCode();
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("token").GetString()!;
    }

    public async Task<RetencionVista> RetenerAsync(string token, Guid cuenta, decimal monto, Guid llave)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"/cuentas/{cuenta}/retener?montoUVB={monto.ToString(System.Globalization.CultureInfo.InvariantCulture)}&transferenciaId={llave}");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var respuesta = await _factory.CreateClient().SendAsync(req);
        var cuerpo = await respuesta.Content.ReadAsStringAsync();
        if (!respuesta.IsSuccessStatusCode)
            return new RetencionVista(respuesta.StatusCode, false, 0, cuerpo, null);

        var json = JsonDocument.Parse(cuerpo).RootElement;
        return new RetencionVista(
            respuesta.StatusCode,
            json.GetProperty("repetida").GetBoolean(),
            json.GetProperty("montoUVB").GetDecimal(),
            json.GetProperty("estado").GetString(),
            json.GetProperty("transferenciaId").GetGuid());
    }

    public async Task<HttpStatusCode> ConfirmarAsync(string token, Guid llave)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/transferencias/{llave}/confirmar-abono");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var respuesta = await _factory.CreateClient().SendAsync(req);
        return respuesta.StatusCode;
    }

    public async Task<(decimal Disponible, decimal Retenido)> SaldoAsync(string token, Guid cuenta)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"/cuentas/{cuenta}/saldo");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        var respuesta = await _factory.CreateClient().SendAsync(req);
        respuesta.EnsureSuccessStatusCode();
        var json = await respuesta.Content.ReadFromJsonAsync<JsonElement>();
        return (json.GetProperty("saldoDisponible").GetDecimal(), json.GetProperty("saldoRetenido").GetDecimal());
    }

    public async Task<int> ContarRetencionesAsync(Guid llave)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasDbContext>();
        return await db.Retenciones.CountAsync(r => r.TransferenciaId == llave);
    }

    public async Task<List<(bool Liberada, bool Liquidada)>> FilasAsync(IEnumerable<Guid> llaves)
    {
        var ids = llaves.ToArray();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CuentasDbContext>();
        var filas = await db.Retenciones.AsNoTracking()
            .Where(r => ids.Contains(r.TransferenciaId))
            .ToListAsync();
        return filas.Select(r => (r.Liberada, r.Liquidada)).ToList();
    }

    public async Task<string> DescribirAsync(IEnumerable<Guid> llaves)
    {
        var filas = await FilasAsync(llaves);
        return string.Join("; ", filas.Select(f => $"L={f.Liberada} Q={f.Liquidada}"));
    }

    public async Task<bool> EsperarAsync(Func<Task<bool>> listo, TimeSpan maximo)
    {
        var hasta = DateTime.UtcNow + maximo;
        while (DateTime.UtcNow < hasta)
        {
            if (await listo()) return true;
            await Task.Delay(400);
        }
        return false;
    }

    private Task<WebApplicationFactory<Program>> ArrancarAsync()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__Cuentas", _connection);
        Environment.SetEnvironmentVariable("RabbitMq__Host", "127.0.0.1");
        Environment.SetEnvironmentVariable("RabbitMq__Port", "5673");
        Environment.SetEnvironmentVariable("RabbitMq__VirtualHost", "/");
        Environment.SetEnvironmentVariable("RabbitMq__User", _rabbitUser);
        Environment.SetEnvironmentVariable("RabbitMq__Password", _rabbitPassword);
        Environment.SetEnvironmentVariable("Conciliacion__Habilitada", "false");

        var factory = new WebApplicationFactory<Program>();
        _ = factory.CreateClient();
        return Task.FromResult(factory);
    }

    private static string Exigir(string nombre)
    {
        var valor = Environment.GetEnvironmentVariable(nombre);
        if (string.IsNullOrWhiteSpace(valor))
            throw new InvalidOperationException(
                $"Falta {nombre}. Esta prueba habla con PostgreSQL y RabbitMQ reales y no trae claves.");
        return valor;
    }

    public readonly record struct RetencionVista(
        HttpStatusCode Codigo, bool Repetida, decimal Monto, string? Estado, Guid? Id);
}
