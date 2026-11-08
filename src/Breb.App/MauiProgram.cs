using Microsoft.Extensions.Logging;
using Breb.App.Servicios;
using Breb.App.ViewModels;
using Breb.App.Vistas;

namespace Breb.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		// El emulador habla con el host por 10.0.2.2. Un teléfono físico no:
		// Entrar deja cambiar la URL y AjusteDeDireccion la aplica en cada llamada.
		var direccion = new DireccionActiva(ConfiguracionApi.Direccion)
		{
			Nota = ConfiguracionApi.Descripcion,
			FaltaLaIpDelTelefono = ConfiguracionApi.FaltaConfigurar
		};
		builder.Services.AddSingleton(direccion);
		builder.Services.AddSingleton<DireccionGuardada>();
		builder.Services.AddTransient<AjusteDeDireccion>();
		builder.Services.AddHttpClient<ClienteBrebApi>(http =>
		{
			http.BaseAddress = new Uri(direccion.Base);
			http.Timeout = TimeSpan.FromSeconds(10);
		}).AddHttpMessageHandler<AjusteDeDireccion>();

		// La cola vive en el almacenamiento de la app: cerrarla no la borra.
		// Desinstalar sí. No es la memoria del proceso.
		builder.Services.AddSingleton(new ColaOperaciones(FileSystem.AppDataDirectory));

		builder.Services.AddSingleton<IBovedaSegura, BovedaSecureStorage>();
		builder.Services.AddSingleton<IAlmacenSesion, AlmacenSesion>();
		builder.Services.AddSingleton<INavegacion, NavegacionShell>();

		builder.Services.AddTransient<LoginViewModel>();
		builder.Services.AddTransient<SaldoViewModel>();
		builder.Services.AddTransient<TransferenciaViewModel>();
		builder.Services.AddTransient<LoginPage>();
		builder.Services.AddTransient<SaldoPage>();
		builder.Services.AddTransient<TransferenciaPage>();
		builder.Services.AddSingleton<AppShell>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
