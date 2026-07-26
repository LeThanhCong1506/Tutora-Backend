using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MV.ApplicationLayer.Hubs;
using MV.ApplicationLayer.Interfaces;
using MV.ApplicationLayer.ServiceInterfaces;
using MV.ApplicationLayer.Services;
using MV.ApplicationLayer.BackgroundJobs;
using MV.DomainLayer.Constants;
using MV.PresentationLayer.Filters;
using MV.PresentationLayer.Migrations;
using Hangfire;
using Hangfire.PostgreSql;
using MV.DomainLayer.Configuration;
using MV.DomainLayer.Interfaces;
using MV.DomainLayer.Settings;
using MV.DomainLayer.DTO;
using MV.InfrastructureLayer;
using MV.InfrastructureLayer.DBContext;
using MV.InfrastructureLayer.ExternalServices;
using MV.InfrastructureLayer.Repositories;
using MV.InfrastructureLayer.Services;
using MV.ApplicationLayer.RepositoryInterfaces;
using PayOS;
using Resend;
using SP25.OJT202.AccountManagement.Presentation.Middlewares;
using StackExchange.Redis;
using System.Text;

// Npgsql Legacy Timestamp Mode
// Giữ lại để tương thích với DB hiện tại dùng `timestamp without time zone`.
// Mọi DateTime GHI xuống DB phải là UTC (dùng TimeZoneHelper.UtcNow).
// Mọi DateTime ĐỌC từ DB trả về UTC+0 — Frontend tự convert sang timezone hiển thị.
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

var redisConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStrings.RedisConnection)!;

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnectionString));

builder.Services.Configure<GoogleGeminiSettings>(builder.Configuration.GetSection(GoogleGeminiSettings.SectionName));
builder.Services.Configure<PaymentSettings>(builder.Configuration.GetSection(PaymentSettings.SectionName));
builder.Services.Configure<GoogleSettings>(builder.Configuration.GetSection(GoogleSettings.SectionName));
builder.Services.Configure<AgoraSettings>(builder.Configuration.GetSection(AgoraSettings.SectionName));
builder.Services.Configure<AgoraRecordingSettings>(builder.Configuration.GetSection(AgoraRecordingSettings.SectionName));
builder.Services.Configure<AgoraNotificationSettings>(builder.Configuration.GetSection(AgoraNotificationSettings.SectionName));
builder.Services.Configure<SessionEvidenceSettings>(builder.Configuration.GetSection(SessionEvidenceSettings.SectionName));
builder.Services.Configure<GoogleDriveSettings>(builder.Configuration.GetSection(GoogleDriveSettings.SectionName));
builder.Services.Configure<WhiteboardSettings>(builder.Configuration.GetSection(WhiteboardSettings.SectionName));
builder.Services.Configure<VietQRSettings>(builder.Configuration.GetSection(VietQRSettings.SectionName));
builder.Services.Configure<ZaloOAConfig>(builder.Configuration.GetSection(ConfigurationKeys.ZaloOA.SectionName));
builder.Services.Configure<CloudinarySettings>(builder.Configuration.GetSection("Cloudinary"));
builder.Services.Configure<TutorAiSettings>(builder.Configuration.GetSection(TutorAiSettings.SectionName));
// builder.Services.Configure<InternalApiSettings>(builder.Configuration.GetSection(InternalApiSettings.SectionName));

builder.Services.AddKeyedSingleton<PayOSClient>(ServiceKeys.PayOS.Checkout, (sp, _) =>
{
    var settings = sp.GetRequiredService<IOptions<PaymentSettings>>().Value;
    var logger = sp.GetRequiredService<ILogger<PayOSClient>>();
    return new PayOSClient(new PayOSOptions
    {
        ClientId = settings.ClientId,
        ApiKey = settings.ApiKey,
        ChecksumKey = settings.ChecksumKey,
        TimeoutMs = 30000,
        MaxRetries = 3,
        Logger = logger
    });
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
    options.InstanceName = "SWD391:";
});

