using Breb.App.ViewModels;

namespace Breb.App.Vistas;

public partial class TransferenciaPage : ContentPage
{
    private readonly TransferenciaViewModel _viewModel;

    public TransferenciaPage(TransferenciaViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Connectivity.Current.ConnectivityChanged += AlCambiarLaRed;
        Dispatcher.DispatchAsync(() => _viewModel.PrepararCommand.ExecuteAsync(null));
    }

    protected override void OnDisappearing()
    {
        Connectivity.Current.ConnectivityChanged -= AlCambiarLaRed;
        base.OnDisappearing();
    }

    // Modo avión a mitad: la llamada que ya salió falla y queda en disco.
    // Al quitar el modo avión, esta señal reenvía la misma llave sin otro toque.
    private void AlCambiarLaRed(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess != NetworkAccess.Internet) return;
        Dispatcher.DispatchAsync(() => _viewModel.ReintentarPendientesCommand.ExecuteAsync(null));
    }
}
