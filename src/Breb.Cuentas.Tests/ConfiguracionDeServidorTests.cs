using Breb.Cuentas.Infraestructura;
using Xunit;

namespace Breb.Cuentas.Tests;

public class ConfiguracionDeServidorTests
{
    [Fact]
    public void Development_sin_configuracion_usa_el_laboratorio()
    {
        var cadena = ConfiguracionDeServidor.CadenaDeCuentas(null, desarrollo: true);
        Assert.Contains("Port=5433", cadena);
        Assert.Contains(ConfiguracionDeServidor.PasswordPostgresDeLaboratorio, cadena);

        var rabbit = ConfiguracionDeServidor.Rabbit(null, null, null, null, null, desarrollo: true);
        Assert.Equal("localhost", rabbit.Host);
        Assert.Equal((ushort)5673, rabbit.Port);
        Assert.Equal("guest", rabbit.User);
    }

    [Fact]
    public void Fuera_de_Development_exige_cadena_y_rechaza_la_de_laboratorio()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConfiguracionDeServidor.CadenaDeCuentas(null, desarrollo: false));
        Assert.Contains("ConnectionStrings__Cuentas", ex.Message);

        ex = Assert.Throws<InvalidOperationException>(() =>
            ConfiguracionDeServidor.CadenaDeCuentas(
                "Host=127.0.0.1;Password=dev_only_password;", desarrollo: false));
        Assert.Contains("laboratorio", ex.Message);

        var propia = ConfiguracionDeServidor.CadenaDeCuentas(
            "Host=127.0.0.1;Port=5433;Password=una-clave-larga;", desarrollo: false);
        Assert.Contains("una-clave-larga", propia);
    }

    [Fact]
    public void Fuera_de_Development_Rabbit_no_acepta_guest_ni_un_puerto_raro()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ConfiguracionDeServidor.Rabbit(null, null, null, null, null, desarrollo: false));

        Assert.Throws<InvalidOperationException>(() =>
            ConfiguracionDeServidor.Rabbit("127.0.0.1", "5673", "/", "guest", "otra-clave-larga", desarrollo: false));

        Assert.Throws<InvalidOperationException>(() =>
            ConfiguracionDeServidor.Rabbit("127.0.0.1", "no-es-puerto", "/", "breb", "otra-clave-larga", desarrollo: false));

        var ok = ConfiguracionDeServidor.Rabbit(
            "127.0.0.1", "5673", "/", "breb", "otra-clave-larga", desarrollo: false);
        Assert.Equal("127.0.0.1", ok.Host);
        Assert.Equal("breb", ok.User);
        Assert.Equal("otra-clave-larga", ok.Password);
    }
}
