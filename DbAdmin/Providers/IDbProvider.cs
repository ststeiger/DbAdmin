
namespace DbAdmin.Providers;


using DbAdmin.Models;


/// <summary>
/// Contract every database provider must implement.
/// Add a new provider by implementing this interface and registering it
/// in ProviderFactory.
/// </summary>
public interface IDbProvider 
    : System.IAsyncDisposable
{
    DbProvider ProviderType { get; }

    // ── Connection ──────────────────────────────────────────────────────────
    System.Threading.Tasks.Task OpenAsync(System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<DatabaseInfo> GetDatabaseInfoAsync(
        System.Threading.CancellationToken ct = default
    );

    // ── Schema objects ──────────────────────────────────────────────────────
    System.Threading.Tasks.Task<System.Collections.Generic.List<SchemaInfo>> GetSchemasAsync(System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<TableInfo>> GetTablesAsync(string? schema = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<TableInfo>> GetViewsAsync(string? schema = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<ColumnInfo>> GetColumnsAsync(string schema, string table, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<IndexInfo>> GetIndexesAsync(string? schema = null, string? table = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<ForeignKeyInfo>> GetForeignKeysAsync(string? schema = null, string? table = null, System.Threading.CancellationToken ct = default);

    // ── Programmability ──────────────────────────────────────────────────────
    System.Threading.Tasks.Task<System.Collections.Generic.List<ProcedureInfo>> GetProceduresAsync(string? schema = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<ProcedureInfo>> GetFunctionsAsync(string? schema = null, System.Threading.CancellationToken ct = default);          // scalar + table-valued
    System.Threading.Tasks.Task<System.Collections.Generic.List<ProcedureParameter>> GetProcedureParametersAsync(string schema, string name, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<string?> GetObjectDefinitionAsync(string schema, string name, string objectType, System.Threading.CancellationToken ct = default);

    // ── Triggers, Sequences ──────────────────────────────────────────────────
    System.Threading.Tasks.Task<System.Collections.Generic.List<TriggerInfo>> GetTriggersAsync(string? schema = null, string? table = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<SequenceInfo>> GetSequencesAsync(string? schema = null, System.Threading.CancellationToken ct = default);

    // ── Data ────────────────────────────────────────────────────────────────
    System.Threading.Tasks.Task<TableDataResult> GetTableDataAsync(TableDataRequest request, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<QueryResult> ExecuteQueryAsync(QueryRequest request, System.Threading.CancellationToken ct = default);

    // ── DDL helpers ──────────────────────────────────────────────────────────
    System.Threading.Tasks.Task<DdlResult> GetCreateScriptAsync(string schema, string name, string objectType, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<DdlResult> TruncateTableAsync(string schema, string table, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<DdlResult> DropObjectAsync(string schema, string name, string objectType, System.Threading.CancellationToken ct = default);

    // ── Stats / misc ─────────────────────────────────────────────────────────
    System.Threading.Tasks.Task<System.Collections.Generic.List<TablespaceInfo>> GetTablespacesAsync(System.Threading.CancellationToken ct = default);
}
