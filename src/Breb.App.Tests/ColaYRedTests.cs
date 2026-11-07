using System.Net;
using Breb.App.Modelos;
using Breb.App.Servicios;
using Breb.App.Tests.Apoyo;
using Breb.App.ViewModels;

namespace Breb.App.Tests;

public class ColaOperacionesTests
{
    [Fact]
    public async Task Sobrevive_a_otra_instancia_sobre_la_misma_carpeta()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "breb-cola", Guid.NewGuid().ToString("N"));
        var llave = Guid.NewGuid();
        var primera = new ColaOperaciones(carpeta);
        await primera.EncolarAsync(new OperacionPendiente(
            llave, Guid.Parse("11111111-1111-1111-1111-111111111111"), 10m, DateTime.UtcNow));

        var segunda = new ColaOperaciones(carpeta);
        var pendiente = Assert.Single(await segunda.PendientesAsync());
        Assert.Equal(llave, pendiente.Llave);
        Assert.Equal(10m, pendiente.MontoUVB);
        Assert.False(File.Exists(primera.Ruta + ".tmp"));
    }

    [Fact]
    public async Task La_misma_llave_no_se_encola_dos_veces()
    {
        var cola = new ColaOperaciones(Carpeta());
        var op = new OperacionPendiente(Guid.NewGuid(), Guid.NewGuid(), 10m, DateTime.UtcNow);
        await cola.EncolarAsync(op);
        await cola.EncolarAsync(op);
        Assert.Single(await cola.PendientesAsync());
    }

    [Fact]
    public async Task Confirmar_la_saca_y_un_archivo_corrupto_no_tumba_la_lectura()
    {
        var cola = new ColaOperaciones(Carpeta());
        var llave = Guid.NewGuid();
        await cola.EncolarAsync(new OperacionPendiente(llave, Guid.NewGuid(), 5m, DateTime.UtcNow));
        await cola.RegistrarIntentoAsync(llave);
        Assert.Equal(1, (await cola.PendientesAsync())[0].Intentos);

        await cola.ConfirmarAsync(llave);
        Assert.Empty(await cola.PendientesAsync());

        await File.WriteAllTextAsync(cola.Ruta, "[{\"Llave\":\"esto no es un guid");
        Assert.Empty(await cola.PendientesAsync());

        await cola.EncolarAsync(new OperacionPendiente(Guid.NewGuid(), Guid.NewGuid(), 1m, DateTime.UtcNow));
        Assert.Single(await cola.PendientesAsync());
    }

    [Fact]
    public async Task Encolar_en_paralelo_no_pierde_operaciones()
    {
        var cola = new ColaOperaciones(Carpeta());
        var tareas = Enumerable.Range(0, 30).Select(_ =>
            cola.EncolarAsync(new OperacionPendiente(Guid.NewGuid(), Guid.NewGuid(), 10m, DateTime.UtcNow)));
        await Task.WhenAll(tareas);
        Assert.Equal(30, (await cola.PendientesAsync()).Count);
    }

    private static string Carpeta() =>
        Path.Combine(Path.GetTempPath(), "breb-cola", Guid.NewGuid().ToString("N"));
}

public class DireccionApiTests
{
    [Fact]
    public void El_emulador_usa_el_alias_y_el_telefono_sin_ip_se_nota()
    {
        Assert.Equal("http://10.0.2.2:5080", EleccionDeDireccion.Elegir(null, esAndroid: true, esEmulador: true));
        Assert.False(EleccionDeDireccion.Falta(null, esAndroid: true, esEmulador: true));
        Assert.True(EleccionDeDireccion.Falta("", esAndroid: true, esEmulador: false));
        Assert.Equal("http://192.168.20.29:5080", EleccionDeDireccion.Elegir("192.168.20.29", true, false));
        Assert.Equal("http://127.0.0.1:5080", EleccionDeDireccion.Elegir(null, esAndroid: false, esEmulador: false));
    }

    [Fact]
    public void La_direccion_escrita_en_entrar_se_normaliza_al_puerto_5080()
    {
        Assert.Equal("http://192.168.8.8:5080", DireccionActiva.Normalizar("192.168.8.8"));
        Assert.Equal("https://192.168.8.8:443", DireccionActiva.Normalizar("https://192.168.8.8"));
        Assert.Equal("http://10.1.2.3:5051", DireccionActiva.Normalizar("http://10.1.2.3:5051/"));
        Assert.Throws<ArgumentException>(() => DireccionActiva.Normalizar("   "));
        Assert.Throws<ArgumentException>(() => DireccionActiva.Normalizar("http://"));
        Assert.Throws<ArgumentException>(() => DireccionActiva.Normalizar("ftp://10.0.0.1"));
    }

