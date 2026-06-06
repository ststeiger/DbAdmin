
namespace DbAdmin.Endpoints;


using DbAdmin.Models;
using DbAdmin.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;


public static class ApiEndpoints
{

    private const string ConnIdHeader = "X-Connection-Id";

    public static void MapAll(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        app.MapConnectionEndpoints();
        app.MapSchemaEndpoints();
        app.MapTableEndpoints();
        app.MapViewEndpoints();
        app.MapProgrammabilityEndpoints();
        app.MapTriggerEndpoints();
        app.MapSequenceEndpoints();
        app.MapIndexEndpoints();
        app.MapForeignKeyEndpoints();
        app.MapDataEndpoints();
        app.MapDdlEndpoints();
    }

    // ── Resolve connection from header ────────────────────────────────────────

    private static Microsoft.AspNetCore.Http.IResult GetProvider(
        Microsoft.AspNetCore.Http.HttpContext ctx, 
        ConnectionSessionService svc, 
        out Providers.IDbProvider? provider
    )
    {
        provider = null;

        // Accessing the header value without LINQ
        Microsoft.Extensions.Primitives.StringValues headerValues;
        string? id = null;

        if (ctx.Request.Headers.TryGetValue(ConnIdHeader, out headerValues))
        {
            // Get the first element if the collection is not empty
            if (headerValues.Count > 0)
            {
                id = headerValues[0];
            }
        }

        if (string.IsNullOrWhiteSpace(id))
            return Microsoft.AspNetCore.Http.Results.BadRequest($"Missing '{ConnIdHeader}' header.");
        try
        {
            provider = svc.GetProvider(id);
            return Microsoft.AspNetCore.Http.Results.Ok(); // sentinel – caller checks provider != null
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return Microsoft.AspNetCore.Http.Results.NotFound(ex.Message);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CONNECTION
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapConnectionEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = NewMethod(app);

        // POST /api/connections — open a new session
        grp.MapPost("/", async (
            ConnectionRequest req,
            ConnectionSessionService svc,
            System.Threading.CancellationToken ct
        ) =>
        {
            try
            {
                Models.ConnectionInfo info = await svc.ConnectAsync(req, ct);
                return Microsoft.AspNetCore.Http.Results.Ok(info);
            }
            catch (System.Exception ex)
            {
                return Microsoft.AspNetCore.Http.Results.Problem(ex.Message, statusCode: 500, title: "Connection failed");
            }
        })
        .WithName("Connect")
        .WithSummary("Open a new database connection. Returns a connectionId to use in X-Connection-Id header.");

        // GET /api/connections — list all open sessions
        grp.MapGet("/", (ConnectionSessionService svc) =>
            Microsoft.AspNetCore.Http.Results.Ok(svc.ListConnections()))
        .WithName("ListConnections")
        .WithSummary("List all open sessions.");

        // DELETE /api/connections/{id} — close a session
        grp.MapDelete("/{id}", async (
            string id,
            ConnectionSessionService svc
        ) =>
        {
            bool removed = await svc.DisconnectAsync(id);
            return removed ? Microsoft.AspNetCore.Http.Results.NoContent() : Microsoft.AspNetCore.Http.Results.NotFound($"Session '{id}' not found.");
        })
        .WithName("Disconnect")
        .WithSummary("Close and dispose a session.");

        // GET /api/connections/info — database-level metadata for the active connection
        grp.MapGet("/info", async (
            Microsoft.AspNetCore.Http.HttpContext ctx,
            ConnectionSessionService svc,
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetDatabaseInfoAsync(ct));
        })
        .WithName("GetDatabaseInfo")
        .WithSummary("Returns server version, collation, size, etc.");
    }

