namespace Breb.Cuentas.Infraestructura;

/// <summary>
/// De dónde salen PostgreSQL y RabbitMQ.
/// En Development, si no hay configuración, se usan los puertos del
/// docker-compose de laboratorio. Fuera de Development no hay valores
/// por defecto y se rechaza la contraseña publicada en el repositorio.
/// </summary>
public static class ConfiguracionDeServidor
{
    public const string PasswordPostgresDeLaboratorio = "dev_only_password";
    public const string UsuarioRabbitDeLaboratorio = "guest";

    public readonly record struct ConexionRabbit(
        string Host, ushort Port, string VirtualHost, string User, string Password);

    public static string CadenaDeCuentas(string? configurada, bool desarrollo)
    {
        if (string.IsNullOrWhiteSpace(configurada))
        {
            if (!desarrollo)
                throw new InvalidOperationException(
                    "Falta ConnectionStrings__Cuentas. Fuera de Development no hay cadena por defecto. " +
                    "PostgreSQL tiene que escuchar solo en 127.0.0.1.");

            return "Host=localhost;Port=5433;Database=brebcuentas;" +
                   "Username=postgres;Password=" + PasswordPostgresDeLaboratorio + ";" +
                   "SSL Mode=Disable;Timeout=30;Command Timeout=60;" +
                   "Maximum Pool Size=25;Minimum Pool Size=5";
        }

        if (!desarrollo && configurada.Contains(PasswordPostgresDeLaboratorio, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "ConnectionStrings__Cuentas usa la contraseña de laboratorio. Fuera de Development no se acepta.");

        return configurada;
    }

    public static ConexionRabbit Rabbit(
        string? host, string? port, string? virtualHost, string? user, string? password, bool desarrollo)
    {
        var faltan = string.IsNullOrWhiteSpace(host)
                     || string.IsNullOrWhiteSpace(user)
                     || string.IsNullOrWhiteSpace(password);
        if (faltan)
        {
            if (!desarrollo)
                throw new InvalidOperationException(
                    "Faltan RabbitMq__Host, RabbitMq__User y RabbitMq__Password. " +
                    "Fuera de Development no se usa guest.");

            host = "localhost";
            port ??= "5673";
            virtualHost ??= "/";
            user = UsuarioRabbitDeLaboratorio;
            password = UsuarioRabbitDeLaboratorio;
        }

        if (!desarrollo && (user == UsuarioRabbitDeLaboratorio || password == UsuarioRabbitDeLaboratorio))
            throw new InvalidOperationException(
                "RabbitMQ guest no se acepta fuera de Development. El broker no debe quedar público.");

        if (!ushort.TryParse(string.IsNullOrWhiteSpace(port) ? "5673" : port, out var puerto))
            throw new InvalidOperationException("RabbitMq__Port no es un puerto válido.");

        return new ConexionRabbit(
            host!,
            puerto,
            string.IsNullOrWhiteSpace(virtualHost) ? "/" : virtualHost,
            user!,
            password!);
    }
}
