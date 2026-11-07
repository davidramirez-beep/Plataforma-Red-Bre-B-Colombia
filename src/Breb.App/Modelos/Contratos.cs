namespace Breb.App.Modelos;

// Mismos contratos que expone Breb.Cuentas. No hay DTOs compartidos entre el
// backend y la app: se copian a mano, como pasa en cualquier app móvil real
// que consume una API que no controla.

public record SolicitudToken(string Usuario, string Clave);

public record RespuestaToken(string Token, DateTimeOffset Expira, string Tipo, string Rol, double DuracionMinutos);

public record RespuestaSaldo(Guid CuentaId, decimal SaldoDisponible, decimal SaldoRetenido);

// POST /cuentas/{id}/retener responde { transferenciaId, montoUVB, repetida }.
// Repetida=true: el servidor ya tenía esta llave y no apartó otra vez.
// Si el JSON no trae el campo (API anterior), queda en false.
public record RespuestaRetencion(Guid TransferenciaId, decimal MontoUVB, bool Repetida = false);
