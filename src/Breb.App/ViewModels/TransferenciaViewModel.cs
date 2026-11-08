using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Breb.App.Modelos;
using Breb.App.Servicios;

namespace Breb.App.ViewModels;

/// <summary>
/// Dispara POST /cuentas/{id}/retener con el token guardado. La llave se
/// escribe en disco antes de la red. No confirma el abono: eso es del rol
/// banco. A los 15 s, si nadie confirma, la saga devuelve la plata.
/// </summary>
public partial class TransferenciaViewModel : ViewModelBase
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CO");

    private readonly ClienteBrebApi _api;
    private readonly IAlmacenSesion _almacen;
    private readonly INavegacion _nav;
    private readonly ColaOperaciones _cola;
    private readonly DireccionActiva _direccion;
    private readonly SemaphoreSlim _unEnvio = new(1, 1);

    public TransferenciaViewModel(
        ClienteBrebApi api,
        IAlmacenSesion almacen,
        INavegacion nav,
        ColaOperaciones cola,
        DireccionActiva direccion)
    {
        _api = api;
        _almacen = almacen;
        _nav = nav;
        _cola = cola;
        _direccion = direccion;
        MontoTexto = "10";
        Estado = "Indique el monto y pulse Transferir.";
        SesionTexto = MensajesDeApi.SinSesion;
        DireccionTexto = direccion.Base;
        LlaveTexto = "—";
        PendientesTexto = "Sin operaciones pendientes";
    }

    internal ColaOperaciones Cola => _cola;

    [ObservableProperty]
    private string montoTexto = string.Empty;

    [ObservableProperty]
    private string resultadoTransferencia = string.Empty;

    [ObservableProperty]
    private string saldoTexto = "—";

    [ObservableProperty]
    private string saldoRetenidoTexto = "—";

    [ObservableProperty]
    private string sesionTexto = string.Empty;

    [ObservableProperty]
    private string direccionTexto = string.Empty;

    [ObservableProperty]
    private string llaveTexto = "—";

    [ObservableProperty]
    private string pendientesTexto = string.Empty;

    [ObservableProperty]
    private bool hayPendientes;

    public bool HayResultado => !string.IsNullOrWhiteSpace(ResultadoTransferencia);

    partial void OnResultadoTransferenciaChanged(string value) =>
        OnPropertyChanged(nameof(HayResultado));

    protected override void AlCambiarCargando()
    {
        TransferirCommand.NotifyCanExecuteChanged();
        ReintentarPendientesCommand.NotifyCanExecuteChanged();
        RefrescarSaldoCommand.NotifyCanExecuteChanged();
        DescartarPendientesCommand.NotifyCanExecuteChanged();
        IrASaldoCommand.NotifyCanExecuteChanged();
        CerrarSesionCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task PrepararAsync()
    {
        try
        {
            DireccionTexto = _direccion.Base;
            var sesion = await GuardiaDeSesion.ParaPantallaAsync(_almacen, _nav, DateTimeOffset.UtcNow);
            await RefrescarPendientesAsync();
            if (sesion is null)
            {
                // Sin sesión no se borra lo encolado: al volver a entrar se reenvía.
                if (HayPendientes)
                    Estado = "Hay una transferencia guardada, pero no hay sesión. Entre y vuelva a Transferir.";
                return;
            }

            SesionTexto = $"{sesion.Usuario} · rol {sesion.Rol}";
            if (HayPendientes)
                await ReintentarPendientesAsync();
        }
        catch (ExcepcionAlmacenSeguro ex)
        {
            MarcarError(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private Task TransferirAsync() => EjecutarAsync(async () =>
    {
        if (!TryParseMonto(MontoTexto, out var monto))
        {
            MarcarError("Escriba un monto mayor que cero. Por ejemplo: 10.");
            return;
        }

        var sesion = await _almacen.LeerAsync();
        if (sesion is null)
        {
            MarcarError(MensajesDeApi.SinSesion);
            await _nav.IrAsync(Rutas.Login);
            return;
        }

        // La llave nace aquí y se guarda antes de la red. Si la app se cierra
        // en el medio, la intención sigue en disco y el reintento es el mismo id.
        var op = new OperacionPendiente(Guid.NewGuid(), CuentaDemo.Id, monto, DateTime.UtcNow);
        await _cola.EncolarAsync(op);
        LlaveTexto = op.Llave.ToString();
        await RefrescarPendientesAsync();
        ResultadoTransferencia = string.Empty;
        await EnviarAsync(op, esReintento: false);
    });

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private Task ReintentarPendientesAsync() => EjecutarAsync(async () =>
    {
        var pendientes = await _cola.PendientesAsync();
        if (pendientes.Count == 0)
        {
            Estado = "No hay nada pendiente.";
            Tono = "neutro";
            return;
        }

        var sesion = await _almacen.LeerAsync();
        if (sesion is null || !VigenciaDelToken.SigueVigente(sesion.Token, DateTimeOffset.UtcNow))
        {
            Estado = $"Hay {pendientes.Count} operación(es) guardada(s), pero no hay sesión vigente. " +
                     "Entre y vuelva a Transferir: no se pierde nada.";
            Tono = "error";
            return;
        }

        foreach (var op in pendientes)
        {
            LlaveTexto = op.Llave.ToString();
            var seguir = await EnviarAsync(op, esReintento: true);
            if (!seguir) break;
        }
    });

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private Task RefrescarSaldoAsync() => EjecutarAsync(async () =>
    {
        var sesion = await _almacen.LeerAsync();
        if (sesion is null)
        {
            MarcarError(MensajesDeApi.SinSesion);
            await _nav.IrAsync(Rutas.Login);
            return;
        }

        if (Guid.TryParse(LlaveTexto, out var llave))
        {
            var estado = await _api.ConsultarEstadoAsync(llave, sesion.Token);
            Mostrar(estado);
            if (InterpretacionDeRetencion.Leer(estado.Repetida, estado.Estado, estado.TransferenciaId.ToString(), FormatoUvb(estado.MontoUVB)).Terminal)
                await _cola.ConfirmarAsync(llave);
            await RefrescarPendientesAsync();
        }

        var saldo = await _api.ConsultarSaldoAsync(CuentaDemo.Id, sesion.Token);
        SaldoTexto = FormatoUvb(saldo.SaldoDisponible);
        SaldoRetenidoTexto = FormatoUvb(saldo.SaldoRetenido);
        if (!Guid.TryParse(LlaveTexto, out _))
        {
            Tono = "neutro";
            Estado = "No hay una llave a la vista. El saldo solo no dice si una transferencia se completó o se compensó.";
        }
    });

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private async Task DescartarPendientesAsync()
    {
        foreach (var op in await _cola.PendientesAsync())
            await _cola.DescartarAsync(op.Llave);

        await RefrescarPendientesAsync();
        LlaveTexto = "—";
        ResultadoTransferencia = string.Empty;
        Tono = "neutro";
        Estado = "Operaciones pendientes descartadas. No se enviaron.";
    }

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private Task IrASaldoAsync() => _nav.IrAsync(Rutas.Atras);

    [RelayCommand(CanExecute = nameof(NoEstaCargando))]
    private Task CerrarSesionAsync() => EjecutarAsync(async () =>
    {
        await _almacen.BorrarAsync();
        SesionTexto = MensajesDeApi.SinSesion;
        await _nav.IrAsync(Rutas.Login);
    });

    /// <summary>false: no siga con la siguiente. La operación, si sigue pendiente, no se borra.</summary>
    private async Task<bool> EnviarAsync(OperacionPendiente op, bool esReintento)
    {
        if (!await _unEnvio.WaitAsync(0))
            return false;

        try
        {
            var sesion = await _almacen.LeerAsync();
            if (sesion is null)
            {
                Tono = "error";
                Estado = "No hay sesión. La operación sigue guardada: entre y vuelva a Transferir.";
                await RefrescarPendientesAsync();
                return false;
            }

            Estado = esReintento ? "Reenviando la operación guardada…" : "Transfiriendo…";
            Tono = "neutro";

            var retencion = await _api.RetenerAsync(op.CuentaId, op.MontoUVB, sesion.Token, op.Llave);
            var lectura = Mostrar(retencion);
            // Pendiente se queda en disco: al reabrir se vuelve a preguntar el
            // mismo id. Solo un final (salió o volvió) la saca de la cola.
            if (lectura.Terminal)
                await _cola.ConfirmarAsync(op.Llave);
            await RefrescarPendientesAsync();

            try
            {
                var saldo = await _api.ConsultarSaldoAsync(op.CuentaId, sesion.Token);
                SaldoTexto = FormatoUvb(saldo.SaldoDisponible);
                SaldoRetenidoTexto = FormatoUvb(saldo.SaldoRetenido);
            }
            catch (Exception ex) when (ex is ExcepcionDeApi or HttpRequestException or TaskCanceledException)
            {
                if (ex is ExcepcionDeApi { Codigo: 401 })
                {
                    await _almacen.BorrarAsync();
                    await _nav.IrAsync(Rutas.Login);
                    MarcarError(MensajesDeApi.Para((ExcepcionDeApi)ex));
                    return false;
                }

                if (lectura.Tono == "error")
                    return lectura.Terminal;

                Tono = lectura.Tono;
                Estado = "La operación quedó registrada con esa llave, pero no pude refrescar el saldo. No asumo si el dinero salió o volvió.";
            }

            return true;
        }
        catch (ExcepcionDeApi ex) when (ex.Codigo == 401)
        {
            await _almacen.BorrarAsync();
            await RefrescarPendientesAsync();
            await _nav.IrAsync(Rutas.Login);
            MarcarError(MensajesDeApi.Para(ex));
            return false;
        }
        catch (ExcepcionDeApi ex) when (ex.Codigo >= 500)
        {
            await _cola.RegistrarIntentoAsync(op.Llave);
            await RefrescarPendientesAsync();
            MarcarError(MensajesDeApi.Para(ex) + " Sigue guardada: el reintento usa la misma llave.");
            return true;
        }
        catch (ExcepcionDeApi ex)
        {
            // 400 y 403 no se arreglan reintentando. El 403 conserva el texto literal.
            await _cola.DescartarAsync(op.Llave);
            await RefrescarPendientesAsync();
            ResultadoTransferencia = string.Empty;
            MarcarError(MensajesDeApi.Para(ex));
            return true;
        }
        catch (HttpRequestException)
        {
            await _cola.RegistrarIntentoAsync(op.Llave);
            await RefrescarPendientesAsync();
            MarcarError(MensajesDeApi.SinRed +
                        " La transferencia quedó guardada y se reenvía con la misma llave al volver la red.");
            return false;
        }
        catch (TaskCanceledException)
        {
            await _cola.RegistrarIntentoAsync(op.Llave);
            await RefrescarPendientesAsync();
            MarcarError(MensajesDeApi.Timeout +
                        " No sabemos si llegó. Quedó guardada: reintentar usa la misma llave y no duplica.");
            return false;
        }
        catch (ExcepcionAlmacenSeguro ex)
        {
            await RefrescarPendientesAsync();
            MarcarError(ex.Message);
            return false;
        }
        finally
        {
            _unEnvio.Release();
        }
    }

    private InterpretacionDeRetencion.Lectura Mostrar(RespuestaRetencion retencion)
    {
        var lectura = InterpretacionDeRetencion.Leer(
            retencion.Repetida,
            retencion.Estado,
            retencion.TransferenciaId.ToString(),
            FormatoUvb(retencion.MontoUVB));
        ResultadoTransferencia = lectura.Resultado;
        Tono = lectura.Tono;
        Estado = lectura.Tono == "error"
            ? lectura.Estado
            : $"Listo · {lectura.Estado}";
        return lectura;
    }

    private async Task RefrescarPendientesAsync()
    {
        var pendientes = await _cola.PendientesAsync();
        HayPendientes = pendientes.Count > 0;
        PendientesTexto = pendientes.Count switch
        {
            0 => "Sin operaciones pendientes",
            1 => $"1 operación pendiente · {FormatoUvb(pendientes[0].MontoUVB)} · {pendientes[0].Intentos} intento(s) · llave {pendientes[0].Llave}",
            _ => $"{pendientes.Count} operaciones pendientes · {FormatoUvb(pendientes.Sum(o => o.MontoUVB))} en total"
        };
    }

    private static bool TryParseMonto(string? texto, out decimal monto)
    {
        monto = 0;
        if (string.IsNullOrWhiteSpace(texto)) return false;

        const NumberStyles estilos = NumberStyles.Number;
        var ok = decimal.TryParse(texto, estilos, Cultura, out monto)
                 || decimal.TryParse(texto, estilos, CultureInfo.InvariantCulture, out monto);
        return ok && monto > 0;
    }
}
