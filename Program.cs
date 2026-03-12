using Microsoft.AspNetCore.HttpOverrides;
using plc_api.Services;
using StackExchange.Redis;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Cors
builder.Services.AddCors(o => o.AddPolicy("AllowAll", p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()
));

// Redis
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")));

// Tag store + cache helpers
builder.Services.AddSingleton<TagRegistry>();
builder.Services.AddSingleton<TagCache>();

// Connection manager (reuses EIP connections safely)
builder.Services.AddSingleton<PlcConnectionManager>();

// Dashboard poller
builder.Services.AddHostedService<DashboardPoller>();

var app = builder.Build();
// Trust proxy headers from nginx
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Serve the app under https://host/plc-api/...
app.UsePathBase("/plc-api");

app.UseCors("AllowAll");

// Swagger should be AFTER UsePathBase so it picks up the base path
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    // Explicit endpoint so swagger UI works behind the path base
    c.SwaggerEndpoint("/plc-api/swagger/v1/swagger.json", "PLC API v1");
    c.RoutePrefix = "swagger"; // => /plc-api/swagger
});

app.MapControllers();
app.Run();
