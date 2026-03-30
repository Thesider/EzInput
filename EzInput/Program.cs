using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using BussinessObject.Entities;
using Controller.Implement;
using Controller.Interface;
using DAO;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Repository.Implement;
using Repository.Interface;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<EzInputDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<User, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 6;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddEntityFrameworkStores<EzInputDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IFileTemplateRepository, FileTemplateRepository>();
builder.Services.AddScoped<IHtmlSanitizationService, HtmlSanitizationService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IFileTemplateService, FileTemplateService>();
builder.Services.AddScoped<IOcrService, TesseractOcrService>();
builder.Services.AddScoped<IOcrTableFillService, OcrTableFillService>();
builder.Services.AddScoped<ISpeechToTextService, WhisperSpeechToTextService>();
builder.Services.AddScoped<ITemplateStoreService, FileTemplateStoreService>();

// Admin service
builder.Services.AddScoped<IAdminService, AdminService>();

// Authorization policies: Admin role OR specific permission claims
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("ManageUsers", policy => policy.RequireAssertion(context =>
        context.User.IsInRole("Admin") || context.User.HasClaim("permission", "manage_users")));
    options.AddPolicy("ManageDocuments", policy => policy.RequireAssertion(context =>
        context.User.IsInRole("Admin") || context.User.HasClaim("permission", "manage_docs")));
    options.AddPolicy("ViewStats", policy => policy.RequireAssertion(context =>
        context.User.IsInRole("Admin") || context.User.HasClaim("permission", "view_stats")));
    options.AddPolicy("ManageRoles", policy => policy.RequireAssertion(context =>
        context.User.IsInRole("Admin") || context.User.HasClaim("permission", "manage_roles")));
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Seed roles and an initial admin user (if configured in appsettings)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    // run seeding synchronously during startup
    SeedAdminIfConfiguredAsync(services).GetAwaiter().GetResult();
}

static async Task SeedAdminIfConfiguredAsync(IServiceProvider services)
{
    try
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<User>>();
        var configuration = services.GetRequiredService<IConfiguration>();

        var adminEmail = configuration["AdminUser:Email"];
        var adminPassword = configuration["AdminUser:Password"];
        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            return; // nothing configured
        }

        const string adminRole = "Admin";
        var claims = new[] { "manage_users", "manage_docs", "view_stats", "manage_roles" };

        if (!await roleManager.RoleExistsAsync(adminRole))
        {
            await roleManager.CreateAsync(new IdentityRole(adminRole));
        }

        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new User { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            var createResult = await userManager.CreateAsync(adminUser, adminPassword);
            if (createResult.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, adminRole);
                foreach (var c in claims)
                {
                    await userManager.AddClaimAsync(adminUser, new Claim("permission", c));
                }
            }
        }
        else
        {
            if (!await userManager.IsInRoleAsync(adminUser, adminRole))
            {
                await userManager.AddToRoleAsync(adminUser, adminRole);
            }

            var existing = await userManager.GetClaimsAsync(adminUser);
            foreach (var c in claims)
            {
                if (!existing.Any(x => x.Type == "permission" && x.Value == c))
                {
                    await userManager.AddClaimAsync(adminUser, new Claim("permission", c));
                }
            }
        }
    }
    catch
    {
        // swallowing startup seeding errors to avoid blocking dev startup; inspect logs if needed
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
