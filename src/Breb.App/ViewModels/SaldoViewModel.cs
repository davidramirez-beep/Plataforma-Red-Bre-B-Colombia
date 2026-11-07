using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Breb.App.Modelos;
using Breb.App.Servicios;

namespace Breb.App.ViewModels;

/// <summary>Lee disponible y retenido con el token que guardó Entrar. No pide otro login.</summary>
public partial class SaldoViewModel : ViewModelBase
{
    private readonly ClienteBrebApi _api;
    private readonly IAlmacenSesion _almacen;
    private readonly INavegacion _nav;
    private readonly ColaOperaciones _cola;

    public SaldoViewModel(ClienteBrebApi api, IAlmacenSesion almacen, INavegacion nav, ColaOperaciones cola)
    {
        _api = api;
        _almacen = almacen;
        _nav = nav;
        _cola = cola;
        Estado = "Sin consultar";
        SesionTexto = MensajesDeApi.SinSesion;
    }

    [ObservableProperty]
    private string saldoTexto = "—";

    [ObservableProperty]
    private string saldoRetenidoTexto = "—";

    [ObservableProperty]
    private string sesionTexto = string.Empty;

    [ObservableProperty]
    private string avisoPendientes = string.Empty;

    public bool HayAvisoPendientes => !string.IsNullOrWhiteSpace(AvisoPendientes);

    partial void OnAvisoPendientesChanged(string value) =>
        OnPropertyChanged(nameof(HayAvisoPendientes));

    protected override void AlCambiarCargando()
    {
        ConsultarSaldoCommand.NotifyCanExecuteChanged();
        IrATransferenciaCommand.NotifyCanExecuteChanged();
        CerrarSesionCommand.NotifyCanExecuteChanged();
    }

    protected override async Task AlRechazarAsync(ExcepcionDeApi ex)
    {
        if (ex.Codigo != 401 || ex.Operacion == "login") return;
        await _almacen.BorrarAsync();
        SesionTexto = MensajesDeApi.SinSesion;
        SaldoTexto = "—";
        SaldoRetenidoTexto = "—";
        await _nav.IrAsync(Rutas.Login);
    }

    [RelayCommand]
    private async Task PrepararAsync()
    {
        try
        {
            var sesion = await GuardiaDeSesion.ParaPantallaAsync(_almacen, _nav, DateTimeOffset.UtcNow);
            if (sesion is null) return;
            SesionTexto = $"{sesion.Usuario} · rol {sesion.Rol}";

            var pendientes = await _cola.PendientesAsync();
            AvisoPendientes = pendientes.Count == 0
                ? string.Empty
                : $"Hay {pendientes.Count} transferencia(s) guardada(s) en el teléfono. Abra Transferir: se reenvían con la misma llave.";
        }
        catch (ExcepcionAlmacenSeguro ex)
        {
            MarcarError(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private Task ConsultarSaldoAsync() => EjecutarAsync(async () =>
    {
        var sesion = await ExigirSesionAsync();
        if (sesion is null) return;

        Estado = "Consultando saldo…";
        var saldo = await _api.ConsultarSaldoAsync(CuentaDemo.Id, sesion.Token);
        SaldoTexto = FormatoUvb(saldo.SaldoDisponible);
        SaldoRetenidoTexto = FormatoUvb(saldo.SaldoRetenido);
        Tono = "ok";
        Estado = $"OK · rol {sesion.Rol} · retenido {SaldoRetenidoTexto}";
    });

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private Task IrATransferenciaAsync() => _nav.IrAsync(Rutas.Transferencia);

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private Task CerrarSesionAsync() => EjecutarAsync(async () =>
    {
        await _almacen.BorrarAsync();
        SaldoTexto = "—";
        SaldoRetenidoTexto = "—";
        SesionTexto = MensajesDeApi.SinSesion;
        await _nav.IrAsync(Rutas.Login);
    });

    private async Task<SesionGuardada?> ExigirSesionAsync()
    {
        var sesion = await _almacen.LeerAsync();
        if (sesion is not null) return sesion;
        MarcarError(MensajesDeApi.SinSesion);
        await _nav.IrAsync(Rutas.Login);
        return null;
    }
}
