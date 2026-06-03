# DbAdmin API

ASP.NET Core 9 Minimal API backend for a database administration application.  
Supports **MS SQL Server** and **PostgreSQL** out of the box.  
Easily extensible to other providers by implementing `IDbProvider`.

---

## Project Structure

```
DbAdmin/
├── Models/
│   └── Models.cs                  # All DTOs / request-response records
├── Providers/
│   ├── IDbProvider.cs             # Extensibility contract
│   ├── ProviderFactory.cs         # Instantiates the right provider
│   ├── MsSqlProvider.cs           # MS SQL Server implementation
│   └── PostgreSqlProvider.cs      # PostgreSQL implementation
├── Services/
│   └── ConnectionSessionService.cs  # Per-session connection management
├── Endpoints/
│   └── ApiEndpoints.cs            # All Minimal API routes
└── Program.cs
```

---

## Running

```bash
dotnet run --project DbAdmin
# Swagger UI → http://localhost:5000
```

---

## Authentication / Session Flow

All endpoints (except `POST /api/connections` and `GET /api/connections`) require the header:

```
X-Connection-Id: <id returned by POST /api/connections>
```

### 1. Open a connection

```http
POST /api/connections
Content-Type: application/json

{
  "provider": 0,            // 0 = MsSql, 1 = PostgreSql
  "host": "localhost",
  "port": 1433,
  "database": "MyDb",
  "username": "sa",
  "password": "secret",
  "trustServerCertificate": true
}
```

Response:
```json
{
  "connectionId": "a1b2c3d4...",
  "provider": 0,
  "host": "localhost",
  "port": 1433,
  "database": "MyDb",
  "username": "sa",
  "connectedAt": "2025-01-01T12:00:00Z"
}
```

### 2. Use the connection

```http
GET /api/tables?schema=dbo
X-Connection-Id: a1b2c3d4...
```

### 3. Close the connection

```http
DELETE /api/connections/a1b2c3d4...
```

---

## Endpoint Reference

### Connections
| Method | Path | Description |
|--------|------|-------------|
| POST   | `/api/connections` | Open session |
| GET    | `/api/connections` | List sessions |
| DELETE | `/api/connections/{id}` | Close session |
| GET    | `/api/connections/info` | DB server info |

### Schemas
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/schemas` | List schemas |

### Tables
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/tables?schema=` | List tables |
| GET    | `/api/tables/{schema}/{table}/columns` | Columns |
| GET    | `/api/tables/{schema}/{table}/indexes` | Indexes |
| GET    | `/api/tables/{schema}/{table}/foreign-keys` | Foreign keys |
| GET    | `/api/tables/{schema}/{table}/triggers` | Triggers |

### Views
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/views?schema=` | List views |
| GET    | `/api/views/{schema}/{name}/columns` | View columns |
| GET    | `/api/views/{schema}/{name}/definition` | View SQL |

### Programmability
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/programmability/procedures?schema=` | Stored procedures |
| GET    | `/api/programmability/procedures/{schema}/{name}/parameters` | Parameters |
| GET    | `/api/programmability/procedures/{schema}/{name}/definition` | Source |
| GET    | `/api/programmability/functions?schema=` | Functions (all kinds) |
| GET    | `/api/programmability/functions/{schema}/{name}/parameters` | Parameters |
| GET    | `/api/programmability/functions/{schema}/{name}/definition` | Source |

### Triggers
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/triggers?schema=` | All triggers |

### Sequences
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/sequences?schema=` | All sequences |

### Indexes (database-wide)
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/indexes?schema=` | All indexes |

### Foreign Keys (database-wide)
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/foreign-keys?schema=` | All foreign keys |

### Data
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/data/{schema}/{table}?page=1&pageSize=100&orderBy=Id&descending=false&filter=` | Paginated rows |
| POST   | `/api/data/query` | Execute arbitrary SQL |

Query request body:
```json
{ "sql": "SELECT TOP 10 * FROM Orders", "maxRows": 1000, "timeoutSeconds": 30 }
```

### DDL
| Method | Path | Description |
|--------|------|-------------|
| GET    | `/api/ddl/{schema}/{name}/script?objectType=TABLE` | CREATE script |
| DELETE | `/api/ddl/{schema}/{name}?objectType=TABLE` | DROP object |
| POST   | `/api/ddl/{schema}/{table}/truncate` | TRUNCATE table |
| GET    | `/api/ddl/tablespaces` | Tablespaces / filegroups |

`objectType` values: `TABLE`, `VIEW`, `PROCEDURE`, `FUNCTION`, `TRIGGER`, `SEQUENCE`

---

## Adding a New Provider (e.g. MySQL)

1. Create `Providers/MySqlProvider.cs` implementing `IDbProvider`
2. Add `MySQL` to the `DbProvider` enum in `Models.cs`
3. Add the case to `ProviderFactory.Create()` and `BuildConnectionString()`
4. Add the NuGet package (`MySqlConnector`)

No changes needed to endpoints or services.
