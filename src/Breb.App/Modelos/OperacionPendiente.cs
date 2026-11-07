namespace Breb.App.Modelos;

/// <summary>
/// Una transferencia que el usuario ya pidió y que el servidor todavía no confirmó.
/// Guarda la intención, no el resultado: la llave nace al tocar Transferir y se
/// reutiliza en cada reintento. Sin ella, reenviar crearía otra retención.
/// </summary>
public record OperacionPendiente(
    Guid Llave,
    Guid CuentaId,
    decimal MontoUVB,
    DateTime CreadaEn,
    int Intentos = 0);
