
namespace DbAdmin;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

using DbAdmin.Endpoints;
using DbAdmin.Services;


public class Program
{

    internal static async System.Threading.Tasks.Task<int> Main(
        string[] args
    )
    {

        Microsoft.AspNetCore.Builder.WebApplicationBuilder builder = 
            Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(args)
        ;

        // ── Services ──────────────────────────────────────────────────────────────────

        builder.Services.AddSingleton<ConnectionSessionService>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new()
            {
                Title = "DbAdmin API",
                Version = "v1",
                Description = """
            Database administration REST API.
            Supports MS SQL Server and PostgreSQL (extensible via IDbProvider).

            **Workflow:**
            1. POST /api/connections  →  receive a `connectionId`
            2. Pass `X-Connection-Id: <connectionId>` on every subsequent request
            3. DELETE /api/connections/{id}  when done
            """
            });

            c.AddSecurityDefinition("ConnectionId", new()
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Name = "X-Connection-Id",
                Description = "Session token returned by POST /api/connections"
            });
            c.AddSecurityRequirement(
                new Microsoft.OpenApi.Models.OpenApiSecurityRequirement()
                {
                    {
                        new Microsoft.OpenApi.Models.OpenApiSecurityScheme() 
                        { 
                            Reference = 
                                new Microsoft.OpenApi.Models.OpenApiReference() 
                                { 
                                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, 
                                    Id = "ConnectionId" 
                                } 
                        },
                        []
                    }
                }
                );
        });

        // ── CORS (open for dev; tighten for prod) ─────────────────────────────────────

        builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
            p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        // ─────────────────────────────────────────────────────────────────────────────

        Microsoft.AspNetCore.Builder.WebApplication app = builder.Build();

        app.UseCors();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "DbAdmin v1");
                c.RoutePrefix = string.Empty;   // Swagger at root
            });
        }

        // ── Global error handler ──────────────────────────────────────────────────────

        app.Use(
            async (ctx, next) =>
            {
                try
                {
                    await next(ctx);
                }
                catch (System.Exception ex)
                {
                    ctx.Response.StatusCode = 500;
                    await ctx.Response.WriteAsJsonAsync(
                        new { error = ex.Message }
                    );
                }
            }
        );

        // ── Map all endpoints ─────────────────────────────────────────────────────────


        app.UseStaticFiles();
        app.MapAll();

        await app.RunAsync();

        return 0;
    }
}

