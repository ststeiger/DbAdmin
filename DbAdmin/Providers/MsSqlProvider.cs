
namespace DbAdmin.Providers;


public sealed class MsSqlProvider
    : IDbProvider
{
    private readonly Microsoft.Data.SqlClient.SqlConnection _conn;
    private readonly System.Threading.SemaphoreSlim m_lock;

    public MsSqlProvider(string connectionString)
    {
        this._conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        this.m_lock = new System.Threading.SemaphoreSlim(1, 1);
    }


    public Models.DbProvider ProviderType => Models.DbProvider.MsSql;

    public async System.Threading.Tasks.Task OpenAsync(System.Threading.CancellationToken ct = default)
        => await _conn.OpenAsync(ct);

    // ── Helpers ─────────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task<
        System.Collections.Generic.List<T>
        > QueryAsync<T>(
        string sql,
        System.Func<Microsoft.Data.SqlClient.SqlDataReader, T> map,
        System.Action<Microsoft.Data.SqlClient.SqlCommand>? configure = null,
        System.Threading.CancellationToken ct = default
    )
    {
        await this.m_lock.WaitAsync(ct);

        try
        {
            await using Microsoft.Data.SqlClient.SqlCommand cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = 60;
            configure?.Invoke(cmd);

            if (_conn.State != System.Data.ConnectionState.Open)
                await _conn.OpenAsync(ct);

            await using Microsoft.Data.SqlClient.SqlDataReader rdr = await cmd.ExecuteReaderAsync(ct);
            System.Collections.Generic.List<T> list =
                new System.Collections.Generic.List<T>()
            ;

            while (await rdr.ReadAsync(ct))
                list.Add(map(rdr));

            return list;
        }
        finally
        {
            this.m_lock.Release();
        }

    }

    private static T? Val<T>(Microsoft.Data.SqlClient.SqlDataReader r, string col)
    {
        object v = r[col];

        if (v == System.DBNull.Value)
            return default;

        System.Type targetType = System.Nullable.GetUnderlyingType(typeof(T))
                          ?? typeof(T);

        return (T)System.Convert.ChangeType(v, targetType);
    }

    // ── Connection info ──────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task<Models.DatabaseInfo> GetDatabaseInfoAsync(
        System.Threading.CancellationToken ct = default
    )
    {
        const string sql = """
        SELECT
            DB_NAME() AS Name,
            @@VERSION AS Version,
            DATABASEPROPERTYEX(DB_NAME(),'Collation') AS Collation,
            create_date AS CreateDate,
            (SELECT SUM(size) * 8 FROM sys.database_files) AS SizeKb
        FROM sys.databases 
        WHERE name = DB_NAME()
        """;

        System.Collections.Generic.IEnumerable<Models.DatabaseInfo> results =
            await QueryAsync(sql, 
                delegate(Microsoft.Data.SqlClient.SqlDataReader r) 
                {
                    return new Models.DatabaseInfo(
                    r["Name"].ToString()!,
                    r["Version"].ToString()!.Split('\n')[0],
                    null,
                    r["Collation"]?.ToString(),
                    Val<long?>(r, "SizeKb"),
                    Val<System.DateTime?>(r, "CreateDate"));
                },
                ct: ct
        );

        // Manual implementation of First()
        foreach (Models.DatabaseInfo item in results)
            return item;

        throw new System.InvalidOperationException("Sequence contains no elements.");
    }

    // ── Schemas ──────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.SchemaInfo>
    > GetSchemasAsync(
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT s.name, p.name AS Owner, NULL AS Description
            FROM sys.schemas s
            LEFT JOIN sys.database_principals p ON s.principal_id = p.principal_id
            ORDER BY s.name
            """,
            r => new Models.SchemaInfo(r["name"].ToString()!, r["Owner"]?.ToString(), null), ct: ct);

    // ── Tables ───────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.TableInfo>
    > GetTablesAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync($"""
            SELECT
                s.name                        AS SchemaName,
                t.name                        AS TableName,
                p.rows                        AS "RowCount",
                'BASE TABLE'                  AS TableType,
                t.create_date                 AS CreateDate,
                t.modify_date                 AS ModifyDate,
                SUM(a.total_pages) * 8        AS SizeKb
            FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.indexes i ON t.object_id = i.object_id AND i.index_id IN (0,1)
            JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
            JOIN sys.allocation_units a ON p.partition_id = a.container_id
            WHERE (@schema IS NULL OR s.name = @schema)
            GROUP BY s.name, t.name, p.rows, t.create_date, t.modify_date
            ORDER BY s.name, t.name
            """,
            r => new Models.TableInfo(
                r["SchemaName"].ToString()!,
                r["TableName"].ToString()!,
                Val<long>(r, "RowCount"), "BASE TABLE",
                Val<System.DateTime?>(r, "CreateDate"),
                Val<System.DateTime?>(r, "ModifyDate"),
                Val<long?>(r, "SizeKb")
                ),
            cmd => cmd.Parameters.AddWithValue("@schema", (object?)schema ??
                System.DBNull.Value
                ), ct
            );

    // ── Views ────────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.TableInfo>
        > GetViewsAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync($"""
            SELECT s.name AS SchemaName, v.name AS ViewName,
                   v.create_date AS CreateDate, v.modify_date AS ModifyDate
            FROM sys.views v
            JOIN sys.schemas s ON v.schema_id = s.schema_id
            WHERE (@schema IS NULL OR s.name = @schema)
            ORDER BY s.name, v.name
            """,
            r => new Models.TableInfo(
                r["SchemaName"].ToString()!,
                r["ViewName"].ToString()!,
                0, "VIEW",
                Val<System.DateTime?>(r, "CreateDate"),
                Val<System.DateTime?>(r, "ModifyDate"), null),
            cmd => cmd.Parameters.AddWithValue("@schema",
                (object?)schema ?? System.DBNull.Value)
                , ct
            );

    // ── Columns ──────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.ColumnInfo>
        > GetColumnsAsync(
        string schema,
        string table,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                c.COLUMN_NAME,
                c.ORDINAL_POSITION,
                c.DATA_TYPE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.NUMERIC_PRECISION,
                c.NUMERIC_SCALE,
                c.IS_NULLABLE,
                c.COLUMN_DEFAULT,
                COLUMNPROPERTY(OBJECT_ID(c.TABLE_SCHEMA+'.'+c.TABLE_NAME), c.COLUMN_NAME,'IsIdentity') AS IsIdentity,
                CAST(CASE WHEN pk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsPK,
                CAST(CASE WHEN fk.COLUMN_NAME IS NOT NULL THEN 1 ELSE 0 END AS BIT) AS IsFK,
                ep.value AS Description
            FROM INFORMATION_SCHEMA.COLUMNS c
            LEFT JOIN (
                SELECT ku.COLUMN_NAME, ku.TABLE_SCHEMA, ku.TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
            ) pk ON pk.COLUMN_NAME = c.COLUMN_NAME AND pk.TABLE_SCHEMA = c.TABLE_SCHEMA AND pk.TABLE_NAME = c.TABLE_NAME
            LEFT JOIN (
                SELECT ku.COLUMN_NAME, ku.TABLE_SCHEMA, ku.TABLE_NAME
                FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc
                JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE ku
                    ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME
                WHERE tc.CONSTRAINT_TYPE = 'FOREIGN KEY'
            ) fk ON fk.COLUMN_NAME = c.COLUMN_NAME AND fk.TABLE_SCHEMA = c.TABLE_SCHEMA AND fk.TABLE_NAME = c.TABLE_NAME
            LEFT JOIN sys.extended_properties ep
                ON ep.major_id = OBJECT_ID(c.TABLE_SCHEMA+'.'+c.TABLE_NAME)
               AND ep.minor_id = c.ORDINAL_POSITION
               AND ep.name = 'MS_Description'
               AND ep.class = 1
            WHERE c.TABLE_SCHEMA = @schema AND c.TABLE_NAME = @table
            ORDER BY c.ORDINAL_POSITION
            """,
            r => new Models.ColumnInfo(
                r["COLUMN_NAME"].ToString()!,
                (int)r["ORDINAL_POSITION"],
                r["DATA_TYPE"].ToString()!,
                Val<int?>(r, "CHARACTER_MAXIMUM_LENGTH"),
                Val<int?>(r, "NUMERIC_PRECISION"),
                Val<int?>(r, "NUMERIC_SCALE"),
                r["IS_NULLABLE"].ToString() == "YES",
                (bool)r["IsPK"],
                (bool)r["IsFK"],
                System.Convert.ToInt32(r["IsIdentity"]) == 1,
                r["COLUMN_DEFAULT"]?.ToString(),
                r["Description"]?.ToString()),
            cmd =>
            {
                cmd.Parameters.AddWithValue("@schema", schema);
                cmd.Parameters.AddWithValue("@table", table);
            }, ct);

    // ── Indexes ──────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.IndexInfo>
        > GetIndexesAsync(
        string? schema = null,
        string? table = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                i.name AS IndexName
                ,s.name AS SchemaName
                ,t.name AS TableName
                ,i.type_desc AS IndexType
                ,i.is_unique AS IsUnique
                ,i.is_primary_key AS IsPrimaryKey
                ,i.is_disabled AS IsDisabled
                ,STRING_AGG(CASE WHEN ic.is_included_column = 0 THEN c.name END, ', ')
                    WITHIN GROUP (ORDER BY ic.key_ordinal) AS Columns
                ,STRING_AGG(CASE WHEN ic.is_included_column = 1 THEN c.name END, ', ') AS IncludedColumns 
            FROM sys.indexes AS i 
            JOIN sys.tables AS t ON i.object_id = t.object_id 
            JOIN sys.schemas AS s ON t.schema_id = s.schema_id 
            JOIN sys.index_columns AS ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id 
            JOIN sys.columns AS c ON ic.object_id = c.object_id AND ic.column_id = c.column_id 
            WHERE i.name IS NOT NULL 
            AND (@schema IS NULL OR s.name = @schema) 
            AND (@table  IS NULL OR t.name = @table) 
            GROUP BY i.name, s.name, t.name, i.type_desc, i.is_unique, i.is_primary_key, i.is_disabled 
            ORDER BY s.name, t.name, i.name 
            """,
            r => new Models.IndexInfo(
                r["IndexName"].ToString()!,
                r["SchemaName"].ToString()!,
                r["TableName"].ToString()!,
                r["IndexType"].ToString()!,
                (bool)r["IsUnique"],
                (bool)r["IsPrimaryKey"],
                (bool)r["IsDisabled"],
                System.Linq.Enumerable.ToList(
                (r["Columns"]?.ToString() ?? "")
                .Split(',',
                    System.StringSplitOptions.RemoveEmptyEntries
                    | System.StringSplitOptions.TrimEntries
                )),
                System.Linq.Enumerable.ToList(
                (r["IncludedColumns"]?.ToString() ?? "")
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries
                    | System.StringSplitOptions.TrimEntries
                ))),
            cmd =>
            {
                cmd.Parameters.AddWithValue("@schema", (object?)schema ??
                    System.DBNull.Value
                );

                cmd.Parameters.AddWithValue("@table", (object?)table ??
                    System.DBNull.Value
                );
            }, ct);

    // ── Foreign Keys ─────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.ForeignKeyInfo>
        > GetForeignKeysAsync(
        string? schema = null,
        string? table = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                fk.name                              AS FkName,
                s.name                               AS SchemaName,
                tp.name                              AS TableName,
                STRING_AGG(cp.name, ', ')
                    WITHIN GROUP (ORDER BY fkc.constraint_column_id) AS Columns,
                sr.name                              AS RefSchema,
                tr.name                              AS RefTable,
                STRING_AGG(cr.name, ', ')
                    WITHIN GROUP (ORDER BY fkc.constraint_column_id) AS RefColumns,
                fk.delete_referential_action_desc    AS OnDelete,
                fk.update_referential_action_desc    AS OnUpdate
            FROM sys.foreign_keys fk
            JOIN sys.tables       tp  ON fk.parent_object_id       = tp.object_id
            JOIN sys.schemas      s   ON tp.schema_id              = s.schema_id
            JOIN sys.tables       tr  ON fk.referenced_object_id   = tr.object_id
            JOIN sys.schemas      sr  ON tr.schema_id              = sr.schema_id
            JOIN sys.foreign_key_columns fkc ON fk.object_id       = fkc.constraint_object_id
            JOIN sys.columns      cp  ON fkc.parent_object_id      = cp.object_id AND fkc.parent_column_id      = cp.column_id
            JOIN sys.columns      cr  ON fkc.referenced_object_id  = cr.object_id AND fkc.referenced_column_id  = cr.column_id
            WHERE (@schema IS NULL OR s.name = @schema)
              AND (@table  IS NULL OR tp.name = @table)
            GROUP BY fk.name, s.name, tp.name, sr.name, tr.name,
                     fk.delete_referential_action_desc, fk.update_referential_action_desc
            ORDER BY s.name, tp.name, fk.name
            """,
            r => new Models.ForeignKeyInfo(
                r["FkName"].ToString()!,
                r["SchemaName"].ToString()!,
                r["TableName"].ToString()!,
                System.Linq.Enumerable.ToList(
                (r["Columns"]?.ToString() ?? "")
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries
                    | System.StringSplitOptions.TrimEntries)),
                r["RefSchema"].ToString()!,
                r["RefTable"].ToString()!,
                System.Linq.Enumerable.ToList(
                (r["RefColumns"]?.ToString() ?? "")
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries
                    | System.StringSplitOptions.TrimEntries)),
                r["OnDelete"].ToString()!,
                r["OnUpdate"].ToString()!),
            cmd =>
            {
                cmd.Parameters.AddWithValue("@schema", (object?)schema ??
                    System.DBNull.Value
                );

                cmd.Parameters.AddWithValue("@table", (object?)table ??
                    System.DBNull.Value
                );
            }, ct);

    // ── Procedures ───────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.ProcedureInfo>
    > GetProceduresAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT s.name AS SchemaName, p.name AS ProcName,
                   p.create_date AS CreateDate, p.modify_date AS ModifyDate
            FROM sys.procedures p
            JOIN sys.schemas s ON p.schema_id = s.schema_id
            WHERE (@schema IS NULL OR s.name = @schema)
            ORDER BY s.name, p.name
            """,
            r => new Models.ProcedureInfo(
                r["SchemaName"].ToString()!,
                r["ProcName"].ToString()!,
                "PROCEDURE",
                "",
                Val<System.DateTime?>(r, "CreateDate"),
                Val<System.DateTime?>(r, "ModifyDate"), null),
            cmd => cmd.Parameters.AddWithValue(
                "@schema",
                (object?)schema ??
                System.DBNull.Value
            )
            , ct
        );

    // ── Functions (scalar + table-valued) ────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.ProcedureInfo>
    > GetFunctionsAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                s.name    AS SchemaName,
                o.name    AS FuncName,
                o.type_desc AS TypeDesc,
                CASE o.type
                    WHEN 'FN'  THEN 'SCALAR'
                    WHEN 'IF'  THEN 'TABLE_INLINE'
                    WHEN 'TF'  THEN 'TABLE_MULTI'
                    WHEN 'FS'  THEN 'SCALAR_CLR'
                    WHEN 'FT'  THEN 'TABLE_CLR'
                    WHEN 'AF'  THEN 'AGGREGATE'
                    ELSE 'OTHER'
                END AS FuncType,
                o.create_date AS CreateDate,
                o.modify_date AS ModifyDate
            FROM sys.objects o
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            WHERE o.type IN ('FN','IF','TF','FS','FT','AF')
              AND (@schema IS NULL OR s.name = @schema)
            ORDER BY s.name, o.name
            """,
            r => new Models.ProcedureInfo(
                r["SchemaName"].ToString()!,
                r["FuncName"].ToString()!,
                "FUNCTION",
                r["FuncType"].ToString()!,
                Val<System.DateTime?>(r, "CreateDate"),
                Val<System.DateTime?>(r, "ModifyDate"), null),
            cmd => cmd.Parameters.AddWithValue(
                "@schema", (object?)schema ?? System.DBNull.Value)
            , ct
            );

    // ── Parameters ───────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.ProcedureParameter>
    > GetProcedureParametersAsync(
        string schema,
        string name,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                p.name           AS ParamName,
                p.parameter_id   AS OrdinalPos,
                t.name           AS DataType,
                p.is_output      AS IsOutput,
                p.has_default_value,
                p.default_value
            FROM sys.parameters p
            JOIN sys.types t ON p.user_type_id = t.user_type_id
            WHERE p.object_id = OBJECT_ID(@fullName)
            ORDER BY p.parameter_id
            """,
            r => new Models.ProcedureParameter(
                r["ParamName"].ToString()!,
                (int)r["OrdinalPos"],
                (bool)r["IsOutput"] ? "OUT" : "IN",
                r["DataType"].ToString()!,
                (bool)r["has_default_value"] ? r["default_value"]?.ToString() : null),
            cmd => cmd.Parameters.AddWithValue("@fullName", $"{schema}.{name}"), ct);

    // ── Object Definition ────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task<string?>
        GetObjectDefinitionAsync(
        string schema,
        string name,
        string objectType,
        System.Threading.CancellationToken ct = default
    )
    {
        await using Microsoft.Data.SqlClient.SqlCommand cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT OBJECT_DEFINITION(OBJECT_ID(@fullName))";
        cmd.Parameters.AddWithValue("@fullName", $"{schema}.{name}");
        object result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    // ── Triggers ─────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.TriggerInfo>
        > GetTriggersAsync(
        string? schema = null,
        string? table = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                tr.name                          AS TriggerName,
                s.name                           AS SchemaName,
                t.name                           AS TableName,
                te.type_desc                     AS EventType,
                CASE WHEN tr.is_instead_of_trigger = 1 THEN 'INSTEAD OF' ELSE 'AFTER' END AS Timing,
                tr.is_disabled                   AS IsDisabled,
                OBJECT_DEFINITION(tr.object_id)  AS Definition
            FROM sys.triggers tr
            JOIN sys.tables t ON tr.parent_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            JOIN sys.trigger_events te ON tr.object_id = te.object_id
            WHERE (@schema IS NULL OR s.name = @schema)
              AND (@table  IS NULL OR t.name = @table)
            ORDER BY s.name, t.name, tr.name
            """,
            r => new Models.TriggerInfo(
                r["TriggerName"].ToString()!,
                r["SchemaName"].ToString()!,
                r["TableName"].ToString()!,
                r["EventType"].ToString()!,
                r["Timing"].ToString()!,
                !(bool)r["IsDisabled"],
                r["Definition"]?.ToString()),
            cmd =>
            {
                cmd.Parameters.AddWithValue(
                    "@schema",
                    (object?)schema ??
                    System.DBNull.Value
                );

                cmd.Parameters.AddWithValue(
                    "@table",
                    (object?)table ?? System.DBNull.Value
                );
            }
            , ct
        );

    // ── Sequences ────────────────────────────────────────────────────────────

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.SequenceInfo>
        > GetSequencesAsync(
        string? schema = null,
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                s.name       AS SchemaName,
                seq.name     AS SeqName,
                t.name       AS DataType,
                seq.start_value,
                seq.increment,
                seq.minimum_value,
                seq.maximum_value,
                seq.is_cycling,
                seq.cache_size,
                seq.current_value
            FROM sys.sequences seq
            JOIN sys.schemas s ON seq.schema_id = s.schema_id
            JOIN sys.types   t ON seq.user_type_id = t.user_type_id
            WHERE (@schema IS NULL OR s.name = @schema)
            ORDER BY s.name, seq.name
            """,
            r => new Models.SequenceInfo(
                r["SchemaName"].ToString()!,
                r["SeqName"].ToString()!,
                r["DataType"].ToString()!,
                System.Convert.ToInt64(r["start_value"]),
                System.Convert.ToInt64(r["increment"]),
                r["minimum_value"] == System.DBNull.Value ? null : System.Convert.ToInt64(r["minimum_value"]),
                r["maximum_value"] == System.DBNull.Value ? null : System.Convert.ToInt64(r["maximum_value"]),
                (bool)r["is_cycling"],
                r["cache_size"] == System.DBNull.Value ? null : System.Convert.ToInt64(r["cache_size"]),
                r["current_value"] == System.DBNull.Value ? null : System.Convert.ToInt64(r["current_value"])),
            cmd => cmd.Parameters.AddWithValue("@schema", (object?)schema ?? System.DBNull.Value), ct);

    // ── Table Data ───────────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task<Models.TableDataResult> GetTableDataAsync(
        Models.TableDataRequest req,
        System.Threading.CancellationToken ct = default
    )
    {
        string quotedTable = $"[{req.Schema}].[{req.Table}]";
        int offset = (req.Page - 1) * req.PageSize;
        string orderBy = string.IsNullOrWhiteSpace(req.OrderBy) ? "(SELECT NULL)" : $"[{req.OrderBy}]";
        string direction = req.Descending ? "DESC" : "ASC";
        string where = string.IsNullOrWhiteSpace(req.Filter) ? "" : $"WHERE {req.Filter}";

        string countSql = $"SELECT COUNT_BIG(*) FROM {quotedTable} {where}";
        string dataSql = $"""
            SELECT * FROM {quotedTable} {where}
            ORDER BY {orderBy} {direction}
            OFFSET {offset} ROWS FETCH NEXT {req.PageSize} ROWS ONLY
            """;

        await using Microsoft.Data.SqlClient.SqlCommand countCmd = _conn.CreateCommand();
        countCmd.CommandText = countSql;
        long total = (long)(await countCmd.ExecuteScalarAsync(ct))!;

        await using Microsoft.Data.SqlClient.SqlCommand cmd = _conn.CreateCommand();
        cmd.CommandText = dataSql;
        await using Microsoft.Data.SqlClient.SqlDataReader rdr = await cmd.ExecuteReaderAsync(ct);

        // Initialize the list for column names
        System.Collections.Generic.List<string> cols = new System.Collections.Generic.List<string>();
        for (int i = 0; i < rdr.FieldCount; i++)
            cols.Add(rdr.GetName(i));

        // Initialize the list for rows
        System.Collections.Generic.List<System.Collections.Generic.List<object?>> rows =
            new System.Collections.Generic.List<System.Collections.Generic.List<object?>>();

        while (await rdr.ReadAsync(ct))
        {
            var row = new System.Collections.Generic.List<object?>();
            for (int i = 0; i < rdr.FieldCount; i++)
                row.Add(rdr.IsDBNull(i) ? null : rdr.GetValue(i));
            rows.Add(row);
        }
        return new Models.TableDataResult(cols, rows, total, req.Page, req.PageSize);
    }

    // ── Query Execution ──────────────────────────────────────────────────────

    public async System.Threading.Tasks.Task<Models.QueryResult> ExecuteQueryAsync(
        Models.QueryRequest request,
        System.Threading.CancellationToken ct = default
    )
    {
        System.Diagnostics.Stopwatch sw =
            System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = request.Sql;
            cmd.CommandTimeout = request.TimeoutSeconds;

            // Try as reader first; fall back to non-query
            System.Collections.Generic.List<string> cols = [];
            System.Collections.Generic.List<
                System.Collections.Generic.List<object?>
                > rows = [];

            int affected = 0;

            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            if (rdr.HasRows || rdr.FieldCount > 0)
            {
                cols = new System.Collections.Generic.List<string>();

                for (int i = 0; i < rdr.FieldCount; i++)
                    cols.Add(rdr.GetName(i));

                int count = 0;
                while (await rdr.ReadAsync(ct) && count < request.MaxRows)
                {
                    System.Collections.Generic.List<object?> row =
                        new System.Collections.Generic.List<object?>();
                    for (int i = 0; i < rdr.FieldCount; i++)
                        row.Add(rdr.IsDBNull(i) ? null : rdr.GetValue(i));
                    rows.Add(row);
                    count++;
                }
            }
            affected = rdr.RecordsAffected;
            sw.Stop();
            return new Models.QueryResult(true, cols, rows, affected, sw.ElapsedMilliseconds, null);
        }
        catch (System.Exception ex)
        {
            sw.Stop();
            return new Models.QueryResult(
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

    public async System.Threading.Tasks.Task<Models.DdlResult> GetCreateScriptAsync(
        string schema,
        string name,
        string objectType,
        System.Threading.CancellationToken ct = default
    )
    {
        var def = await GetObjectDefinitionAsync(schema, name, objectType, ct);
        return def is null
            ? new Models.DdlResult(false, "Could not retrieve definition.", null)
            : new Models.DdlResult(true, null, def);
    }

    public async System.Threading.Tasks.Task<Models.DdlResult> TruncateTableAsync(
        string schema,
        string table,
        System.Threading.CancellationToken ct = default
    )
    {
        try
        {
            await using var cmd = _conn.CreateCommand();
            cmd.CommandText = $"TRUNCATE TABLE [{schema}].[{table}]";
            await cmd.ExecuteNonQueryAsync(ct);
            return new Models.DdlResult(true, null, null);
        }
        catch (System.Exception ex)
        {
            return new Models.DdlResult(false, ex.Message, null);
        }
    }

    public async System.Threading.Tasks.Task<Models.DdlResult> DropObjectAsync(
        string schema,
        string name,
        string objectType,
        System.Threading.CancellationToken ct = default
    )
    {
        var ddl = objectType.ToUpperInvariant() switch
        {
            "TABLE" => $"DROP TABLE IF EXISTS [{schema}].[{name}]",
            "VIEW" => $"DROP VIEW IF EXISTS [{schema}].[{name}]",
            "PROCEDURE" => $"DROP PROCEDURE IF EXISTS [{schema}].[{name}]",
            "FUNCTION" => $"DROP FUNCTION IF EXISTS [{schema}].[{name}]",
            "TRIGGER" => $"DROP TRIGGER IF EXISTS [{schema}].[{name}]",
            "SEQUENCE" => $"DROP SEQUENCE IF EXISTS [{schema}].[{name}]",
            _ => throw new System.ArgumentException($"Unsupported object type: {objectType}")
        };
        try
        {
            await using Microsoft.Data.SqlClient.SqlCommand cmd = _conn.CreateCommand();
            cmd.CommandText = ddl;
            await cmd.ExecuteNonQueryAsync(ct);
            return new Models.DdlResult(true, null, ddl);
        }
        catch (System.Exception ex)
        {
            return new Models.DdlResult(false, ex.Message, ddl);
        }
    }

    public System.Threading.Tasks.Task<
        System.Collections.Generic.List<Models.TablespaceInfo>
        > GetTablespacesAsync(
        System.Threading.CancellationToken ct = default
    ) =>
        QueryAsync("""
            SELECT
                ds.name AS Name,
                mf.physical_name AS Location,
                mf.size * 8 AS SizeKb
            FROM sys.data_spaces ds
            LEFT JOIN sys.master_files mf ON mf.database_id = DB_ID() AND mf.data_space_id = ds.data_space_id
            ORDER BY ds.name
            """,
            r => new Models.TablespaceInfo(
                r["Name"].ToString()!,
                r["Location"]?.ToString(),
                Val<long?>(r, "SizeKb")
                )
            , ct: ct
        );

    public async System.Threading.Tasks.ValueTask DisposeAsync()
    {
        await this._conn.DisposeAsync();
    }
}
