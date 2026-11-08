using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Breb.App.Servicios;

namespace Breb.App.ViewModels;

/// <summary>Pide el token y lo deja en el almacén seguro. No consulta ni transfiere.</summary>
public partial class LoginViewModel : ViewModelBase
{
    private readonly ClienteBrebApi _api;
    private readonly IAlmacenSesion _almacen;
    private readonly INavegacion _nav;
    private readonly DireccionActiva _direccion;
    private readonly DireccionGuardada _guardada;
    private bool _revisoArranque;

    public LoginViewModel(
        ClienteBrebApi api,
        IAlmacenSesion almacen,
        INavegacion nav,
        DireccionActiva direccion,
        DireccionGuardada guardada)
    {
        _api = api;
        _almacen = almacen;
        _nav = nav;
        _direccion = direccion;
        _guardada = guardada;
        Usuario = "luis.consulta";
        Clave = "Consulta-2026";
        DireccionTexto = direccion.Base;
        Estado = direccion.FaltaLaIpDelTelefono
            ? direccion.Nota
            : "Escriba usuario y clave.";
    }

    [ObservableProperty]
    private string usuario = string.Empty;

    [ObservableProperty]
    private string clave = string.Empty;

    [ObservableProperty]
    private string direccionTexto = string.Empty;

    protected override void AlCambiarCargando() => EntrarCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Solo al abrir. Si ya hay un JWT vigente, entra a Saldo. Si está vencido
    /// o no se puede leer, se queda en Entrar. Una segunda vez (el atrás
    /// desde Saldo) no vuelve a saltar.
    /// </summary>
    [RelayCommand]
    private async Task PrepararAsync()
    {
        if (_revisoArranque) return;
        _revisoArranque = true;

        try
        {
            await _guardada.CargarAsync();
            DireccionTexto = _direccion.Base;

            var sesion = await _almacen.LeerAsync();
            if (sesion is null) return;

            if (!VigenciaDelToken.SigueVigente(sesion.Token, DateTimeOffset.UtcNow))
            {
                await _almacen.BorrarAsync();
                return;
            }

            Tono = "ok";
            Estado = $"Sesión guardada · rol {sesion.Rol}.";
            await _nav.IrAsync(Rutas.Saldo);
        }
        catch (ExcepcionAlmacenSeguro ex)
        {
            MarcarError(ex.Message);
        }
    }

    /// <summary>
    /// Lo que está escrito en los Entry, copiado antes de Entrar. En Android
    /// el enlace del texto no llega al ViewModel al pulsar el botón: se queda
    /// el valor anterior, que es el http del emulador en el puerto 5080.
    /// </summary>
    public void TomarLoEscrito(string? direccion, string? usuario, string? clave)
    {
        DireccionTexto = direccion ?? string.Empty;
        Usuario = usuario ?? string.Empty;
        Clave = clave ?? string.Empty;
    }

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private Task EntrarAsync() => EjecutarAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Usuario) || string.IsNullOrWhiteSpace(Clave))
        {
            MarcarError("Escriba usuario y clave.");
            return;
        }

        if (!await AplicarDireccionAsync()) return;

        Estado = "Pidiendo token…";
        var token = await _api.ObtenerTokenAsync(Usuario.Trim(), Clave);
        await _almacen.GuardarAsync(token.Token, token.Rol, Usuario.Trim());
        Tono = "ok";
        Estado = $"Sesión iniciada · rol {token.Rol}.";
        await _nav.IrAsync(Rutas.Saldo);
    });

    private async Task<bool> AplicarDireccionAsync()
    {
        try
        {
            var normalizada = DireccionActiva.Normalizar(DireccionTexto);
            if (!string.Equals(normalizada, _direccion.Base, StringComparison.OrdinalIgnoreCase))
                await _guardada.GuardarAsync(normalizada);
            DireccionTexto = _direccion.Base;
            return true;
        }
        catch (ArgumentException ex)
        {
            MarcarError(ex.Message);
            return false;
        }
    }
}
