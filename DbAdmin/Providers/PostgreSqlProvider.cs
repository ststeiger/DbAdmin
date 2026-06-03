
namespace DbAdmin.Providers;

using DbAdmin.Models;


public sealed class PostgreSqlProvider 
    : IDbProvider
{
    private readonly Npgsql.NpgsqlConnection _conn;


    public PostgreSqlProvider(string connectionString)
    {
        this._conn = new Npgsql.NpgsqlConnection(connectionString);
    }


    public DbProvider ProviderType
    {
        get
        {
            return DbProvider.PostgreSql;
        }
    }


    public async System.Threading.Tasks.Task OpenAsync(
        System.Threading.CancellationToken ct = default
    )
    {
        await this._conn.OpenAsync(ct);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task<
        System.Collections.Generic.List<T>
        > QueryAsync<T>(
        string sql, 
        System.Func<Npgsql.NpgsqlDataReader, T> map,
        System.Action<Npgsql.NpgsqlCommand>? configure = null, 
        System.Threading.CancellationToken ct = default
    )
    {
        await using Npgsql.NpgsqlCommand cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 60;
        configure?.Invoke(cmd);
        await using Npgsql.NpgsqlDataReader rdr = await cmd.ExecuteReaderAsync(ct);
        System.Collections.Generic.List<T> list = new System.Collections.Generic.List<T>();
        while (await rdr.ReadAsync(ct)) list.Add(map(rdr));
        return list;
    }

    private static T? Val<T>(
        Npgsql.NpgsqlDataReader r, 
        string col
    )
    {
        object v = r[col];
        return v == System.DBNull.Value ? default : 
            (T)System.Convert.ChangeType(v, typeof(T) )
        ;
    }

    // ── Connection info ──────────────────────────────────────────────────────
    public async System.Threading.Tasks.Task<DatabaseInfo> GetDatabaseInfoAsync(
    System.Threading.CancellationToken ct = default
)
    {
        System.Collections.Generic.List<DatabaseInfo> results = 
            await QueryAsync("""
        SELECT 
             current_database() AS name 
            ,version() AS version 
            ,pg_encoding_to_char(encoding) AS encoding 
            ,datcollate AS collation 
            ,pg_database_size(current_database()) / 1024 AS size_kb 
            ,NULL::timestamptz AS create_date 
        FROM pg_database 
        WHERE datname = current_database() 
        """,
            r => new DatabaseInfo(
                r["name"].ToString()!,
                r["version"].ToString()!,
                r["encoding"]?.ToString(),
                r["collation"]?.ToString(),
                Val<long?>(r, "size_kb"),
                null
            ), 
            ct: ct
        );

        // Manual implementation of First()
        foreach (DatabaseInfo? item in results)
        {
            return item;
        }

        throw new System.InvalidOperationException("Sequence contains no elements.");
    }


    // ── Schemas ──────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<SchemaInfo>
    > GetSchemasAsync(
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT n.nspname AS name, pg_catalog.pg_get_userbyid(n.nspowner) AS owner,
                   obj_description(n.oid, 'pg_namespace') AS description
            FROM pg_catalog.pg_namespace n
            WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
            ORDER BY n.nspname
            """,
            r => new SchemaInfo(
                r["name"].ToString()!, 
                r["owner"]?.ToString(), 
                r["description"]?.ToString()
                )
            , ct: ct
        );

    // ── Tables ───────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<TableInfo>
        > GetTablesAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                n.nspname                                       AS schema_name,
                c.relname                                       AS table_name,
                pg_stat_get_live_tuples(c.oid)                  AS row_count,
                pg_total_relation_size(c.oid) / 1024            AS size_kb
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r'
              AND n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              AND ($1::text IS NULL OR n.nspname = $1)
            ORDER BY n.nspname, c.relname
            """,
            r => new TableInfo(r["schema_name"].ToString()!, r["table_name"].ToString()!,
                Val<long>(r, "row_count"), "BASE TABLE", null, null, Val<long?>(r, "size_kb")),
            cmd => cmd.Parameters.AddWithValue(schema as object ?? System.DBNull.Value), ct);

    // ── Views ────────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<TableInfo>> GetViewsAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                 n.nspname AS schema_name 
                ,c.relname AS view_name 
                ,c.relkind  AS kind 
            FROM pg_catalog.pg_class AS c 
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace 
            WHERE c.relkind IN ('v','m') 
            AND n.nspname !~ '^pg_' AND n.nspname <> 'information_schema' 
            AND ($1::text IS NULL OR n.nspname = $1) 
            ORDER BY n.nspname, c.relname 
            """,
            r => new TableInfo(r["schema_name"].ToString()!, r["view_name"].ToString()!,
                0, r["kind"].ToString() == "m" ? "MATERIALIZED VIEW" : "VIEW", null, null, null),
            cmd => cmd.Parameters.AddWithValue(schema as object ?? 
                System.DBNull.Value
                ), ct);

    // ── Columns ──────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<ColumnInfo>> GetColumnsAsync(
        string schema, 
        string table,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT 
                a.attname AS column_name,
                a.attnum AS ordinal_position,
                pg_catalog.format_type(a.atttypid, a.atttypmod) AS data_type,
                a.atttypmod AS max_length,
                NULL::int AS numeric_precision,
                NULL::int AS numeric_scale,
                NOT a.attnotnull AS is_nullable,
                pg_catalog.pg_get_expr(d.adbin, d.adrelid) AS column_default,
                (a.attidentity != '') AS is_identity,
                COALESCE((
                    SELECT true FROM pg_constraint pk
                    JOIN pg_attribute pa ON pa.attnum = ANY(pk.conkey) AND pa.attrelid = pk.conrelid
                    WHERE pk.contype = 'p' AND pk.conrelid = a.attrelid AND pa.attnum = a.attnum
                    LIMIT 1), false) AS is_pk,
                COALESCE((
                    SELECT true FROM pg_constraint fk
                    JOIN pg_attribute fa ON fa.attnum = ANY(fk.conkey) AND fa.attrelid = fk.conrelid
                    WHERE fk.contype = 'f' AND fk.conrelid = a.attrelid AND fa.attnum = a.attnum
                    LIMIT 1), false) AS is_fk,
                col_description(a.attrelid, a.attnum) AS description
            FROM pg_catalog.pg_attribute AS a
            JOIN pg_catalog.pg_class AS c ON c.oid = a.attrelid
            JOIN pg_catalog.pg_namespace AS n ON n.oid = c.relnamespace
            LEFT JOIN pg_catalog.pg_attrdef AS d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE c.relname = $1 AND n.nspname = $2 AND a.attnum > 0 AND NOT a.attisdropped
            ORDER BY a.attnum
            """,
            r => new ColumnInfo(
                r["column_name"].ToString()!,
                (int)r["ordinal_position"],
                r["data_type"].ToString()!,
                null, null, null,
                (bool)r["is_nullable"],
                (bool)r["is_pk"],
                (bool)r["is_fk"],
                (bool)r["is_identity"],
                r["column_default"]?.ToString(),
                r["description"]?.ToString()),
            cmd => {
                cmd.Parameters.AddWithValue(table);
                cmd.Parameters.AddWithValue(schema);
            }, ct);

    // ── Indexes ──────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<IndexInfo>
        > GetIndexesAsync(
        string? schema = null, 
        string? table = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                i.relname                           AS index_name,
                n.nspname                           AS schema_name,
                t.relname                           AS table_name,
                am.amname                           AS index_type,
                ix.indisunique                      AS is_unique,
                ix.indisprimary                     AS is_pk,
                NOT ix.indisready                   AS is_disabled,
                pg_get_indexdef(ix.indexrelid)      AS index_def
            FROM pg_index ix
            JOIN pg_class t  ON t.oid = ix.indrelid
            JOIN pg_class i  ON i.oid = ix.indexrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            JOIN pg_am am    ON am.oid = i.relam
            WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              AND ($1::text IS NULL OR n.nspname = $1)
              AND ($2::text IS NULL OR t.relname  = $2)
            ORDER BY n.nspname, t.relname, i.relname
            """,
            r => new IndexInfo(
                r["index_name"].ToString()!,
                r["schema_name"].ToString()!,
                r["table_name"].ToString()!,
                r["index_type"].ToString()!,
                (bool)r["is_unique"],
                (bool)r["is_pk"],
                (bool)r["is_disabled"],
                [], []),          // columns parsed from index_def for brevity
            cmd => {
                cmd.Parameters.AddWithValue(schema as object ?? System.DBNull.Value);
                cmd.Parameters.AddWithValue(table as object ?? System.DBNull.Value);
            }, ct);

    // ── Foreign Keys ─────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<ForeignKeyInfo>
        > GetForeignKeysAsync(
        string? schema = null, 
        string? table = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                c.conname                                  AS fk_name,
                n.nspname                                  AS schema_name,
                cl.relname                                 AS table_name,
                STRING_AGG(a.attname, ',' ORDER BY u.pos) AS columns,
                rn.nspname                                 AS ref_schema,
                rcl.relname                                AS ref_table,
                STRING_AGG(ra.attname,',' ORDER BY u.pos)  AS ref_columns,
                c.confdeltype                              AS on_delete,
                c.confupdtype                              AS on_update
            FROM pg_constraint c
            JOIN pg_class cl ON cl.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = cl.relnamespace
            JOIN pg_class rcl ON rcl.oid = c.confrelid
            JOIN pg_namespace rn ON rn.oid = rcl.relnamespace
            JOIN LATERAL UNNEST(c.conkey) WITH ORDINALITY AS u(col, pos) ON TRUE
            JOIN pg_attribute a ON a.attrelid = c.conrelid AND a.attnum = u.col
            JOIN LATERAL UNNEST(c.confkey) WITH ORDINALITY AS ur(col, pos) ON ur.pos = u.pos
            JOIN pg_attribute ra ON ra.attrelid = c.confrelid AND ra.attnum = ur.col
            WHERE c.contype = 'f'
              AND ($1::text IS NULL OR n.nspname = $1)
              AND ($2::text IS NULL OR cl.relname = $2)
            GROUP BY c.conname, n.nspname, cl.relname, rn.nspname, rcl.relname, c.confdeltype, c.confupdtype
            ORDER BY n.nspname, cl.relname, c.conname
            """,
            r => new ForeignKeyInfo(
                r["fk_name"].ToString()!,
                r["schema_name"].ToString()!,
                r["table_name"].ToString()!,
                System.Linq.Enumerable.ToList(  r["columns"].ToString()!.Split(',')),
                r["ref_schema"].ToString()!,
                r["ref_table"].ToString()!,
                System.Linq.Enumerable.ToList(r["ref_columns"].ToString()!.Split(',')),
                MapRefAction(r["on_delete"].ToString()!),
                MapRefAction(r["on_update"].ToString()!)),
            cmd => {
                cmd.Parameters.AddWithValue(schema as object ?? System.DBNull.Value);
                cmd.Parameters.AddWithValue(table as object ?? System.DBNull.Value);
            }, ct);

    private static string MapRefAction(string code) => code switch
    {
        "a" => "NO ACTION", "r" => "RESTRICT", "c" => "CASCADE",
        "n" => "SET NULL",  "d" => "SET DEFAULT", _ => code
    };

    // ── Procedures ───────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<ProcedureInfo>
        > GetProceduresAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT n.nspname AS schema_name, p.proname AS proc_name,
                   p.prokind AS kind
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE p.prokind = 'p'
              AND n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              AND ($1::text IS NULL OR n.nspname = $1)
            ORDER BY n.nspname, p.proname
            """,
            r => new ProcedureInfo(r["schema_name"].ToString()!, r["proc_name"].ToString()!,
                "PROCEDURE", "", null, null, null),
            cmd => cmd.Parameters.AddWithValue(schema as object ?? System.DBNull.Value), ct);

    // ── Functions ────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<ProcedureInfo>
        > GetFunctionsAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                n.nspname                                  AS schema_name,
                p.proname                                  AS func_name,
                CASE p.prokind
                    WHEN 'f' THEN CASE WHEN p.proretset THEN 'TABLE' ELSE 'SCALAR' END
                    WHEN 'a' THEN 'AGGREGATE'
                    WHEN 'w' THEN 'WINDOW'
                    ELSE 'OTHER'
                END AS func_type
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE p.prokind IN ('f','a','w')
              AND n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
              AND ($1::text IS NULL OR n.nspname = $1)
            ORDER BY n.nspname, p.proname
            """,
            r => new ProcedureInfo(r["schema_name"].ToString()!, r["func_name"].ToString()!,
                "FUNCTION", r["func_type"].ToString()!, null, null, null),
            cmd => cmd.Parameters.AddWithValue(schema as object ?? System.DBNull.Value), ct);

    // ── Parameters ───────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<ProcedureParameter>
        > GetProcedureParametersAsync(
        string schema, 
        string name,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                p.parameter_name,
                p.ordinal_position,
                p.parameter_mode,
                p.data_type,
                p.parameter_default
            FROM information_schema.parameters p
            WHERE p.specific_schema = $1 AND p.specific_name LIKE $2 || '%'
            ORDER BY p.ordinal_position
            """,
            r => new ProcedureParameter(
                r["parameter_name"]?.ToString() ?? "",
                (int)r["ordinal_position"],
                r["parameter_mode"].ToString()!,
                r["data_type"].ToString()!,
                r["parameter_default"]?.ToString()),
            cmd => {
                cmd.Parameters.AddWithValue(schema);
                cmd.Parameters.AddWithValue(name);
            }, ct);

    // ── Object Definition ────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task<string?> GetObjectDefinitionAsync(
        string schema, 
        string name, 
        string objectType,
        System.Threading.CancellationToken ct = default
    )
    {
        await using Npgsql.NpgsqlCommand cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT pg_get_functiondef(p.oid)
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = $1 AND p.proname = $2
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue(schema);
        cmd.Parameters.AddWithValue(name);
        object? result = await cmd.ExecuteScalarAsync(ct);
        if (result is not null && result != System.DBNull.Value)
            return result.ToString();

        // Try view
        cmd.CommandText = """
            SELECT pg_get_viewdef(c.oid, true)
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = $1 AND c.relname = $2
            LIMIT 1
            """;
        result = await cmd.ExecuteScalarAsync(ct);
        return result == System.DBNull.Value ? null : result?.ToString();
    }

    // ── Triggers ─────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<TriggerInfo>
        > GetTriggersAsync(
        string? schema = null, 
        string? table = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                t.trigger_name,
                t.trigger_schema AS schema_name,
                t.event_object_table AS table_name,
                t.event_manipulation AS event,
                t.action_timing AS timing,
                'true' AS is_enabled,
                t.action_statement AS definition
            FROM information_schema.triggers t
            WHERE ($1::text IS NULL OR t.trigger_schema = $1)
              AND ($2::text IS NULL OR t.event_object_table = $2)
            ORDER BY t.trigger_schema, t.event_object_table, t.trigger_name
            """,
            r => new TriggerInfo(
                r["trigger_name"].ToString()!,
                r["schema_name"].ToString()!,
                r["table_name"].ToString()!,
                r["event"].ToString()!,
                r["timing"].ToString()!,
                true,
                r["definition"]?.ToString()),
            cmd => {
                cmd.Parameters.AddWithValue(schema as object ?? System.DBNull.Value);
                cmd.Parameters.AddWithValue(table as object ?? System.DBNull.Value);
            }, ct);

    // ── Sequences ────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<SequenceInfo>
        > GetSequencesAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                sequence_schema,
                sequence_name,
                data_type,
                start_value::bigint,
                increment::bigint,
                minimum_value::bigint,
                maximum_value::bigint,
                cycle_option,
                NULL::bigint AS cache_size
            FROM information_schema.sequences
            WHERE ($1::text IS NULL OR sequence_schema = $1)
            ORDER BY sequence_schema, sequence_name
            """,
            r => new SequenceInfo(
                r["sequence_schema"].ToString()!,
                r["sequence_name"].ToString()!,
                r["data_type"].ToString()!,
                Val<long>(r, "start_value"),
                Val<long>(r, "increment"),
                Val<long?>(r, "minimum_value"),
                Val<long?>(r, "maximum_value"),
                r["cycle_option"].ToString() == "YES",
                null, null),
            cmd => cmd.Parameters.AddWithValue(schema as object ?? System.DBNull.Value), ct);

    // ── Table Data ───────────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task<TableDataResult> GetTableDataAsync(
        TableDataRequest req,
        System.Threading.CancellationToken ct = default
    )
    {
        string quotedTable = $"\"{req.Schema}\".\"{req.Table}\"";
        int offset = (req.Page - 1) * req.PageSize;
        string orderBy = string.IsNullOrWhiteSpace(req.OrderBy) ? "1" : $"\"{req.OrderBy}\"";
        string direction = req.Descending ? "DESC" : "ASC";
        string where = string.IsNullOrWhiteSpace(req.Filter) ? "" : $"WHERE {req.Filter}";

        await using Npgsql.NpgsqlCommand countCmd = _conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM {quotedTable} {where}";
        long total = System.Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));

        await using Npgsql.NpgsqlCommand cmd = _conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT * FROM {quotedTable} {where}
            ORDER BY {orderBy} {direction}
            LIMIT {req.PageSize} OFFSET {offset}
            """;
        await using Npgsql.NpgsqlDataReader rdr = 
            await cmd.ExecuteReaderAsync(ct)
        ;

        System.Collections.Generic.List<string> cols = 
            new System.Collections.Generic.List<string>();

        for (int i = 0; i < rdr.FieldCount; i++)
            cols.Add(rdr.GetName(i));

        System.Collections.Generic.List<System.Collections.Generic.List<object?>> rows =
            new System.Collections.Generic.List<System.Collections.Generic.List<object?>>();

        while (await rdr.ReadAsync(ct))
        {
            System.Collections.Generic.List<object?> row = 
                new System.Collections.Generic.List<object?>();

            for (int i = 0; i < rdr.FieldCount; i++)
                row.Add(rdr.IsDBNull(i) ? null : rdr.GetValue(i));
            rows.Add(row);
        }
        return new TableDataResult(cols, rows, total, req.Page, req.PageSize);
    }

    // ── Query Execution ──────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task<QueryResult> ExecuteQueryAsync(
        QueryRequest request,
        System.Threading.CancellationToken ct = default
    )
    {
        System.Diagnostics.Stopwatch sw = 
            System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using Npgsql.NpgsqlCommand cmd = _conn.CreateCommand();
            cmd.CommandText = request.Sql;
            cmd.CommandTimeout = request.TimeoutSeconds;

            await using Npgsql.NpgsqlDataReader rdr = 
                await cmd.ExecuteReaderAsync(ct)
            ;
            
            System.Collections.Generic.List<string> cols = 
                new System.Collections.Generic.List<string>();

            for (int i = 0; i < rdr.FieldCount; i++)
                cols.Add(rdr.GetName(i));

            System.Collections.Generic.List<System.Collections.Generic.List<object?>> rows = 
                new System.Collections.Generic.List<System.Collections.Generic.List<object?>>();
            
            int count = 0;
            while (await rdr.ReadAsync(ct) && count < request.MaxRows)
            {
                System.Collections.Generic.List<object?> row = 
                    new System.Collections.Generic.List<object?>()
                ;

                for (int i = 0; i < rdr.FieldCount; i++)
                    row.Add(rdr.IsDBNull(i) ? null : rdr.GetValue(i));
                rows.Add(row);
                count++;
            }
            sw.Stop();
            return new QueryResult(
                true, 
                cols, 
                rows, 
                rdr.RecordsAffected, 
                sw.ElapsedMilliseconds, 
                null
            );
        }
        catch (System.Exception ex)
        {
            sw.Stop();
            return new QueryResult(
                false, 
                [], 
                [], 
                0, 
                sw.ElapsedMilliseconds, 
                ex.Message
            );
        }
    }

    // ── DDL helpers ──────────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task<DdlResult> GetCreateScriptAsync(
        string schema, 
        string name, 
        string objectType,
        System.Threading.CancellationToken ct = default
    )
    {
        string? def = await GetObjectDefinitionAsync(schema, name, objectType, ct);
        return def is null
            ? new DdlResult(false, "Could not retrieve definition.", null)
            : new DdlResult(true, null, def);
    }

    public async System.Threading.Tasks.Task<DdlResult> TruncateTableAsync(
        string schema, 
        string table,
        System.Threading.CancellationToken ct = default
    )
    {
        try
        {
            await using Npgsql.NpgsqlCommand cmd = _conn.CreateCommand();
            cmd.CommandText = $"TRUNCATE TABLE \"{schema}\".\"{table}\"";
            await cmd.ExecuteNonQueryAsync(ct);
            return new DdlResult(true, null, null);
        }
        catch (System.Exception ex) 
        { 
            return new DdlResult(false, ex.Message, null); 
        }
    }

    public async System.Threading.Tasks.Task<DdlResult> DropObjectAsync(
        string schema, 
        string name, 
        string objectType,
        System.Threading.CancellationToken ct = default
    )
    {
        string ddl = objectType.ToUpperInvariant() switch
        {
            "TABLE"     => $"DROP TABLE IF EXISTS \"{schema}\".\"{name}\"",
            "VIEW"      => $"DROP VIEW IF EXISTS \"{schema}\".\"{name}\"",
            "PROCEDURE" => $"DROP PROCEDURE IF EXISTS \"{schema}\".\"{name}\"",
            "FUNCTION"  => $"DROP FUNCTION IF EXISTS \"{schema}\".\"{name}\"",
            "TRIGGER"   => $"DROP TRIGGER IF EXISTS \"{name}\" ON \"{schema}\".\"{name}\"",
            "SEQUENCE"  => $"DROP SEQUENCE IF EXISTS \"{schema}\".\"{name}\"",
            _ => throw new System.ArgumentException($"Unsupported object type: {objectType}")
        };
        try
        {
            await using System.Data.Common.DbCommand cmd = 
                _conn.CreateCommand()
            ;

            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync(ct);
            return new DdlResult(true, null, ddl);
        }
        catch (System.Exception ex) 
        { 
            return new DdlResult(false, ex.Message, ddl); 
        }
    }

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<TablespaceInfo>
        > GetTablespacesAsync(
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT spcname AS name,
                   pg_tablespace_location(oid) AS location,
                   pg_tablespace_size(oid) / 1024 AS size_kb
            FROM pg_tablespace
            ORDER BY spcname
            """,
            r => new TablespaceInfo(r["name"]
                .ToString()!
                , r["location"]?.ToString()
                , Val<long?>(r, "size_kb"))
            , ct: ct
        );

    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await _conn.DisposeAsync();
    }

}