// Allow larger file uploads (100MB for video)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 104_857_600; // 100 MB
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Thêm dòng này để chuyển đổi Enum thành String trên Swagger và API
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        // Cấu hình này sẽ bảo bộ serialize bỏ qua các vòng lặp tham chiếu
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        // Cấu hình để ASP.NET Core tự động xử lý DateTime từ JSON
        // Nếu frontend gửi ISO 8601 với timezone (ví dụ: "2026-06-10T14:00:00Z" hoặc "2026-06-10T14:00:00+07:00")
        // thì sẽ được convert đúng. Nếu không có timezone thì coi như UTC.
        // Note: Nếu muốn frontend gửi local time (VN) thì cần gửi với offset: "2026-06-10T14:00:00+07:00"
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var hasPayloadErrors = context.ModelState.Keys
            .Any(key => key == "$" || key.StartsWith("$."));

        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .Where(entry => !hasPayloadErrors || !entry.Key.Equals("request", StringComparison.OrdinalIgnoreCase))
            .Select(entry => new
            {
                Key = NormalizeModelStateKey(entry.Key),
                Errors = entry.Value!.Errors
                    .Select(error => NormalizeValidationError(entry.Key, error.ErrorMessage))
            })
            .GroupBy(entry => entry.Key)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(entry => entry.Errors).Distinct().ToArray());

        return new BadRequestObjectResult(
            APIResponse<object>.Fail("Dữ liệu đầu vào không hợp lệ.", 400, errors));
    };
});

static string NormalizeModelStateKey(string key)
{
    if (string.IsNullOrWhiteSpace(key))
        return "body";

    if (key == "$")
        return "body";

    return key.Length > 2 && key.StartsWith("$.")
        ? char.ToLowerInvariant(key[2]) + key[3..]
        : char.ToLowerInvariant(key[0]) + key[1..];
}

static string NormalizeValidationError(string key, string message)
{
    if (key.Equals("$.birthdate", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Birthdate", StringComparison.OrdinalIgnoreCase))
    {
        return "Birthdate must be a valid date in yyyy-MM-dd format.";
    }

    if (key.Equals("$.gender", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("Gender", StringComparison.OrdinalIgnoreCase))
    {
        return "Gender must be a valid value.";
    }

    if (message.Contains("The request field is required.", StringComparison.OrdinalIgnoreCase))
        return "Request body is required.";

    if (message.Contains("could not be converted", StringComparison.OrdinalIgnoreCase))
        return "Invalid value format.";

    return message;
}

var signalRBuilder = builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
});

if (!builder.Environment.IsDevelopment())
{
    signalRBuilder.AddStackExchangeRedis(
        builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStrings.RedisConnection)!,
        options =>
        {
            options.Configuration.AbortOnConnectFail = false;
            options.Configuration.ConnectRetry = 3;
        });
}

builder.Services.AddMemoryCache();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// Thêm CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy =>
        {
            policy.WithOrigins(
                    "https://localhost:7203", "http://localhost:5173", "http://localhost:5174", "http://localhost:5166",
                    "http://localhost:5180",
                    // Next.js dev server (apps/web-next)
                    "http://localhost:3000",
                    "https://swd-391-frontend-d4ek.vercel.app", "http://localhost:5500",
                    "https://www.tutora.vn", "https://tutora.vn", "https://tutorahelps.vercel.app",
                    // Vite app sau cutover sang Next (portal + auth)
                    "https://app.tutora.vn",
                    "https://apps.tutora.vn",
                    // Developer app
                    "https://tutora-developer.vercel.app",
                    // Zalo Mini App domains
                    "https://h5.zalo.me", "https://h5.zadn.vn", "https://h5.zdn.vn", "https://miniapp-cdn.zalo.me")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  // AllowAnyHeader chỉ áp dụng cho REQUEST header. Muốn JS đọc được
                  // response header tuỳ biến thì phải khai báo Access-Control-Expose-Headers.
                  // Thiếu dòng này, FE không đọc nổi X-Pagination → tổng số bản ghi rơi
                  // về độ dài của trang hiện tại → thanh phân trang biến mất.
                  .WithExposedHeaders("X-Pagination")
                  .AllowCredentials();
        });
});

// Setup Swagger để Authorize
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "AGORA-BackEnd", Version = "v1" });

    options.AddSecurityDefinition(HttpConstants.BearerScheme, new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter a valid token.",
        Name = HttpConstants.AuthorizationHeader,
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = HttpConstants.BearerScheme
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = HttpConstants.BearerScheme
                }
            },
            new string[] {}
        }
    });
});

builder.Services.AddDbContext<AgoraDbContext>(options =>
                options.UseNpgsql(
                    builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStrings.DefaultConnection),
                    o => o.UseVector())   // pgvector: map cột vector(768) của questions.embedding
            );
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AgoraDbContext>());

