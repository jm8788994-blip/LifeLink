using LifeLink.Data;
using LifeLink.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Render.com uses PORT environment variable; otherwise fallback to launchSettings.json in local/Visual Studio
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}

// Add services to the container.
builder.Services.AddControllersWithViews();

// Register Swagger / OpenAPI (PDF Section 6.5)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "LifeLink API",
        Version = "v1",
        Description = "LifeLink - AI Powered Blood Donation & Inventory Management System (Interactive API Explorer & Testing)"
    });
});

// Register DbContext with PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString, pg =>
    {
        pg.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
        pg.CommandTimeout(120);
    }));

// Register Application Services
builder.Services.AddSingleton<IPasswordHasherService, PasswordHasherService>();
builder.Services.AddSingleton<IOtpService, OtpService>();
builder.Services.AddScoped<IEmailSenderService, EmailSenderService>();

// Register AI & Machine Learning Services (ML.NET)
builder.Services.AddScoped<IAiMatchingService, AiMatchingService>();
builder.Services.AddScoped<IAiForecastingService, AiForecastingService>();
builder.Services.AddScoped<IAiChatbotService, AiChatbotService>();

// Register Real-Time Communication Services (SignalR)
builder.Services.AddSignalR();
builder.Services.AddScoped<INotificationService, NotificationService>();

// Configure Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.LogoutPath = "/Account/Logout";
        options.Cookie.Name = "LifeLink.Auth";
        options.Cookie.HttpOnly = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

// Initialize and seed database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    try
    {
        await DbInitializer.InitializeAsync(services);
        logger.LogInformation("Database initialized and seeded successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while migrating or seeding the database.");
        if (ex.InnerException != null)
        {
            logger.LogError(ex.InnerException, "INNER EXCEPTION: {InnerMessage}", ex.InnerException.Message);
        }
    }
}

// Security Hardening Headers Middleware (PDF Section 4.20)
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Frame-Options", "SAMEORIGIN");
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Enable Swagger UI across environments for defense demonstration & API testing
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "LifeLink API v1");
    c.RoutePrefix = "swagger";
});

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapHub<LifeLink.Hubs.LifeLinkHub>("/hubs/lifelink");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();


