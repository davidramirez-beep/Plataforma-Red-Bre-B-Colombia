namespace Breb.App.Servicios;

/// <summary>
/// Reescribe el host de cada petición con <see cref="DireccionActiva"/>.
/// El HttpClient se crea una vez, al arrancar; la URL puede cambiar después.
/// </summary>
public sealed class AjusteDeDireccion(DireccionActiva direccion) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { IsAbsoluteUri: true } uri &&
            Uri.TryCreate(direccion.Base, UriKind.Absolute, out var baseUri))
        {
            var builder = new UriBuilder(uri)
            {
                Scheme = baseUri.Scheme,
                Host = baseUri.Host,
                Port = baseUri.Port
            };
            request.RequestUri = builder.Uri;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
