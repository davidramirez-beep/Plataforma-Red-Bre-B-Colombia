namespace Breb.App.Servicios;

/// <summary>
/// Traduce la respuesta de /retener. La llave evita el duplicado; el estado
/// dice si esa única operación sigue en curso, ya salió o ya volvió.
/// Liberada no es éxito: el post-it de 15 s compensó.
/// </summary>
public static class InterpretacionDeRetencion
{
    public const string Pendiente = "pendiente";
    public const string Liquidada = "liquidada";
    public const string Liberada = "liberada";

    public readonly record struct Lectura(string Tono, string Resultado, string Estado, bool Terminal);

    public static Lectura Leer(bool repetida, string? estado, string numero, string monto)
    {
        var normalizado = (estado ?? string.Empty).Trim().ToLowerInvariant();

        if (normalizado == Liquidada)
        {
            return new Lectura(
                "ok",
                $"Esta transferencia ya se completó una vez. El dinero salió. No se hizo otra. Número: {numero}.",
                "Completada una vez. No es un duplicado.",
                Terminal: true);
        }

        if (normalizado == Liberada)
        {
            return new Lectura(
                "error",
                $"Esta operación se compensó: el banco no confirmó a tiempo y el dinero volvió. No es una transferencia completada y no se repitió. Número: {numero}. Si todavía quiere enviarla, pida otra: será otra llave.",
                "Compensada. El dinero volvió. No se presente como éxito.",
                Terminal: true);
        }

        if (normalizado.Length > 0 && normalizado != Pendiente)
        {
            return new Lectura(
                "error",
                $"El servidor respondió un estado desconocido ({estado}). Número: {numero}. No se asume que haya salido.",
                "Estado desconocido. No se da por completada.",
                Terminal: false);
        }

        if (repetida)
        {
            return new Lectura(
                "ok",
                $"El servidor ya tenía esta llave y no apartó otra vez. Sigue pendiente de confirmación del banco. Número: {numero}. Si nadie confirma, el dinero vuelve solo a los 15 segundos.",
                "Misma llave, una sola retención. Aún no es el final.",
                Terminal: false);
        }

        return new Lectura(
            "ok",
            $"Transferencia aceptada. Se apartaron {monto}. Número: {numero}. Si el banco destino no confirma, este dinero vuelve solo en unos 15 segundos.",
            "La plata quedó apartada. Aún no salió de la cuenta.",
            Terminal: false);
    }
}