    private static Microsoft.AspNetCore.Routing.RouteGroupBuilder NewMethod(Microsoft.AspNetCore.Builder.WebApplication app)
    {
        return app.MapGroup("/api/connections").WithTags("Connections");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCHEMAS
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapSchemaEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/schemas").WithTags("Schemas");

        grp.MapGet("/", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetSchemasAsync(ct));
        })
        .WithName("GetSchemas")
        .WithSummary("List all user schemas.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TABLES
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapTableEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/tables").WithTags("Tables");

        // GET /api/tables?schema=dbo
        grp.MapGet("/", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string? schema, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetTablesAsync(schema, ct));
        })
        .WithName("GetTables")
        .WithSummary("List tables. Optionally filter by schema.");

        // GET /api/tables/{schema}/{table}/columns
        grp.MapGet("/{schema}/{table}/columns", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string table, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetColumnsAsync(schema, table, ct));
        })
        .WithName("GetColumns")
        .WithSummary("List columns for a table or view.");

        // GET /api/tables/{schema}/{table}/indexes
        grp.MapGet("/{schema}/{table}/indexes", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string table, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetIndexesAsync(schema, table, ct));
        })
        .WithName("GetTableIndexes")
        .WithSummary("Indexes on a specific table.");

        // GET /api/tables/{schema}/{table}/foreign-keys
        grp.MapGet("/{schema}/{table}/foreign-keys", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, ConnectionSessionService svc,
            string schema, 
            string table, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetForeignKeysAsync(schema, table, ct));
        })
        .WithName("GetTableForeignKeys")
        .WithSummary("Foreign keys on a specific table.");

        // GET /api/tables/{schema}/{table}/triggers
        grp.MapGet("/{schema}/{table}/triggers", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string table, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetTriggersAsync(schema, table, ct));
        })
        .WithName("GetTableTriggers")
        .WithSummary("Triggers on a specific table.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // VIEWS
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapViewEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/views").WithTags("Views");

        grp.MapGet("/", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string? schema, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetViewsAsync(schema, ct));
        })
        .WithName("GetViews")
        .WithSummary("List views (including materialized views in PostgreSQL).");

        grp.MapGet("/{schema}/{name}/definition", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string name,
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            string? def = await p.GetObjectDefinitionAsync(schema, name, "VIEW", ct);
            return def is null ? Microsoft.AspNetCore.Http.Results.NotFound() : Microsoft.AspNetCore.Http.Results.Ok(new { Definition = def });
        })
        .WithName("GetViewDefinition")
        .WithSummary("Source definition of a view.");

        grp.MapGet("/{schema}/{name}/columns", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string name, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) 
                return err;

            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetColumnsAsync(schema, name, ct));
        })
        .WithName("GetViewColumns")
        .WithSummary("Columns of a view.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PROGRAMMABILITY  (procedures + functions)
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapProgrammabilityEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/programmability").WithTags("Programmability");

        // ── Stored Procedures ────────────────────────────────────────────────

        grp.MapGet("/procedures", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string? schema, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetProceduresAsync(schema, ct));
        })
        .WithName("GetProcedures")
        .WithSummary("List stored procedures.");

        grp.MapGet("/procedures/{schema}/{name}/parameters", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string name, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetProcedureParametersAsync(schema, name, ct));
        })
        .WithName("GetProcedureParameters")
        .WithSummary("Parameters of a stored procedure.");

        grp.MapGet("/procedures/{schema}/{name}/definition", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, ConnectionSessionService svc,
            string schema, 
            string name, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            string? def = await p.GetObjectDefinitionAsync(schema, name, "PROCEDURE", ct);
            return def is null ? Microsoft.AspNetCore.Http.Results.NotFound() : Microsoft.AspNetCore.Http.Results.Ok(new { Definition = def });
        })
        .WithName("GetProcedureDefinition")
        .WithSummary("Source definition of a stored procedure.");

        // ── Functions ────────────────────────────────────────────────────────

        grp.MapGet("/functions", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string? schema, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetFunctionsAsync(schema, ct));
        })
        .WithName("GetFunctions")
        .WithSummary("List functions — scalar, table-valued, aggregate.");

        grp.MapGet("/functions/{schema}/{name}/parameters", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string name,
            System.Threading.CancellationToken ct) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetProcedureParametersAsync(schema, name, ct));
        })
        .WithName("GetFunctionParameters")
        .WithSummary("Parameters of a function.");

        grp.MapGet("/functions/{schema}/{name}/definition", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string name, 
            System.Threading.CancellationToken ct) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            string? def = await p.GetObjectDefinitionAsync(schema, name, "FUNCTION", ct);
            return def is null ? Microsoft.AspNetCore.Http.Results.NotFound() : Microsoft.AspNetCore.Http.Results.Ok(new { Definition = def });
        })
        .WithName("GetFunctionDefinition")
        .WithSummary("Source definition of a function.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // TRIGGERS
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapTriggerEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/triggers").WithTags("Triggers");

        grp.MapGet("/", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string? schema, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetTriggersAsync(schema, null, ct));
        })
        .WithName("GetTriggers")
        .WithSummary("List all triggers, optionally filtered by schema.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SEQUENCES
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapSequenceEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/sequences").WithTags("Sequences");

        grp.MapGet("/", async (Microsoft.AspNetCore.Http.HttpContext ctx, ConnectionSessionService svc,
            string? schema, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetSequencesAsync(schema, ct));
        })
        .WithName("GetSequences")
        .WithSummary("List sequences.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // INDEXES  (database-wide)
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapIndexEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/indexes").WithTags("Indexes");

        grp.MapGet("/", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string? schema, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetIndexesAsync(schema, null, ct));
        })
        .WithName("GetIndexes")
        .WithSummary("List all indexes, optionally filtered by schema.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FOREIGN KEYS  (database-wide)
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapForeignKeyEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/foreign-keys").WithTags("Foreign Keys");

        grp.MapGet("/", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string? schema, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetForeignKeysAsync(schema, null, ct));
        })
        .WithName("GetForeignKeys")
        .WithSummary("List all foreign keys, optionally filtered by schema.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DATA
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapDataEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/data").WithTags("Data");

        // GET /api/data/{schema}/{table}?page=1&pageSize=100&orderBy=Id&descending=false&filter=...
        grp.MapGet("/{schema}/{table}", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string table,
            int page = 1, 
            int pageSize = 100,
            string? orderBy = null, 
            bool descending = false, 
            string? filter = null,
            System.Threading.CancellationToken ct = default
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) 
                return err;

            TableDataRequest req = new TableDataRequest(schema, table, page, System.Math.Min(pageSize, 5000), orderBy, descending, filter);
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetTableDataAsync(req, ct));
        })
        .WithName("GetTableData")
        .WithSummary("Paginated table data with optional sort and WHERE filter.");

        // POST /api/data/query — run arbitrary SQL
        grp.MapPost("/query", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            QueryRequest req, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) 
                return err;

            QueryResult result = await p.ExecuteQueryAsync(req, ct);
            return result.Success ? Microsoft.AspNetCore.Http.Results.Ok(result) : Microsoft.AspNetCore.Http.Results.UnprocessableEntity(result);
        })
        .WithName("ExecuteQuery")
        .WithSummary("Run arbitrary SQL. Returns rows + affected count + elapsed ms.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DDL
    // ─────────────────────────────────────────────────────────────────────────

    private static void MapDdlEndpoints(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        Microsoft.AspNetCore.Routing.RouteGroupBuilder grp = app.MapGroup("/api/ddl").WithTags("DDL");

        // GET /api/ddl/{schema}/{name}/script?objectType=TABLE
        grp.MapGet("/{schema}/{name}/script", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string name, 
            string objectType,
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            DdlResult result = await p.GetCreateScriptAsync(schema, name, objectType, ct);
            return result.Success ? Microsoft.AspNetCore.Http.Results.Ok(result) : Microsoft.AspNetCore.Http.Results.NotFound(result);
        })
        .WithName("GetCreateScript")
        .WithSummary("Retrieve CREATE script for any schema object.");

        // DELETE /api/ddl/{schema}/{name}?objectType=TABLE
        grp.MapDelete("/{schema}/{name}", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string name, 
            string objectType, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            DdlResult result = await p.DropObjectAsync(schema, name, objectType, ct);
            return result.Success ? Microsoft.AspNetCore.Http.Results.Ok(result) : Microsoft.AspNetCore.Http.Results.UnprocessableEntity(result);
        })
        .WithName("DropObject")
        .WithSummary("DROP a schema object (TABLE, VIEW, PROCEDURE, FUNCTION, TRIGGER, SEQUENCE).");

        // POST /api/ddl/{schema}/{table}/truncate
        grp.MapPost("/{schema}/{table}/truncate", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc,
            string schema, 
            string table, 
            System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            DdlResult result = await p.TruncateTableAsync(schema, table, ct);
            return result.Success ? Microsoft.AspNetCore.Http.Results.Ok(result) : Microsoft.AspNetCore.Http.Results.UnprocessableEntity(result);
        })
        .WithName("TruncateTable")
        .WithSummary("TRUNCATE a table.");

        // GET /api/ddl/tablespaces
        grp.MapGet("/tablespaces", async (
            Microsoft.AspNetCore.Http.HttpContext ctx, 
            ConnectionSessionService svc, System.Threading.CancellationToken ct
        ) =>
        {
            Microsoft.AspNetCore.Http.IResult err = GetProvider(ctx, svc, out Providers.IDbProvider? p);
            if (p is null) return err;
            return Microsoft.AspNetCore.Http.Results.Ok(await p.GetTablespacesAsync(ct));
        })
        .WithName("GetTablespaces")
        .WithSummary("List tablespaces / filegroups.");
    }
}
