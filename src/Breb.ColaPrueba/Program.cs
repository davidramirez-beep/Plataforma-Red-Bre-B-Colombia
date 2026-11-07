using Breb.App.Modelos;
using Breb.App.Servicios;

// Dos procesos distintos, la misma carpeta:
//   dotnet run -- encolar <carpeta>
//   dotnet run -- leer    <carpeta>
// Si el segundo ve lo que encoló el primero, la cola sobrevive a que el proceso muera.

if (args.Length < 2)
{
    Console.WriteLine("uso: encolar|leer|confirmar|intento|corromper|concurrente <carpeta> [llave|n]");
    return 1;
}

var orden = args[0];
var cola = new ColaOperaciones(args[1]);
var cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

switch (orden)
{
    case "encolar":
    {
        var op = new OperacionPendiente(Guid.NewGuid(), cuenta, 100m, DateTime.UtcNow);
        await cola.EncolarAsync(op);
        Console.WriteLine($"encolada   {op.Llave}");
        Console.WriteLine($"archivo    {cola.Ruta}");
        break;
    }

    case "leer":
    {
        var pendientes = await cola.PendientesAsync();
        Console.WriteLine($"pendientes {pendientes.Count}");
        foreach (var o in pendientes)
            Console.WriteLine($"  {o.Llave}  {o.MontoUVB,8:N2} UVB  intentos={o.Intentos}  creada={o.CreadaEn:HH:mm:ss}");
        break;
    }

    case "confirmar":
    {
        await cola.ConfirmarAsync(Guid.Parse(args[2]));
        Console.WriteLine($"confirmada {args[2]}");
        break;
    }

    case "intento":
    {
        await cola.RegistrarIntentoAsync(Guid.Parse(args[2]));
        Console.WriteLine($"intento registrado para {args[2]}");
        break;
    }

    case "corromper":
    {
        await File.WriteAllTextAsync(cola.Ruta, "[{\"Llave\":\"esto no es un guid");
        Console.WriteLine($"corrompido {cola.Ruta}");
        break;
    }

    case "concurrente":
    {
        var n = args.Length > 2 ? int.Parse(args[2]) : 20;
        var tareas = Enumerable.Range(0, n).Select(_ =>
            cola.EncolarAsync(new OperacionPendiente(Guid.NewGuid(), cuenta, 10m, DateTime.UtcNow)));
        await Task.WhenAll(tareas);
        Console.WriteLine($"encoladas {n} en paralelo");
        break;
    }

    default:
        Console.WriteLine($"orden desconocida: {orden}");
        return 1;
}

return 0;