    [Fact]
    public async Task El_handler_habla_con_la_url_que_quedo_guardada()
    {
        var direccion = new DireccionActiva("http://10.0.2.2:5080");
        var interno = new HandlerFalso();
        interno.Encolar(HttpStatusCode.OK, "{}");
        var ajuste = new AjusteDeDireccion(direccion) { InnerHandler = interno };
        using var http = new HttpClient(ajuste) { BaseAddress = new Uri("http://10.0.2.2:5080") };

        direccion.Aplicar("http://20.1.2.3:5080");
        await http.GetAsync("/swagger/index.html");

        Assert.Equal("http://20.1.2.3:5080/swagger/index.html", interno.Solicitudes[0].UriAbsoluta);
    }

    [Fact]
    public async Task Entrar_guarda_la_url_nueva_y_rechaza_una_invalida()
    {
        var (vm, boveda, _, nav, http) = Fabrica.Login();
        vm.DireccionTexto = "hola mundo";
        http.Encolar(HttpStatusCode.OK,
            """{"token":"abc","expira":"2026-10-21T12:00:00Z","tipo":"Bearer","rol":"operador","duracionMinutos":15}""");

        await vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal("error", vm.Tono);
        Assert.Empty(nav.RutasVisitadas);
        Assert.DoesNotContain(DireccionActiva.ClaveAlmacen, boveda.Datos.Keys);

        vm.DireccionTexto = "192.168.8.8:5080";
        await vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal("http://192.168.8.8:5080", boveda.Datos[DireccionActiva.ClaveAlmacen]);
        Assert.Equal(Rutas.Saldo, Assert.Single(nav.RutasVisitadas));
    }
}

public class OfflineIdempotenciaTests
{
    private static readonly SesionGuardada Operadora = new("abc", "operador", "ana.operadora");

    private const string SaldoTrasRetener =
        """{"cuentaId":"11111111-1111-1111-1111-111111111111","saldoDisponible":4990,"saldoRetenido":10}""";