// Fail fast if DefaultConnection is not configured (helps diagnose missing config/env)
var defaultConnCheck = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStrings.DefaultConnection);
if (string.IsNullOrEmpty(defaultConnCheck))
{
    throw new InvalidOperationException("Configuration value 'ConnectionStrings:DefaultConnection' is missing. Set it in appsettings.json/appsettings.Development.json or as environment variable 'ConnectionStrings__DefaultConnection'.");
}

builder.Services.Configure<ResendSettings>(builder.Configuration.GetSection(ResendSettings.SectionName));
builder.Services.AddHttpClient<IResend, ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
{
    o.ApiToken = builder.Configuration[$"{ResendSettings.SectionName}:ApiKey"]!;
});

// 1. Đăng ký HttpClient cho FptAiService (Để nó gọi API ra ngoài được)
builder.Services.AddHttpClient<IFptAiService, FptAiService>();

// 2. Đăng ký HttpClient cho OcrSpaceService (OCR cho chứng chỉ/bằng cấp)
builder.Services.AddHttpClient<IOcrService, OcrSpaceService>();

// 3. Đăng ký HttpClient cho DisputeClassificationService (Groq AI phân loại ưu tiên tranh chấp)
builder.Services.AddHttpClient<IDisputeClassificationService, DisputeClassificationService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Repo injection
builder.Services.AddScoped<IAuthenticationRepository, AuthenticationRepository>();
builder.Services.AddScoped<IPasswordRepository, PasswordRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IStudentRepository, StudentRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IDisputeRepository, DisputeRepository>();
builder.Services.AddScoped<IWarningRepository, WarningRepository>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IAiChatRepository, AiChatRepository>();
builder.Services.AddScoped<IQuestionNoteRepository, QuestionNoteRepository>();
builder.Services.AddScoped<IClassSessionRepository, ClassSessionRepository>();
builder.Services.AddScoped<IWalletRepository, WalletRepository>();
builder.Services.AddScoped<IWithdrawalRepository, WithdrawalRepository>();
builder.Services.AddScoped<ITutorRepository, TutorRepository>();
builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<ITutorSearchRepository, TutorSearchRepository>();
builder.Services.AddScoped<IStaffPermissionRepository, StaffPermissionRepository>();
builder.Services.AddScoped<IPermissionGroupRepository, PermissionGroupRepository>();
builder.Services.AddScoped<ILearningMaterialRepository, LearningMaterialRepository>();

// Service injection
builder.Services.AddScoped<ITutorVerificationService, TutorVerificationService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IPermissionGroupService, PermissionGroupService>();
builder.Services.AddScoped<ILoginService, LoginService>();
builder.Services.AddScoped<ITutorService, TutorService>();
builder.Services.AddScoped<ITutorProfileUpdateStagingService, TutorProfileUpdateStagingService>();
builder.Services.AddScoped<IPushTokenService, PushTokenService>();
builder.Services.AddScoped<IFirebasePushNotificationService, FirebasePushNotificationService>();
builder.Services.AddScoped<IEmailService, ResendEmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IExportService, ExportService>();
builder.Services.AddScoped<IPasswordService, PasswordService>();
builder.Services.AddScoped<IUserTestService, UserTestService>();
builder.Services.AddScoped<IFileStorageService, CloudinaryStorageService>();
builder.Services.AddSingleton<IEncryptionService, AesEncryptionService>();
builder.Services.AddScoped<ISimpleAuthService, SimpleAuthService>();
// ZNS OTP dùng số điện thoại đã được xác thực của tài khoản.
builder.Services.AddScoped<IOtpSender, ZnsOtpSender>();
builder.Services.AddScoped<ISocialRegistrationService, SocialRegistrationService>();
builder.Services.AddScoped<IZaloAuthService, ZaloAuthService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
builder.Services.AddScoped<ITutorAvailabilityService, TutorAvailabilityService>();
builder.Services.AddScoped<ILookupService, LookupService>();
builder.Services.AddScoped<IStudyResourceService, StudyResourceService>();
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<IEkycService, EkycService>();
builder.Services.AddScoped<IStudentIdentityService, StudentIdentityService>();
builder.Services.AddScoped<IStudentService, StudentService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IPresenceService, PresenceService>();
builder.Services.AddHostedService<PresenceLeaseCleanupService>();
builder.Services.AddScoped<IAiChatService, AiChatService>();
builder.Services.AddScoped<IQuestionNoteService, QuestionNoteService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IWalletService, WalletService>();
builder.Services.AddScoped<IAiCreditService, AiCreditService>();
builder.Services.AddScoped<IClassSessionService, ClassSessionService>();
builder.Services.AddScoped<ISessionLogService, SessionLogService>();
builder.Services.AddScoped<IClassSessionScheduleChangeService, ClassSessionScheduleChangeService>();
builder.Services.AddScoped<IAgoraRTCService, AgoraRTCService>();
builder.Services.AddSingleton<ILiveSessionDeviceLeaseService, LiveSessionDeviceLeaseService>();
// Presence in-memory (Singleton): theo dõi ai đang trong phòng học để auto check-in khi đủ cả 2.
builder.Services.AddSingleton<ISessionPresenceService, SessionPresenceService>();
// Cloud Recording: auto start khi auto check-in, stop khi check-out; relay file S3 → Drive chạy nền.
builder.Services.AddHttpClient<ICloudRecordingService, CloudRecordingService>();
builder.Services.AddHttpClient<IWhiteboardService, WhiteboardService>();
builder.Services.AddScoped<IGoogleDriveService, GoogleDriveService>();
builder.Services.AddSingleton<IRecordingAccessTokenService, RecordingAccessTokenService>();
builder.Services.AddScoped<IRecordingRelayService, RecordingRelayService>();
builder.Services.AddHostedService<MV.PresentationLayer.BackgroundServices.RecordingRelayHostedService>();
builder.Services.AddScoped<ITutorFinanceService, TutorFinanceService>();
builder.Services.AddScoped<ILearningMaterialService, LearningMaterialService>();

