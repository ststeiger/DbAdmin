using DbAdmin.Models;

namespace DbAdmin.Providers;

public static class ProviderFactory
{
    /// <summary>
    /// Add new providers here — just implement IDbProvider and register.
    /// </summary>
    public static IDbProvider Create(ConnectionRequest req)
    {
        var cs = BuildConnectionString(req);
        return req.Provider switch
        {
            DbProvider.MsSql      => new MsSqlProvider(cs),
            DbProvider.PostgreSql => new PostgreSqlProvider(cs),
            _ => throw new System.NotSupportedException($"Provider '{req.Provider}' is not supported.")
        };
    }

    private static string BuildConnectionString(ConnectionRequest req) =>
        req.Provider switch
        {
            DbProvider.MsSql =>
                $"Server={req.Host},{req.Port};Database={req.Database};" +
                $"User Id={req.Username};Password={req.Password};" +
                $"TrustServerCertificate={req.TrustServerCertificate};",

            DbProvider.PostgreSql =>
                $"Host={req.Host};Port={req.Port};Database={req.Database};" +
                $"Username={req.Username};Password={req.Password};",

            _ => throw new System.NotSupportedException($"Provider '{req.Provider}' is not supported.")
        };
}
