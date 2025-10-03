using CleanArchitecture.Application.GraphQL;
using CleanArchitecture.Application.IRepository;
using CleanArchitecture.Application.IService;
using CleanArchitecture.Application.Repository;
using CleanArchitecture.Application.Service;
using CleanArchitecture.Domain.Model;
using CleanArchitecture.Domain.Model.Splendor.System;
using CleanArchitecture.Infrastructure.Redis;
using CleanArchitecture.Infrastructure.Repository;
using CleanArchitecture.Infrastructure.Security;
using CleanArchitecture.Presentation.Hubs;
using GraphQL;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Converters;
using Splendor_Game_Server.Hubs;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Database settings
builder.Services.Configure<DatabaseSettings>(
builder.Configuration.GetSection("DatabaseSettings"));

// CORS
builder.Services.AddCors(c =>
{
    c.AddPolicy("AllowOrigin", options =>
        options.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader());
});

// Controllers
builder.Services.AddControllers()
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.Converters.Add(new StringEnumConverter());
        options.SerializerSettings.ReferenceLoopHandling =
            Newtonsoft.Json.ReferenceLoopHandling.Ignore;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// ============== REDIS ==============
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var redisConnection = builder.Configuration.GetConnectionString("Redis");
    var config = ConfigurationOptions.Parse(redisConnection);
    config.AbortOnConnectFail = false;
    config.ConnectTimeout = 15000;
    config.SyncTimeout = 15000;
    config.ConnectRetry = 5;
    config.AllowAdmin = true;
    config.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;

    var multiplexer = ConnectionMultiplexer.Connect(config);
    multiplexer.ConnectionFailed += (sender, args) =>
    {
        Console.WriteLine($"Connection failed: {args.Exception.Message}");
    };
    multiplexer.ConnectionRestored += (sender, args) =>
    {
        Console.WriteLine("Connection restored");
    };
    return multiplexer;
});

builder.Services.AddSingleton<IGameStateStore, RedisGameStateStore>();
builder.Services.AddSingleton<GameRedisMapper>();

// ============== SPLENDOR SYSTEMS ==============
builder.Services.AddSingleton<NobleVisitSystem>();
builder.Services.AddSingleton<DiscardGemSystem>();
builder.Services.AddSingleton<EndGameSystem>();
builder.Services.AddSingleton<TurnSystem>();

builder.Services.AddSingleton<GameInitializationSystem>();
builder.Services.AddSingleton<GemCollectionSystem>(sp =>
    new GemCollectionSystem(sp.GetRequiredService<DiscardGemSystem>()));
builder.Services.AddSingleton<CardPurchaseSystem>(sp =>
    new CardPurchaseSystem(sp.GetRequiredService<NobleVisitSystem>()));
builder.Services.AddSingleton<CardReservationSystem>();

// ============== REPOSITORIES & SERVICES ==============
builder.Services.AddSingleton<SecurityUtility>();
builder.Services.AddSingleton<IPlayerService, PlayerService>();
builder.Services.AddSingleton<IPlayerRepository, PlayerRepository>();
builder.Services.AddSingleton<ISplendorRepository, SplendorRepository>();
builder.Services.AddSingleton<ISplendorService, SplendorService>();
builder.Services.AddSingleton<IRoomService, RoomService>();
builder.Services.AddSingleton<IRoomRepository, RoomRepository>();

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IUserConnectionService, UserConnectionService>();

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddAzureWebAppDiagnostics();

// ============== SIGNALR ==============
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
})
.AddJsonProtocol(options =>
{
    options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.PayloadSerializerOptions.WriteIndented = false;
    options.PayloadSerializerOptions.DefaultIgnoreCondition =
        JsonIgnoreCondition.WhenWritingNull;
    options.PayloadSerializerOptions.ReferenceHandler =
        ReferenceHandler.IgnoreCycles;
    options.PayloadSerializerOptions.Converters.Add(
        new JsonStringEnumConverter());
})
.AddMessagePackProtocol(options =>
{
    options.SerializerOptions = MessagePackSerializerOptions.Standard
        .WithResolver(CompositeResolver.Create(
            AttributeFormatterResolver.Instance,
            DynamicEnumAsStringResolver.Instance,
            StandardResolver.Instance
        ))
        .WithSecurity(MessagePackSecurity.UntrustedData);
});

// GraphQL
builder.Services.AddGraphQLServer()
    .AddQueryType<PlayerQuery>()
    .AddType<PlayerType>();

// ============== JWT AUTHENTICATION ==============
var configuration = builder.Configuration;
var secretKey = configuration["Appsettings:SecretKey"] ?? string.Empty;
var secretKeyBytes = Encoding.UTF8.GetBytes(secretKey);

builder.Services.AddAuthorization();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(secretKeyBytes),
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                // Support both roomHub and gameHub
                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/roomHub") ||
                     path.StartsWithSegments("/gameHub")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                context.NoResult();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";

                var error = context.Exception is SecurityTokenExpiredException
                    ? "{\"error\":\"TokenExpired\"}"
                    : "{\"error\":\"InvalidToken\"}";

                return context.Response.WriteAsync(error);
            },
            OnChallenge = context =>
            {
                if (!context.Response.HasStarted)
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.ContentType = "application/json";
                    return context.Response.WriteAsync("{\"error\":\"Unauthorized\"}");
                }
                return Task.CompletedTask;
            }
        };
    });
//Json Format
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });

// Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Splendor Game", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] { }
        }
    });
});

builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// Configure pipeline
if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Splendor Game v1");
        c.RoutePrefix = "swagger";
    });
}

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

app.UseCors(policy => policy
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()
    .SetIsOriginAllowed(_ => true)
);

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RoomHub>("/roomHub");
app.MapHub<GameHub>("/gameHub");
app.MapGraphQL("/graphql");

app.Run();