
namespace DbAdmin.Providers;


/// <summary>
/// Contract every database provider must implement.
/// Add a new provider by implementing this interface and registering it
/// in ProviderFactory.
/// </summary>
public interface IDbProvider 
    : System.IAsyncDisposable
{
    Models.DbProvider ProviderType { get; }

    // ── Connection ──────────────────────────────────────────────────────────
    System.Threading.Tasks.Task OpenAsync(System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<Models.DatabaseInfo> GetDatabaseInfoAsync(
        System.Threading.CancellationToken ct = default
    );

    // ── Schema objects ──────────────────────────────────────────────────────
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.SchemaInfo>> GetSchemasAsync(System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.TableInfo>> GetTablesAsync(string? schema = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.TableInfo>> GetViewsAsync(string? schema = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.ColumnInfo>> GetColumnsAsync(string schema, string table, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.IndexInfo>> GetIndexesAsync(string? schema = null, string? table = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.ForeignKeyInfo>> GetForeignKeysAsync(string? schema = null, string? table = null, System.Threading.CancellationToken ct = default);

    // ── Programmability ──────────────────────────────────────────────────────
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.ProcedureInfo>> GetProceduresAsync(string? schema = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.ProcedureInfo>> GetFunctionsAsync(string? schema = null, System.Threading.CancellationToken ct = default);          // scalar + table-valued
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.ProcedureParameter>> GetProcedureParametersAsync(string schema, string name, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<string?> GetObjectDefinitionAsync(string schema, string name, string objectType, System.Threading.CancellationToken ct = default);

    // ── Triggers, Sequences ──────────────────────────────────────────────────
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.TriggerInfo>> GetTriggersAsync(string? schema = null, string? table = null, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.SequenceInfo>> GetSequencesAsync(string? schema = null, System.Threading.CancellationToken ct = default);

    // ── Data ────────────────────────────────────────────────────────────────
    System.Threading.Tasks.Task<Models.TableDataResult> GetTableDataAsync(Models.TableDataRequest request, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<Models.QueryResult> ExecuteQueryAsync(Models.QueryRequest request, System.Threading.CancellationToken ct = default);

    // ── DDL helpers ──────────────────────────────────────────────────────────
    System.Threading.Tasks.Task<Models.DdlResult> GetCreateScriptAsync(string schema, string name, string objectType, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<Models.DdlResult> TruncateTableAsync(string schema, string table, System.Threading.CancellationToken ct = default);
    System.Threading.Tasks.Task<Models.DdlResult> DropObjectAsync(string schema, string name, string objectType, System.Threading.CancellationToken ct = default);

    // ── Stats / misc ─────────────────────────────────────────────────────────
    System.Threading.Tasks.Task<System.Collections.Generic.List<Models.TablespaceInfo>> GetTablespacesAsync(System.Threading.CancellationToken ct = default);
}