builder.Services.AddHttpContextAccessor();

// M3: ClassSession Management & Settlement
builder.Services.AddScoped<ISettlementService, SettlementService>();
builder.Services.AddScoped<IParentService, ParentService>();
builder.Services.AddScoped<IDisputeService, DisputeService>();
builder.Services.AddScoped<IWarningService, WarningService>();
builder.Services.AddScoped<IFeedbackService, FeedbackService>();
builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<ISourceDocumentService, SourceDocumentService>();
builder.Services.AddScoped<IKnowledgeBaseService, KnowledgeBaseService>();

builder.Services.AddScoped<IBankListService, BankListService>();
builder.Services.AddScoped<PayOSWebhookService>();

// M4-T7: Admin Monitoring Dashboard
builder.Services.AddScoped<IAdminPayoutService, AdminPayoutService>();
builder.Services.AddScoped<ISystemAlertService, SystemAlertService>();
builder.Services.AddScoped<IAdminFinancialService, AdminFinancialService>();
builder.Services.AddScoped<IAdminBookingService, AdminBookingService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
    {
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionStrings.DefaultConnection));
    }, new PostgreSqlStorageOptions
    {
        PrepareSchemaIfNecessary = false
    }));

builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
    options.Queues = new[] { "default" };
});

builder.Services.AddHttpClient(ServiceKeys.HttpClients.VietQR, client =>
{
    var vietQRSettings = builder.Configuration.GetSection(VietQRSettings.SectionName).Get<VietQRSettings>();
    client.BaseAddress = new Uri(vietQRSettings?.BaseUrl ?? VietQRSettings.DefaultBaseUrl);
    client.Timeout = TimeSpan.FromSeconds(vietQRSettings?.TimeoutSeconds ?? 30);
});
builder.Services.AddScoped<IVietQRClient, VietQRClient>();
builder.Services.AddHttpClient(ServiceKeys.HttpClients.ZaloOA, client =>
{
    client.BaseAddress = new Uri("https://openapi.zalo.me/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient(ServiceKeys.HttpClients.ZaloZNS, client =>
{
    client.BaseAddress = new Uri("https://business.openapi.zalo.me/");
    client.Timeout = TimeSpan.FromSeconds(15);
});
// Tutor AI (FastAPI)
builder.Services.AddHttpClient(ServiceKeys.HttpClients.TutorAi, client =>
{
    var aiSettings = builder.Configuration.GetSection(TutorAiSettings.SectionName).Get<TutorAiSettings>();
    if (string.IsNullOrWhiteSpace(aiSettings?.BaseUrl))
        throw new InvalidOperationException(
            "Thiếu cấu hình 'TutorAi:BaseUrl'. Hãy set qua appsettings hoặc biến môi trường.");
    client.BaseAddress = new Uri(aiSettings.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(aiSettings.StreamTimeoutSeconds > 0 ? aiSettings.StreamTimeoutSeconds : 120);
});
// Zalo OA
builder.Services.AddScoped<IZaloOAService, ZaloOAService>();
builder.Services.AddScoped<IZaloChatbotService, ZaloChatbotService>();

// Tutor Search Service
builder.Services.AddScoped<ITutorSearchService, TutorSearchService>();

// Tutor Recommend (SQL filter → AI rank → profile fetch)
builder.Services.AddScoped<ITutorAiClient, TutorAiClient>();
builder.Services.AddScoped<ITutorRecommendService, TutorRecommendService>();

//Unit of work
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Background job
//builder.Services.AddHostedService<EmailConsumerService>();
builder.Services.AddHostedService<PaymentTimeoutJob>();
builder.Services.AddHostedService<PaymentRequestReconciliationJob>();
builder.Services.AddHostedService<TutorResponseTimeoutJob>();
builder.Services.AddHostedService<AutoConfirmClassSessionJob>();
builder.Services.AddHostedService<AutoUnsuspendJob>();
builder.Services.AddHostedService<ClassSessionReminderJob>();
builder.Services.AddHostedService<RemainingPaymentTriggerJob>();
builder.Services.AddHostedService<GhostUserCleanupJob>();
builder.Services.AddHostedService<ZaloTokenRefreshJob>();
builder.Services.AddSingleton<ITutorEmbedQueue, TutorEmbedQueue>();
builder.Services.AddHostedService<TutorEmbedWorker>();

// Cấu hình Authentication (JWT mặc định, Google/Facebook song song)
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
// JWT cho API + SignalR
.AddJwtBearer(options =>
{
    var jwtKey = builder.Configuration[ConfigurationKeys.Jwt.Key];
    var jwtIssuer = builder.Configuration[ConfigurationKeys.Jwt.Issuer];
    var jwtAudience = builder.Configuration[ConfigurationKeys.Jwt.Audience];

    if (string.IsNullOrEmpty(jwtKey) || string.IsNullOrEmpty(jwtIssuer))
    {
        throw new InvalidOperationException("JWT configuration is missing in appsettings.json");
    }

    var keyBytes = Encoding.UTF8.GetBytes(jwtKey);

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(keyBytes),

        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,

        ValidateAudience = true,
        ValidAudience = jwtAudience,

        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        RoleClaimType = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
    };

    options.Events = new JwtBearerEvents
    {
        // Cho phép nhận token từ query string cho SignalR
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query[OAuthFieldNames.AccessToken];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken)
                && (path.StartsWithSegments("/notificationHub")
                    || path.StartsWithSegments("/hubs/chat")
                    || path.StartsWithSegments("/hubs/session-lobby")
                    || path.StartsWithSegments("/hubs/live-session")))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.Message}.");
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                var userRepository = context.HttpContext.RequestServices.GetRequiredService<MV.ApplicationLayer.RepositoryInterfaces.IUserRepository>();
                var user = await userRepository.GetUserByIdAsync(userId);
                if (user == null || user.Status == 0)
                {
                    context.Fail("Tài khoản đã bị khóa hoặc bị xóa.");
                    return;
                }

                var identity = context.Principal?.Identity as System.Security.Claims.ClaimsIdentity;
                if (identity == null || string.IsNullOrWhiteSpace(user.Primaryrole))
                {
                    context.Fail("Không xác định được role hiện tại của tài khoản.");
                    return;
                }

                // JWT có thể còn role/quyền cũ. Lấy quyền hiệu lực từ DB ở mỗi request,
                // rồi thay toàn bộ claim trước khi authorization chạy.
                IEnumerable<string> grantedKeys = Array.Empty<string>();
                if (string.Equals(user.Primaryrole, UserRole.Staff, StringComparison.OrdinalIgnoreCase))
                {
                    var permissionRepo = context.HttpContext.RequestServices.GetRequiredService<MV.ApplicationLayer.RepositoryInterfaces.IStaffPermissionRepository>();
                    grantedKeys = await permissionRepo.GetGrantedPermissionKeysAsync(userId);
                }
                MV.PresentationLayer.Authorization.CurrentAccessClaims.Replace(
                    identity,
                    user.Primaryrole,
                    grantedKeys);
            }
        }
    };
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TutorOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireAssertion(context =>
            context.User.IsInRole(UserRole.Tutor));
    });
});
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, MV.PresentationLayer.Authorization.PermissionRequirementHandler>();

