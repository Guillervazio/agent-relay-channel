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
    /// Los tres criterios del buzón. Están aquí y no dentro del reclamo porque son la parte que
    /// se lee sola: qué alcanza cada uno es lo que decide P020, y el SELECT que los usa está a
    /// treinta líneas de distancia rodeado de transacción.
    /// </summary>
    private static void BindInbox(
        SqliteCommand command, string agent, bool includeUnanswered, DateTimeOffset? replaySince)
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

        command.CommandText = SelectSql
            + $" WHERE to_agent = $agent AND ({string.Join(" OR ", reachable)}) ORDER BY created_at";
        command.Parameters.AddWithValue("$agent", agent);
        if (replaySince is { } since)
        {
            command.Parameters.AddWithValue("$since", Format(since));
        }
    }

    /// <summary>
    /// El buzón, leído y marcado en una sola transacción. Devuelve lo que este llamante se lleva,
    /// con el estado que la fila tiene ya escrito: lo recién reclamado sale <c>delivered</c>, no
    /// <c>pending</c>.
    /// </summary>
    /// <remarks>
    /// Sustituye al par <c>GetInboxAsync</c> + <c>MarkDeliveredAsync</c> en el camino del buzón, y
    /// la razón es una carrera, no la comodidad. Entre la lectura y el marcado cabía otro sondeo
    /// del mismo agente: los dos leían las mismas filas y las dos respuestas HTTP se llevaban el
    /// mensaje, aunque sólo una lo marcase. La transacción es de escritura desde que empieza, así
    /// que el segundo sondeo espera y su SELECT ya no ve pendiente lo que el primero se llevó.
    /// <para>
    /// Es la forma de H007 que <c>AddResponseAsync</c> ya usa: la decisión de devolver salió de una
    /// fila leída, así que el WHERE del UPDATE la repite. Aquí el recuento de filas afectadas es
    /// además lo que dice qué se reclamó de verdad y qué venía ya entregado por la ventana.
    /// </para>
    /// <para>
    /// Lo que esto no arregla: el mensaje se sigue marcando antes de que el cliente lo tenga. Una
    /// respuesta perdida en tránsito lo saca del buzón por defecto igual que antes, y la vuelta
    /// sigue siendo <c>?replay=N</c> — P020. Reconocimiento y reintento son P001, y no es esto.
    /// </para>
    /// </remarks>
    public async Task<(IReadOnlyList<Message> Messages, IReadOnlyList<string> Claimed)> ClaimInboxAsync(
        string agent, bool includeUnanswered = false, DateTimeOffset? replaySince = null, CancellationToken ct = default)
    {
        await using SqliteConnection connection = await OpenAsync(ct).ConfigureAwait(false);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        List<Message> read = new List<Message>();
        await using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = (SqliteTransaction)transaction;
            BindInbox(select, agent, includeUnanswered, replaySince);
            await using SqliteDataReader reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                read.Add(Read(reader));
            }
        }

        if (read.Count == 0)
        {
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return (read, Array.Empty<string>());
        }

        List<Message> messages = new List<Message>(read.Count);
        List<string> claimed = new List<string>();
        await using (SqliteCommand update = connection.CreateCommand())
        {
            update.Transaction = (SqliteTransaction)transaction;
            update.CommandText = "UPDATE messages SET status = 'delivered' WHERE id = $id AND status = 'pending'";
            SqliteParameter parameter = update.Parameters.Add("$id", SqliteType.Text);

            foreach (Message message in read)
            {
                if (message.Status != MessageStatus.Pending)
                {
                    messages.Add(message);
                    continue;
                }

                parameter.Value = message.Id;
                if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1)
                {
                    messages.Add(message with { Status = MessageStatus.Delivered });
                    claimed.Add(message.Id);
                }
                else
                {
                    messages.Add(message);
                }
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return (messages, claimed);
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
