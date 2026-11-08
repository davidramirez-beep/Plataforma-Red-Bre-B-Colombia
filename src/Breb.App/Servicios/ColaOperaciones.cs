using System.Text.Json;
using Breb.App.Modelos;

namespace Breb.App.Servicios;

/// <summary>
/// Cola local de transferencias pendientes, en un archivo JSON.
/// No referencia MAUI: se puede ejercer desde las pruebas y desde
/// <c>Breb.ColaPrueba</c> en otro proceso. Eso es lo que demuestra que
/// cerrar la app no borra la intención.
/// </summary>
public sealed class ColaOperaciones
{
    private const string Archivo = "operaciones-pendientes.json";

    // Dos reintentos a la vez no pueden leer-modificar-escribir el mismo
    // archivo: uno perdería la operación del otro. Hacen fila.
    private readonly SemaphoreSlim _candado = new(1, 1);
    private readonly string _ruta;

    public ColaOperaciones(string carpeta)
    {
        Directory.CreateDirectory(carpeta);
        _ruta = Path.Combine(carpeta, Archivo);
    }

    public string Ruta => _ruta;

    /// <summary>
    /// Se llama antes de salir a la red. La misma llave no se encola dos veces.
    /// </summary>
    public async Task EncolarAsync(OperacionPendiente op)
    {
        await _candado.WaitAsync();
        try
        {
            var cola = await LeerAsync();
            if (cola.Any(o => o.Llave == op.Llave)) return;
            cola.Add(op);
            await EscribirAsync(cola);
        }
        finally { _candado.Release(); }
    }

    public async Task<IReadOnlyList<OperacionPendiente>> PendientesAsync()
    {
        await _candado.WaitAsync();
        try
        {
            var cola = await LeerAsync();
            return cola.OrderBy(o => o.CreadaEn).ToList();
        }
        finally { _candado.Release(); }
    }

    /// <summary>
    /// Solo cuando el servidor confirmó, sea la primera vez o una repetición
    /// de la misma llave. Las dos quieren decir que ya está hecha.
    /// </summary>
    public async Task ConfirmarAsync(Guid llave)
    {
        await _candado.WaitAsync();
        try
        {
            var cola = await LeerAsync();
            if (cola.RemoveAll(o => o.Llave == llave) > 0)
                await EscribirAsync(cola);
        }
        finally { _candado.Release(); }
    }

    public async Task RegistrarIntentoAsync(Guid llave)
    {
        await _candado.WaitAsync();
        try
        {
            var cola = await LeerAsync();
            var i = cola.FindIndex(o => o.Llave == llave);
            if (i < 0) return;
            cola[i] = cola[i] with { Intentos = cola[i].Intentos + 1 };
            await EscribirAsync(cola);
        }
        finally { _candado.Release(); }
    }

    public Task DescartarAsync(Guid llave) => ConfirmarAsync(llave);

    private async Task<List<OperacionPendiente>> LeerAsync()
    {
        if (!File.Exists(_ruta)) return [];
        try
        {
            await using var fs = File.OpenRead(_ruta);
            return await JsonSerializer.DeserializeAsync<List<OperacionPendiente>>(fs) ?? [];
        }
        catch (JsonException)
        {
            // Un archivo a medias no debe impedir que la app abra.
            // Que esto sea raro depende de EscribirAsync.
            return [];
        }
    }

    // Temporal y luego Move. Si el proceso muere a mitad, queda el archivo
    // anterior entero o el nuevo entero, nunca un JSON truncado que se lleve
    // por delante todas las operaciones.
    private async Task EscribirAsync(List<OperacionPendiente> cola)
    {
        var temporal = _ruta + ".tmp";
        await using (var fs = File.Create(temporal))
        {
            await JsonSerializer.SerializeAsync(fs, cola,
                new JsonSerializerOptions { WriteIndented = true });
            await fs.FlushAsync();
        }
        File.Move(temporal, _ruta, overwrite: true);
    }
}
