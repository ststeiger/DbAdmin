
namespace DbAdmin.Models;

// global 
public record ErrorResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("error")] string Error
);



// ─── Connection ───────────────────────────────────────────────────────────────

public enum DbProvider { MsSql, PostgreSql }

public record ConnectionRequest(
    DbProvider Provider,
    string Host,
    int Port,
    string Database,
    string Username,
    string Password,
    bool TrustServerCertificate = true
);

public record ConnectionInfo(
    string ConnectionId,
    DbProvider Provider,
    string Host,
    int Port,
    string Database,
    string Username,
    System.DateTime ConnectedAt
);

// ─── Schema Objects ───────────────────────────────────────────────────────────

public record SchemaInfo(
    string Name, 
    string? Owner,
    string? Description
);

public record TableInfo(
    string Schema,
    string Name,
    long RowCount,
    string TableType, // BASE TABLE | VIEW
    System.DateTime? CreateDate,
   System.DateTime? ModifyDate,
    long? SizeKb
);

public record ColumnInfo(
    string Name,
    int OrdinalPosition,
    string DataType,
    int? MaxLength,
    int? Precision,
    int? Scale,
    bool IsNullable,
    bool IsPrimaryKey,
    bool IsForeignKey,
    bool IsIdentity,
    string? DefaultValue,
    string? Description
);

public record IndexInfo(
    string Name,
    string Schema,
    string Table,
    string IndexType,
    bool IsUnique,
    bool IsPrimaryKey,
    bool IsDisabled,
    System.Collections.Generic.List<string> Columns,
    System.Collections.Generic.List<string> IncludedColumns
);

public record ForeignKeyInfo(
    string Name,
    string Schema,
    string Table,
    System.Collections.Generic.List<string> Columns,
    string ReferencedSchema,
    string ReferencedTable,
    System.Collections.Generic.List<string> ReferencedColumns,
    string OnDelete,
    string OnUpdate
);

public record ProcedureInfo(
    string Schema,
    string Name,
    string ObjectType, // PROCEDURE | FUNCTION
    string FunctionType, // SCALAR | TABLE | AGGREGATE | ""
    System.DateTime? CreateDate,
    System.DateTime? ModifyDate,
    string? Definition
);

public record ProcedureParameter(
    string Name,
    int OrdinalPosition,
    string Mode, // IN | OUT | INOUT
    string DataType,
    string? DefaultValue
);

public record TriggerInfo(
    string Name,
    string Schema,
    string Table,
    string Event, // INSERT, UPDATE, DELETE
    string Timing, // BEFORE | AFTER | INSTEAD OF
    bool IsEnabled,
    string? Definition
);

public record SequenceInfo(
    string Schema,
    string Name,
    string DataType,
    long StartValue,
    long Increment,
    long? MinValue,
    long? MaxValue,
    bool IsCyclic,
    long? CacheSize,
    long? CurrentValue
);

public record ViewInfo(
    string Schema,
    string Name,
    bool IsMaterialized,
    System.DateTime? CreateDate,
    System.DateTime? ModifyDate,
    string? Definition
);

// ─── Query Execution ─────────────────────────────────────────────────────────

public record QueryRequest(
    string Sql, 
    int MaxRows = 1000, 
    int TimeoutSeconds = 30
);

public record QueryResult(
    bool Success,
    System.Collections.Generic.List<string> Columns,
    System.Collections.Generic.List<System.Collections.Generic.List<object?>> Rows,
    int RowsAffected,
    long ElapsedMs,
    string? Error
);

// ─── DDL ─────────────────────────────────────────────────────────────────────

public record DdlResult(bool Success, string? Error, string? Script);

// ─── Table Data ───────────────────────────────────────────────────────────────

public record TableDataRequest(
    string Schema,
    string Table,
    int Page = 1,
    int PageSize = 100,
    string? OrderBy = null,
    bool Descending = false,
    string? Filter = null
);

public record TableDataResult(
    System.Collections.Generic.List<string> Columns,
    System.Collections.Generic.List<System.Collections.Generic.List<object?>> Rows,
    long TotalCount,
    int Page,
    int PageSize
);

// ─── Database Info ────────────────────────────────────────────────────────────

public record DatabaseInfo(
    string Name,
    string Version,
    string? Encoding,
    string? Collation,
    long? SizeKb,
   System.DateTime? CreateDate
);

public record TablespaceInfo(
    string Name, 
    string? Location, 
    long? SizeKb
);
