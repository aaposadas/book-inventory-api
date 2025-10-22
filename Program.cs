using BookInventory.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var allowedOrigin = builder.Configuration["AllowedOrigin"];
var jwtSettings = builder.Configuration.GetSection("Jwt");

// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Validate JWT configuration
    var jwtKey = jwtSettings["Key"];
    if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32)
    {
        throw new InvalidOperationException(
            "JWT Key must be at least 32 characters long. Configure this in appsettings.json or environment variables.");
    }

    var issuer = jwtSettings["Issuer"] ?? throw new InvalidOperationException("JWT Issuer not configured");
    var audience = jwtSettings["Audience"] ?? throw new InvalidOperationException("JWT Audience not configured");

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero // Remove default 5-minute clock skew for more precise expiration
    };

    // Configure to read JWT from cookies
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // Try to get token from cookie first
            if (context.Request.Cookies.TryGetValue("auth_token", out var token))
            {
                context.Token = token;
            }
            // Fallback to Authorization header (for API tools like Postman)
            else if (context.Request.Headers.ContainsKey("Authorization"))
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = authHeader.Substring("Bearer ".Length).Trim();
                }
            }

            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            // Log authentication failures for debugging
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning("Authentication failed: {Exception}", context.Exception.Message);
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// OpenAPI/Swagger
builder.Services.AddOpenApi();

// Controllers
builder.Services.AddControllers();

// HttpClient for external API calls
builder.Services.AddHttpClient();

// CORS Configuration
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", corsBuilder =>
    {
        var origin = allowedOrigin ?? "http://localhost:4200"; // Default to localhost if not configured

        corsBuilder.WithOrigins(origin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials() // Required for cookies
            .WithExposedHeaders("X-Total-Count", "X-Page", "X-Page-Size"); // For pagination
    });
});

// MongoDB Configuration
builder.Services.Configure<MongoDbSettings>(
    builder.Configuration.GetSection("MongoDbSettings"));

builder.Services.AddSingleton<MongoDbContext>();

var app = builder.Build();

// Middleware Pipeline
// 1. CORS 
app.UseCors("AllowAngular");

// 2. Development tools
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 3. Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// 4. Map controllers
app.MapControllers();

app.Run();