if (args.Any(arg => string.Equals(arg, "--run-managed-migrations", StringComparison.OrdinalIgnoreCase)))
{
    await ManagedMigrationRunner.RunAsync(builder.Configuration);
    return;
}

var app = builder.Build();

// Development applies managed migrations on boot so a new table never needs to be pasted into a
// SQL console by hand. Other environments keep the explicit --run-managed-migrations gate, where
// schema changes belong to the deployment step rather than to whoever starts the process first.
// The runner takes an advisory lock and journals checksums, so repeated starts are a no-op.
if (app.Environment.IsDevelopment())
{
    var migrationLogger = app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger("ManagedMigrationRunner");
    // Session admission and the admin evidence endpoint both depend on the managed schema.
    // Starting with a failed/checksum-mismatched migration would only defer the error into a
    // lesson or dispute, so development fails fast just like the explicit deployment command.
    await ManagedMigrationRunner.RunAsync(app.Configuration);
    migrationLogger.LogInformation("Managed migrations are up to date.");
}

await using (var schemaScope = app.Services.CreateAsyncScope())
{
    var schemaContext = schemaScope.ServiceProvider
        .GetRequiredService<AgoraDbContext>();
    var schemaLogger = schemaScope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("PaymentSqlMigrationRunner");

    if (args.Any(a => string.Equals(
        a,
        "--migrate-only",
        StringComparison.OrdinalIgnoreCase)))
    {
        await PaymentSqlMigrationRunner.ApplyPendingAsync(
            schemaContext,
            schemaLogger);
        return;
    }

    await PaymentSqlMigrationRunner.EnsureCurrentAsync(
        schemaContext,
        schemaLogger);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{

}

// UseRouting phải được khai báo tường minh TRƯỚC UseCors.
// Nếu không, .NET 6+ tự thêm implicit UseRouting() vào đầu pipeline,
// khiến router xử lý OPTIONS preflight trước CORS → 405 → CORS fail.
app.UseRouting();

// CORS phải đứng SAU UseRouting và TRƯỚC UseAuthentication/UseAuthorization
app.UseCors("AllowReactApp");

// Middleware xử lý lỗi
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// M4-T6: Hangfire Dashboard (optional, for monitoring jobs)
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireAuthorizationFilter() }
});

