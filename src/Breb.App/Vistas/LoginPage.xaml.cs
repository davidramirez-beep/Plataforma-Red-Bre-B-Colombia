using Breb.App.ViewModels;

namespace Breb.App.Vistas;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Dispatcher.DispatchAsync(() => _viewModel.PrepararCommand.ExecuteAsync(null));
    }

    // El texto del Entry se lee aquí, no del ViewModel: al pulsar Entrar el
    // campo sigue enfocado y el enlace todavía tiene la dirección vieja.
    private void AlPulsarEntrar(object? sender, EventArgs e)
    {
        _viewModel.TomarLoEscrito(DireccionEntry.Text, UsuarioEntry.Text, ClaveEntry.Text);
        if (_viewModel.EntrarCommand.CanExecute(null))
            _viewModel.EntrarCommand.Execute(null);
    }
}
