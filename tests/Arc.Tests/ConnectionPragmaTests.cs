using Arc.Core;
using Microsoft.Data.Sqlite;

namespace Arc.Tests;

/// <summary>
/// Los PRAGMA con los que el store abre. `journal_mode` se guarda en el fichero;
/// `synchronous` es por conexión, y ésa es toda la razón de que este fichero exista:
/// emitido una sola vez al crear el esquema, dejaba a las demás conexiones del pool
/// en el FULL por defecto sin que nada lo dijera.
/// </summary>
public sealed class ConnectionPragmaTests : IAsyncLifetime
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"arc-pragma-{Guid.NewGuid():n}.db");
    private MessageStore _store = null!;

    public async Task InitializeAsync()
    {
        _store = new MessageStore(_path);
        await _store.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<string> ReadPragmaAsync(SqliteConnection connection, string name)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        object? value = await command.ExecuteScalarAsync();
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
    }

    [Fact]
    public async Task Toda_conexion_del_store_abre_en_synchronous_normal()
    {
        // Tres seguidas: la primera puede ser la de InitializeAsync devuelta al pool,
        // las siguientes no. Si el PRAGMA sólo se emitiera al crear el esquema, alguna
        // de éstas volvería con el 2 de FULL.
        for (int i = 0; i < 3; i++)
        {
            await using SqliteConnection connection = await _store.OpenAsync(default);
            Assert.Equal("1", await ReadPragmaAsync(connection, "synchronous"));
        }
    }

    [Fact]
    public async Task El_modo_de_diario_es_wal_y_lo_recuerda_el_fichero()
    {
        await using SqliteConnection connection = await _store.OpenAsync(default);

        Assert.Equal("wal", await ReadPragmaAsync(connection, "journal_mode"));
    }
}
