using Breb.Cuentas.Dominio;
using Xunit;

namespace Breb.Cuentas.Tests;

public class RetencionInvarianteTests
{
    [Fact]
    public void Liquidar_y_despues_liberar_no_devuelve_el_dinero()
    {
        var cuenta = new Cuenta(Guid.NewGuid(), 5000m);
        var retencion = cuenta.Retener(Guid.NewGuid(), 15m);
        Assert.Equal(4985m, cuenta.SaldoDisponible);
        Assert.Equal(15m, cuenta.SaldoRetenido);

        Assert.True(cuenta.LiquidarRetencion(retencion));
        Assert.Equal(4985m, cuenta.SaldoDisponible);
        Assert.Equal(0m, cuenta.SaldoRetenido);
        Assert.True(retencion.Liquidada);

        Assert.False(cuenta.LiberarRetencion(retencion));
        Assert.False(retencion.Liberada);
        Assert.Equal(4985m, cuenta.SaldoDisponible);
        Assert.Equal(0m, cuenta.SaldoRetenido);
    }

    [Fact]
    public void Liberar_dos_veces_no_suma_el_disponible_dos_veces()
    {
        var cuenta = new Cuenta(Guid.NewGuid(), 100m);
        var retencion = cuenta.Retener(Guid.NewGuid(), 10m);

        Assert.True(cuenta.LiberarRetencion(retencion));
        Assert.False(cuenta.LiberarRetencion(retencion));
        Assert.Equal(100m, cuenta.SaldoDisponible);
        Assert.Equal(0m, cuenta.SaldoRetenido);
    }
}