app.MapGet("/", () => Results.Content("""
    <!DOCTYPE html>
    <html><head>
      <meta charset="UTF-8" />
      <meta name="zalo-platform-site-verification" content="JyEN0ewmJ0HRf9ePWhCF80Abl2_kqsbmDpGq" />
      <title>Tutora API</title>
    </head><body><p>Tutora API</p></body></html>
""", HttpConstants.HtmlContentType));

app.MapControllers();
app.MapHub<NotificationHub>("/notificationHub");
app.MapHub<ChatHub>("/hubs/chat");
app.MapHub<SessionLobbyHub>("/hubs/session-lobby");
app.MapHub<LiveSessionHub>("/hubs/live-session");

// --- KHỞI TẠO STORAGE BUCKET ---
using (var scope = app.Services.CreateScope())
{
    var storageService = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
    await storageService.EnsureBucketExistsAsync(StorageBucket.Avatars);
}

// --- KHỞI TẠO FIREBASE ---
var firebaseJson = app.Configuration["Firebase:ServiceAccountJson"];
var credentialPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "serviceAccountKey.json");

if (!string.IsNullOrEmpty(firebaseJson))
{
    FirebaseApp.Create(new AppOptions()
    {
        Credential = GoogleCredential.FromJson(firebaseJson)
    });
    Console.WriteLine("✅ Firebase Admin SDK đã khởi tạo thành công (từ env var)!");
}
else if (File.Exists(credentialPath))
{
    FirebaseApp.Create(new AppOptions()
    {
        Credential = GoogleCredential.FromFile(credentialPath)
    });
    Console.WriteLine("✅ Firebase Admin SDK has been successfully initialized!");
}
else
{
    Console.WriteLine("⚠️ ServiceAccountKey.json file not found - Push notifications will not work.");
}

app.Run();
