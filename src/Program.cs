using System.Text;
using System.Text.Json;
using LmsPlatform.Data;
using LmsPlatform.Middleware;
using LmsPlatform.Models;
using LmsPlatform.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 1. Add Controllers
builder.Services.AddControllers();

// 2. Add DbContext
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
                       ?? "Data Source=lms.db";
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionString));

// 3. Register Application Services
builder.Services.AddScoped<IAuthService, AuthService>();

// 4. Configure JWT Authentication & Authorization
var jwtSigningKey = builder.Configuration["Jwt:SigningKey"] 
    ?? "SuperSecretJwtSigningKeyForLmsPlatformProject2026!";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "LmsPlatformApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "LmsPlatformClients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),
        ClockSkew = TimeSpan.Zero
    };

    // Custom 401 & 403 JSON responses
    options.Events = new JwtBearerEvents
    {
        OnChallenge = context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            var response = JsonSerializer.Serialize(new
            {
                statusCode = 401,
                message = "Unauthorized: Authentication token is missing or invalid."
            });
            return context.Response.WriteAsync(response);
        },
        OnForbidden = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            var response = JsonSerializer.Serialize(new
            {
                statusCode = 403,
                message = "Forbidden: You do not have permission to access this resource."
            });
            return context.Response.WriteAsync(response);
        }
    };
});

// 5. Configure Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(UserRole.Admin.ToString()));
    options.AddPolicy("TeacherOnly", policy => policy.RequireRole(UserRole.Teacher.ToString()));
    options.AddPolicy("StudentOnly", policy => policy.RequireRole(UserRole.Student.ToString()));
    options.AddPolicy("TeacherOrAdmin", policy => policy.RequireRole(UserRole.Teacher.ToString(), UserRole.Admin.ToString()));
});

// 6. Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 7. Swagger Configuration with Bearer Token support
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "LMS Platform API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
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
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Ensure DB is created
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
}

// Global Exception Handler Middleware
app.UseMiddleware<ErrorHandlingMiddleware>();

if (app.Environment.IsDevelopment() || true)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "LMS Platform API v1");
    });
}

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

