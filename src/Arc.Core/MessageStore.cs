using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Arc.Core;

/// <summary>Almacén SQLite del canal. Una conexión por operación (el pool la reutiliza).</summary>
public sealed class MessageStore
{
    public const int MaxBodyBytes = 256 * 1024;

    private readonly string _connectionString;
    private readonly TimeProvider _time;

    public MessageStore(string databasePath, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;

        SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
            DefaultTimeout = 30 // busy_timeout: espera en vez de fallar con SQLITE_BUSY
        };
        _connectionString = builder.ToString();
    }

    internal async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        SqliteConnection connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        // `synchronous` es por conexión y no se guarda en el fichero, a diferencia de
        // `journal_mode`. Emitirlo sólo al crear el esquema dejaba a todas las demás
        // conexiones del pool en el FULL por defecto: la compensación que las reglas
        // llaman decisión variaba entre operaciones.
        await using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA synchronous = NORMAL;";
            await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        return connection;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS messages (
                id             TEXT PRIMARY KEY,
                thread_id      TEXT NOT NULL,
                from_agent     TEXT NOT NULL,
                to_agent       TEXT NOT NULL,
                kind           TEXT NOT NULL,
                subject        TEXT,
                body           TEXT NOT NULL,
                refs_json      TEXT,
                status         TEXT NOT NULL,
                correlation_id TEXT,
                created_at     TEXT NOT NULL,
                answered_at    TEXT
            );

            CREATE INDEX IF NOT EXISTS ix_messages_inbox       ON messages(to_agent, status);
            CREATE INDEX IF NOT EXISTS ix_messages_replay      ON messages(to_agent, created_at);
            CREATE INDEX IF NOT EXISTS ix_messages_thread      ON messages(thread_id, created_at);
            CREATE INDEX IF NOT EXISTS ix_messages_correlation ON messages(correlation_id);

            CREATE TABLE IF NOT EXISTS agents (
                id            TEXT PRIMARY KEY,
                provider      TEXT,
                host          TEXT,
                last_seen     TEXT NOT NULL,
                messages_sent INTEGER NOT NULL DEFAULT 0
            );
            """;
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task AddAsync(Message message, CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = InsertSql;
        Bind(command, message);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Cierra el request e inserta la respuesta en la misma transacción. Devuelve
    /// <c>false</c> si el request ya estaba respondido, sin escribir nada.
    /// </summary>
    /// <remarks>
    /// El cierre va primero y lleva el estado en el WHERE, no en una comprobación
    /// previa del llamante: dos respuestas simultáneas pasarían las dos esa
    /// comprobación y el request acabaría con dos respuestas.
    /// </remarks>
    public async Task<bool> AddResponseAsync(Message response, CancellationToken ct = default)
    {
        if (response.CorrelationId is null)
        {
            throw new ArgumentException("Una respuesta necesita correlation_id.", nameof(response));
        }

        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = """
                UPDATE messages
                   SET status = 'answered', answered_at = $answered_at
                 WHERE id = $id AND kind = 'request' AND status <> 'answered'
                """;
            update.Parameters.AddWithValue("$id", response.CorrelationId);
            update.Parameters.AddWithValue("$answered_at", Format(response.CreatedAt));
            int closed = await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            if (closed == 0)
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
                return false;
            }
        }

        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = InsertSql;
            Bind(insert, response);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<Message?> GetAsync(string id, CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    /// <summary>La respuesta a un request, si ya llegó. Cinturón para la carrera del long-poll.</summary>
    public async Task<Message?> GetResponseForAsync(string requestId, CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE correlation_id = $id AND kind = 'response' ORDER BY created_at LIMIT 1";
        command.Parameters.AddWithValue("$id", requestId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    /// <summary>
    /// Correo del agente. Por defecto sólo lo no entregado; <paramref name="includeUnanswered"/>
    /// añade los requests ya entregados que siguen sin respuesta, y <paramref name="replaySince"/>
    /// añade todo lo dirigido al agente desde ese instante, sea cual sea su tipo y su estado.
    /// </summary>
    /// <remarks>
    /// Los tres criterios se suman en un OR y no se excluyen: un mensaje que cumple dos sale una
    /// vez. La ventana es lo único que alcanza a un aviso ya entregado, porque un aviso no tiene
    /// estado terminal — un request se drena solo al pasar a <c>answered</c>, y un aviso se queda
    /// en <c>delivered</c> para siempre, de modo que sin cota devolvería el histórico entero.
    /// <para>
    /// Se mide sobre <c>created_at</c> porque no hay columna de entrega, y añadirla no está
    /// disponible: P007 crea el esquema con <c>CREATE … IF NOT EXISTS</c>, que no toca una tabla
    /// que ya existe. Todas las fechas se guardan en el mismo ISO 8601 UTC de ancho fijo, así que
    /// comparar texto y comparar instantes coincide — es de lo que ya vive el <c>ORDER BY</c>.
    /// </para>
    /// <para>Ninguna de las tres ramas escribe: releer no cambia nada.</para>
    /// </remarks>
    public async Task<IReadOnlyList<Message>> GetInboxAsync(
        string agent, bool includeUnanswered = false, DateTimeOffset? replaySince = null, CancellationToken ct = default)
    {
        List<string> reachable = new List<string> { "status = 'pending'" };
        if (includeUnanswered)
        {
            reachable.Add("(status = 'delivered' AND kind = 'request')");
        }

        if (replaySince is not null)
        {
            reachable.Add("created_at >= $since");
        }

        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql
            + $" WHERE to_agent = $agent AND ({string.Join(" OR ", reachable)}) ORDER BY created_at";
        command.Parameters.AddWithValue("$agent", agent);
        if (replaySince is { } since)
        {
            command.Parameters.AddWithValue("$since", Format(since));
        }

        List<Message> messages = new List<Message>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            messages.Add(Read(reader));
        }

        return messages;
    }

    public async Task MarkDeliveredAsync(IEnumerable<string> ids, CancellationToken ct = default)
    {
        IReadOnlyCollection<string> list = ids as IReadOnlyCollection<string> ?? ids.ToList();
        if (list.Count == 0)
        {
            return;
        }

        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "UPDATE messages SET status = 'delivered' WHERE id = $id AND status = 'pending'";
        SqliteParameter parameter = command.Parameters.Add("$id", SqliteType.Text);

        foreach (string id in list)
        {
            parameter.Value = id;
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Los últimos mensajes del canal, en orden cronológico. Es la carga inicial del panel:
    /// se consulta en orden inverso para quedarse con la cola y se le da la vuelta al final.
    /// </summary>
    public async Task<IReadOnlyList<Message>> GetRecentAsync(int limit, string? threadId = null, CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql
            + (threadId is null ? "" : " WHERE thread_id = $thread")
            + " ORDER BY created_at DESC, rowid DESC LIMIT $limit";
        if (threadId is not null)
        {
            command.Parameters.AddWithValue("$thread", threadId);
        }

        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));

        List<Message> messages = new List<Message>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            messages.Add(Read(reader));
        }

        messages.Reverse();
        return messages;
    }

    public async Task<IReadOnlyList<Message>> GetThreadAsync(string threadId, CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = SelectSql + " WHERE thread_id = $thread ORDER BY created_at";
        command.Parameters.AddWithValue("$thread", threadId);

        List<Message> messages = new List<Message>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            messages.Add(Read(reader));
        }

        return messages;
    }

    /// <summary>
    /// El índice de conversaciones, de la más reciente a la más antigua. Una fila por
    /// hilo y sin cuerpos: el panel lo pide para poder elegir, no para leer.
    ///
    /// El estado se deriva aquí y no se guarda: un hilo está terminado cuando ninguna
    /// de sus preguntas sigue esperando. Guardarlo como columna obligaría a mantenerlo
    /// al día en cada respuesta, y ya hay una única verdad — el estado de los mensajes.
    /// </summary>
    public async Task<IReadOnlyList<ThreadSummary>> ListThreadsAsync(int limit = 200, CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.thread_id,
                   COUNT(*),
                   SUM(CASE WHEN m.kind = 'request' AND m.status <> 'answered' THEN 1 ELSE 0 END),
                   MIN(m.created_at),
                   MAX(m.created_at),
                   (SELECT s.subject FROM messages s
                     WHERE s.thread_id = m.thread_id AND s.subject IS NOT NULL
                     ORDER BY s.created_at, s.rowid LIMIT 1),
                   (SELECT group_concat(p.agent, ',') FROM (
                        SELECT from_agent AS agent FROM messages WHERE thread_id = m.thread_id
                        UNION
                        SELECT to_agent   AS agent FROM messages WHERE thread_id = m.thread_id
                    ) p)
              FROM messages m
             GROUP BY m.thread_id
             ORDER BY MAX(m.created_at) DESC, m.thread_id DESC
             LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 2000));

        List<ThreadSummary> threads = new List<ThreadSummary>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            // El separador es seguro: AgentNamePattern no admite comas en un nombre.
            string[] participants = reader.IsDBNull(6)
                ? Array.Empty<string>()
                : reader.GetString(6).Split(',', StringSplitOptions.RemoveEmptyEntries);
            Array.Sort(participants, StringComparer.Ordinal);

            threads.Add(new ThreadSummary
            {
                ThreadId = reader.GetString(0),
                Messages = reader.GetInt32(1),
                OpenRequests = reader.GetInt32(2),
                StartedAt = Parse(reader.GetString(3)),
                LastAt = Parse(reader.GetString(4)),
                Subject = reader.IsDBNull(5) ? null : reader.GetString(5),
                Participants = participants
            });
        }
        return threads;
    }

    public async Task TouchAgentAsync(string id, string? provider, string? host, bool sentMessage = false, CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agents (id, provider, host, last_seen, messages_sent)
            VALUES ($id, $provider, $host, $last_seen, $sent)
            ON CONFLICT(id) DO UPDATE SET
                provider      = COALESCE(excluded.provider, agents.provider),
                host          = COALESCE(excluded.host, agents.host),
                last_seen     = excluded.last_seen,
                messages_sent = agents.messages_sent + $sent
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$provider", (object?)provider ?? DBNull.Value);
        command.Parameters.AddWithValue("$host", (object?)host ?? DBNull.Value);
        command.Parameters.AddWithValue("$last_seen", Format(_time.GetUtcNow()));
        command.Parameters.AddWithValue("$sent", sentMessage ? 1 : 0);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentInfo>> ListAgentsAsync(CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT id, provider, host, last_seen, messages_sent FROM agents ORDER BY id";

        List<AgentInfo> agents = new List<AgentInfo>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            agents.Add(new AgentInfo
            {
                Id = reader.GetString(0),
                Provider = reader.IsDBNull(1) ? null : reader.GetString(1),
                Host = reader.IsDBNull(2) ? null : reader.GetString(2),
                LastSeen = Parse(reader.GetString(3)),
                MessagesSent = reader.GetInt32(4)
            });
        }
        return agents;
    }

    // ---------- SQL y mapeo ----------

    private const string SelectSql =
        "SELECT id, thread_id, from_agent, to_agent, kind, subject, body, " +
        "refs_json, status, correlation_id, created_at, answered_at FROM messages";

    private const string InsertSql =
        "INSERT INTO messages (id, thread_id, from_agent, to_agent, kind, subject, body, " +
        "refs_json, status, correlation_id, created_at, answered_at) " +
        "VALUES ($id, $thread_id, $from, $to, $kind, $subject, $body, " +
        "$refs, $status, $correlation_id, $created_at, $answered_at)";

    private static void Bind(SqliteCommand command, Message message)
    {
        command.Parameters.AddWithValue("$id", message.Id);
        command.Parameters.AddWithValue("$thread_id", message.ThreadId);
        command.Parameters.AddWithValue("$from", message.From);
        command.Parameters.AddWithValue("$to", message.To);
        command.Parameters.AddWithValue("$kind", Lower(message.Kind));
        command.Parameters.AddWithValue("$subject", (object?)message.Subject ?? DBNull.Value);
        command.Parameters.AddWithValue("$body", message.Body);
        command.Parameters.AddWithValue("$refs", (object?)message.Refs?.GetRawText() ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", Lower(message.Status));
        command.Parameters.AddWithValue("$correlation_id", (object?)message.CorrelationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", Format(message.CreatedAt));
        command.Parameters.AddWithValue("$answered_at", message.AnsweredAt is { } answered ? Format(answered) : DBNull.Value);
    }

    private static Message Read(IDataRecord row) => new()
    {
        Id = row.GetString(0),
        ThreadId = row.GetString(1),
        From = row.GetString(2),
        To = row.GetString(3),
        Kind = Enum.Parse<MessageKind>(row.GetString(4), ignoreCase: true),
        Subject = row.IsDBNull(5) ? null : row.GetString(5),
        Body = row.GetString(6),
        Refs = row.IsDBNull(7) ? null : JsonDocument.Parse(row.GetString(7)).RootElement.Clone(),
        Status = Enum.Parse<MessageStatus>(row.GetString(8), ignoreCase: true),
        CorrelationId = row.IsDBNull(9) ? null : row.GetString(9),
        CreatedAt = Parse(row.GetString(10)),
        AnsweredAt = row.IsDBNull(11) ? null : Parse(row.GetString(11))
    };

    private static string Lower<T>(T value) where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