    [Fact]
    public async Task Sin_red_la_deja_en_disco_y_el_reintento_repite_la_llave()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "breb-cola", Guid.NewGuid().ToString("N"));
        var (vm, _, _, http) = Fabrica.Transferencia(Operadora, carpeta);
        http.Falla = new HttpRequestException("Network is unreachable");

        await vm.TransferirCommand.ExecuteAsync(null);

        var guardada = Assert.Single(await vm.Cola.PendientesAsync());
        Assert.Equal(1, guardada.Intentos);
        Assert.Contains("quedó guardada", vm.Estado);
        Assert.Contains("misma llave", vm.Estado);
        var llave = Guid.Parse(http.Solicitudes[0].Ruta.Split("transferenciaId=")[1]);
        Assert.Equal(llave, guardada.Llave);

        // Otro proceso, la misma carpeta: es cerrar la app y volver a abrirla.
        var sesionVigente = new SesionGuardada(
            JwtDePrueba.ConExpira(DateTimeOffset.UtcNow.AddMinutes(10)), "operador", "ana.operadora");
        var (otra, _, _, http2) = Fabrica.Transferencia(sesionVigente, carpeta);
        http2.Encolar(HttpStatusCode.OK,
            $$"""{"transferenciaId":"{{llave}}","montoUVB":10,"repetida":true,"estado":"pendiente"}""");
        http2.Encolar(HttpStatusCode.OK, SaldoTrasRetener);

        await otra.ReintentarPendientesCommand.ExecuteAsync(null);

        Assert.Contains($"transferenciaId={llave}", http2.Solicitudes[0].Ruta);
        Assert.Single(await otra.Cola.PendientesAsync());
        Assert.Contains("no apartó otra vez", otra.ResultadoTransferencia);
        Assert.Contains("15 segundos", otra.ResultadoTransferencia);
        Assert.DoesNotContain("completada", otra.ResultadoTransferencia);
        Assert.Equal("ok", otra.Tono);

        http2.Encolar(HttpStatusCode.OK,
            $$"""{"transferenciaId":"{{llave}}","montoUVB":10,"repetida":true,"estado":"liberada"}""");
        http2.Encolar(HttpStatusCode.OK,
            """{"cuentaId":"11111111-1111-1111-1111-111111111111","saldoDisponible":5000,"saldoRetenido":0}""");
        await otra.ReintentarPendientesCommand.ExecuteAsync(null);

        Assert.Empty(await otra.Cola.PendientesAsync());
        Assert.Equal("error", otra.Tono);
        Assert.Contains("compensó", otra.ResultadoTransferencia);
        Assert.Contains("volvió", otra.ResultadoTransferencia);
        Assert.Contains("No es una transferencia completada", otra.ResultadoTransferencia);
        Assert.DoesNotContain("plata quedó apartada", otra.Estado);
    }

    [Fact]
    public async Task Sin_sesion_el_reintento_no_borra_la_pendiente()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "breb-cola", Guid.NewGuid().ToString("N"));
        var (vm, _, _, http) = Fabrica.Transferencia(Operadora, carpeta);
        http.Falla = new TaskCanceledException();
        await vm.TransferirCommand.ExecuteAsync(null);
        var llave = (await vm.Cola.PendientesAsync())[0].Llave;

        var (sinSesion, _, nav, http2) = Fabrica.Transferencia(null, carpeta);
        await sinSesion.ReintentarPendientesCommand.ExecuteAsync(null);

        Assert.Empty(http2.Solicitudes);
        Assert.Contains("no se pierde", sinSesion.Estado);
        Assert.Equal(llave, (await sinSesion.Cola.PendientesAsync())[0].Llave);
        Assert.Empty(nav.RutasVisitadas);
    }

    [Fact]
    public async Task Al_abrir_con_sesion_vigente_reenvia_lo_que_sobrevivio()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "breb-cola", Guid.NewGuid().ToString("N"));
        var cola = new ColaOperaciones(carpeta);
        var llave = Guid.NewGuid();
        await cola.EncolarAsync(new OperacionPendiente(
            llave, Guid.Parse("11111111-1111-1111-1111-111111111111"), 10m, DateTime.UtcNow));

        var sesion = new SesionGuardada(
            JwtDePrueba.ConExpira(DateTimeOffset.UtcNow.AddMinutes(10)), "operador", "ana.operadora");
        var (vm, _, _, http) = Fabrica.Transferencia(sesion, carpeta);
        http.Encolar(HttpStatusCode.OK,
            $$"""{"transferenciaId":"{{llave}}","montoUVB":10,"repetida":true,"estado":"liquidada"}""");
        http.Encolar(HttpStatusCode.OK, SaldoTrasRetener);

        await vm.PrepararCommand.ExecuteAsync(null);

        Assert.Contains($"transferenciaId={llave}", Assert.Single(http.Solicitudes, s => s.Ruta.Contains("/retener")).Ruta);
        Assert.Empty(await vm.Cola.PendientesAsync());
        Assert.Contains("una vez", vm.ResultadoTransferencia);
        Assert.Contains("No se hizo otra", vm.ResultadoTransferencia);
        Assert.Equal("ok", vm.Tono);
    }

    [Fact]
    public async Task Un_401_no_borra_la_cola_y_al_reabrir_repite_la_llave()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "breb-cola", Guid.NewGuid().ToString("N"));
        var (vm, _, _, http) = Fabrica.Transferencia(Operadora, carpeta);
        http.Encolar(HttpStatusCode.Unauthorized, "");

        await vm.TransferirCommand.ExecuteAsync(null);

        var llave = Assert.Single(await vm.Cola.PendientesAsync()).Llave;
        Assert.Contains("sesión venció", vm.Estado);

        var sesion = new SesionGuardada(
            JwtDePrueba.ConExpira(DateTimeOffset.UtcNow.AddMinutes(10)), "operador", "ana.operadora");
        var (reabierta, _, _, http2) = Fabrica.Transferencia(sesion, carpeta);
        http2.Encolar(HttpStatusCode.OK,
            $$"""{"transferenciaId":"{{llave}}","montoUVB":10,"repetida":true,"estado":"pendiente"}""");
        http2.Encolar(HttpStatusCode.OK, SaldoTrasRetener);

        await reabierta.PrepararCommand.ExecuteAsync(null);

        Assert.Contains($"transferenciaId={llave}", http2.Solicitudes[0].Ruta);
        Assert.Equal(llave, Assert.Single(await reabierta.Cola.PendientesAsync()).Llave);
    }
}
