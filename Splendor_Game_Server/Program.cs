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
using CleanArchitecture.SignalR.Hubs;
using GraphQL;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json.Converters;
using Splendor_Game_Server.Hubs;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Database settings
builder.Services.Configure<DatabaseSettings>(
builder.Configuration.GetSection("DatabaseSettings"));

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("OpenCors", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
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

// ============== RATE LIMITING ==============
builder.Services.AddRateLimiter(options =>
{
    // 1. Fixed Window: Giới hạn theo khung thời gian cố định
    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = 100; // 100 requests
        opt.Window = TimeSpan.FromMinutes(1); // trong 1 phút
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 10; // Queue tối đa 10 requests
    });

    // 2. Sliding Window: Giới hạn linh hoạt hơn
    options.AddSlidingWindowLimiter("sliding", opt =>
    {
        opt.PermitLimit = 50;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.SegmentsPerWindow = 4; // Chia thành 4 đoạn 15s
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 5;
    });

    // 3. Token Bucket: Cho phép burst traffic
    options.AddTokenBucketLimiter("token", opt =>
    {
        opt.TokenLimit = 100; // Tối đa 100 tokens
        opt.ReplenishmentPeriod = TimeSpan.FromMinutes(1); // Refill mỗi phút
        opt.TokensPerPeriod = 50; // Thêm 50 tokens mỗi lần refill
        opt.AutoReplenishment = true;
        opt.QueueLimit = 10;
    });

    // 4. Concurrency: Giới hạn số requests đồng thời
    options.AddConcurrencyLimiter("concurrent", opt =>
    {
        opt.PermitLimit = 20; // Tối đa 20 requests cùng lúc
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 30;
    });

    // 5. Rate limit riêng cho từng user (dựa trên IP hoặc UserId)
    options.AddPolicy("perUser", context =>
    {
        // Lấy UserId từ JWT token
        var userId = context.User?.FindFirst("UserId")?.Value;

        // Fallback sang IP nếu không có UserId
        var key = userId ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";

        return RateLimitPartition.GetSlidingWindowLimiter(key, _ =>
            new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30, // 30 requests
                Window = TimeSpan.FromMinutes(1), // mỗi phút
                SegmentsPerWindow = 3,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            });
    });

    // 6. Rate limit cho Admin (ít hạn chế hơn)
    options.AddPolicy("admin", context =>
    {
        var isAdmin = context.User?.IsInRole("Admin") ?? false;

        if (isAdmin)
        {
            return RateLimitPartition.GetNoLimiter("admin");
        }

        return RateLimitPartition.GetFixedWindowLimiter("user", _ =>
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
    });

    // Global settings
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = retryAfter.TotalSeconds.ToString();
        }

        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Too many requests",
            message = "Please try again later",
            retryAfter = retryAfter.TotalSeconds
        }, cancellationToken: token);
    };
});

// ============== REDIS ==============
//builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
//{
//    var redisConnection = builder.Configuration.GetConnectionString("Redis");
//    var config = ConfigurationOptions.Parse(redisConnection);
//    config.AbortOnConnectFail = false;
//    config.ConnectTimeout = 30000;
//    config.SyncTimeout = 60000;
//    config.ConnectRetry = 5;
//    config.AllowAdmin = true; // Đổi thành false
//    
//    config.SslProtocols = System.Security.Authentication.SslProtocols.Tls12;
//   
//    var multiplexer = ConnectionMultiplexer.Connect(config);
//    multiplexer.ConnectionFailed += (sender, args) =>
//    {
//        Console.WriteLine($"Connection failed: {args.Exception.Message}");
//    };
//    multiplexer.ConnectionRestored += (sender, args) =>
//    {
//        Console.WriteLine("Connection restored");
//    };
//    return multiplexer;
//});

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var host = configuration.GetConnectionString("Redis");
    var user = configuration["RedisSettings:User"];
    var password = configuration["RedisSettings:Password"];

    var config = new ConfigurationOptions
    {
        EndPoints = { host },
        User = user,
        Password = password,
        Ssl = false, 
        SslProtocols = System.Security.Authentication.SslProtocols.Tls13,
        AbortOnConnectFail = true, 
        ConnectTimeout = 30000,
        SyncTimeout = 30000,
        AsyncTimeout = 30000,
        ConnectRetry = 5,
        Protocol = RedisProtocol.Resp3, 
        AllowAdmin = false
    };

    config.CertificateValidation += (sender, cert, chain, errors) => true;

    var multiplexer = ConnectionMultiplexer.Connect(config);

    multiplexer.ConnectionFailed += (sender, args) =>
    {
        Console.WriteLine($"❌ Redis Connection failed: {args.Exception?.Message}");
        Console.WriteLine($"   Endpoint: {args.EndPoint}");
        Console.WriteLine($"   Type: {args.FailureType}");
    };

    multiplexer.ConnectionRestored += (sender, args) =>
    {
        Console.WriteLine($"✅ Redis Connection restored: {args.EndPoint}");
    };

    Console.WriteLine($"🔌 Redis IsConnected: {multiplexer.IsConnected}");

    return multiplexer;
});

builder.Services.AddSingleton<IGameStateStore, RedisGameStateStore>();
builder.Services.AddSingleton<IRedisMapper , GameRedisMapper>();

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
builder.Services.AddSingleton<IGameHistoryRepository, GameHistoryRepository>();
builder.Services.AddSingleton<IGameHistoryService, GameHistoryService>();
builder.Services.AddSingleton<ITutorialSplendorService, TutorialSplendorService>();
builder.Services.AddSingleton<ITutorialSessionRepository, RedisTutorialSessionRepository>();
builder.Services.AddSingleton<IGameNotifier, GameHubNotifier>();
builder.Services.AddHostedService<TutorialCleanupService>();

builder.Services.AddKeyedSingleton<IBotService, BotService>("rule");
builder.Services.AddKeyedSingleton<IBotService, LangChainBotService>("ai");

builder.Services.AddHttpClient("BotAgent", c =>
{
    c.BaseAddress = new Uri(builder.Configuration["BotAgent:Url"]!);
    c.Timeout = TimeSpan.FromSeconds(180); // ← tăng lên 3 phút
});


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

        // ✅ Cho phép đọc metadata $type từ [JsonPolymorphic]/[JsonDerivedType]
        options.JsonSerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver();

        // ✅ Không phân biệt hoa thường khi deserialize property name
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
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

builder.WebHost.UseUrls("http://0.0.0.0:8080");

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

app.UseCors("OpenCors");

//app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<RoomHub>("/roomHub");
app.MapHub<GameHub>("/gameHub");
app.MapHub<TutorialGameHub>("/tutorialGameHub");
app.MapGraphQL("/graphql");

app.Run();