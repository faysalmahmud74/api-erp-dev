using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Ordernest.Backend.Data;
using Ordernest.Backend.Models;
using Ordernest.Backend.Services;
using Swashbuckle.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// ---------- 1. Database ----------
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ---------- 2. Identity: users, roles, password hashing ----------
// AddIdentityCore (NOT AddIdentity): AddIdentity installs cookie schemes that
// 302-redirect unauthenticated requests to /Account/Login, which breaks JSON clients.
// AddIdentityCore gives the same managers without touching authentication schemes.
builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false; // friendlier for counter staff
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    })
    .AddRoles<IdentityRole>()                    // enables role checks
    .AddEntityFrameworkStores<AppDbContext>()    // stores users/roles in OUR database
    .AddSignInManager()                          // password check + lockout tracking
    .AddDefaultTokenProviders();

// ---------- 3. JWT bearer authentication ----------
var jwt = builder.Configuration.GetSection("Jwt");
var jwtKey = jwt["Key"] ?? string.Empty;
if (jwtKey.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 characters for HMAC-SHA256. Check appsettings.json.");

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromSeconds(30), // default is 5 minutes; tighten it
            NameClaimType = "name",
            RoleClaimType = "role"                // matches the claims TokenService writes
        };
        // Keep claim names exactly as written in the token (no MS-URI remapping),
        // so "role"/"sub" stay "role"/"sub" and the settings above line up.
        options.MapInboundClaims = false;
    });

builder.Services.AddAuthorization();

// ---------- 4. CORS for the React dev server ----------
builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactDev", policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// ---------- 5. MVC controllers (enums serialized as strings in JSON) ----------
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddProblemDetails(); // RFC 7807 JSON errors from UseExceptionHandler

// ---------- 6. App services ----------
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IOrderService, OrderService>();

// ---------- 7. Swagger (Swashbuckle) with JWT bearer support ----------
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Ordernest POS API",
        Version = "v1",
        Description = "Backend API for the Ordernest point-of-sale React app."
    });

    // The "Authorize" button in Swagger UI: paste a token from /api/auth/login.
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT from POST /api/auth/login (without the word 'Bearer')."
    });

    // Swashbuckle 10 takes a delegate; Microsoft.OpenApi v2 references schemes
    // via OpenApiSecuritySchemeReference.
    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", null, null)] = []
    });
});

var app = builder.Build();

// ---------- Pipeline: order matters ----------
// 1. Exception handler first: anything downstream returns JSON ProblemDetails.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();    // serves /swagger/v1/swagger.json
    app.UseSwaggerUI();  // serves /swagger — the interactive docs UI
}

app.UseHttpsRedirection();

// 2. CORS BEFORE authentication: a browser preflight (OPTIONS) carries no
//    Authorization header and must not be challenged as unauthenticated.
app.UseCors("ReactDev");

// 3. Authentication ("who are you?") before Authorization ("are you allowed?").
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ---------- 8. Seed roles + first users on startup ----------
await DbSeeder.SeedAsync(app.Services, app.Configuration,
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder"));

app.Run();
