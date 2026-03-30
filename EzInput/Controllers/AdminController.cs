using Controller.Interface;
using Controller.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BussinessObject.Entities;
using Microsoft.AspNetCore.Identity;
using DAO;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.RegularExpressions;

namespace EzInput.Controllers;

[Authorize]
[Route("Admin")]
public class AdminController : Microsoft.AspNetCore.Mvc.Controller
{
    private readonly IAdminService _adminService;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly EzInputDbContext _dbContext;

    public AdminController(IAdminService adminService, UserManager<User> userManager, RoleManager<IdentityRole> roleManager, EzInputDbContext dbContext)
    {
        _adminService = adminService;
        _userManager = userManager;
        _roleManager = roleManager;
        _dbContext = dbContext;
    }

    [HttpGet("")]
    [Authorize(Policy = "ViewStats")]
    public async Task<IActionResult> Index()
    {
        var model = await _adminService.GetDashboardAsync();
        return View(model);
    }

    [HttpGet("Users")]
    [Authorize(Policy = "ManageUsers")]
    public async Task<IActionResult> Users(string? query, int page = 1, int pageSize = 20, string sortBy = "email", string sortDir = "asc")
    {
        var q = _userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            q = q.Where(u => u.Email!.Contains(query));
        }

        // sorting
        q = (sortBy, sortDir?.ToLowerInvariant()) switch
        {
            ("email", "desc") => q.OrderByDescending(u => u.Email),
            ("email", _) => q.OrderBy(u => u.Email),
            _ => q.OrderBy(u => u.Email)
        };

        var total = await q.CountAsync();
        var users = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var rows = new List<AdminUserRowModel>();
        foreach (var user in users)
        {
            var roles = (await _userManager.GetRolesAsync(user)).ToList();
            rows.Add(new AdminUserRowModel { Id = user.Id, Email = user.Email ?? string.Empty, Roles = roles });
        }

        var model = new AdminUsersViewModel
        {
            Users = rows,
            CurrentPage = page,
            PageSize = pageSize,
            TotalItems = total,
            TotalPages = total == 0 ? 1 : (int)Math.Ceiling((double)total / pageSize),
            SortBy = sortBy,
            SortDir = sortDir ?? "asc",
            Query = query ?? string.Empty
        };

        return View(model);
    }

    [HttpGet("Users/Edit/{id}")]
    [Authorize(Policy = "ManageUsers")]
    public async Task<IActionResult> EditUser(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var allRoles = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();
        var assignedRoles = (await _userManager.GetRolesAsync(user)).ToList();
        var assignedClaims = (await _userManager.GetClaimsAsync(user)).Where(c => c.Type == "permission").Select(c => c.Value).ToList();

        var permissions = new List<string> { "manage_users", "manage_docs", "view_stats", "manage_roles" };

        var model = new AdminUserEditViewModel
        {
            Id = user.Id,
            Email = user.Email ?? string.Empty,
            AllRoles = allRoles,
            AssignedRoles = assignedRoles,
            AllPermissions = permissions,
            AssignedPermissions = assignedClaims
        };

        return View(model);
    }

    [HttpPost("Users/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(AdminUserEditViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await _userManager.FindByIdAsync(model.Id);
        if (user == null) return NotFound();

        var currentRoles = (await _userManager.GetRolesAsync(user)).ToList();
        var rolesToAdd = model.AssignedRoles.Except(currentRoles).ToList();
        var rolesToRemove = currentRoles.Except(model.AssignedRoles).ToList();
        if (rolesToAdd.Any())
        {
            await _userManager.AddToRolesAsync(user, rolesToAdd);
        }
        if (rolesToRemove.Any())
        {
            await _userManager.RemoveFromRolesAsync(user, rolesToRemove);
        }

        var currentClaims = (await _userManager.GetClaimsAsync(user)).Where(c => c.Type == "permission").Select(c => c.Value).ToList();
        var claimsToAdd = model.AssignedPermissions.Except(currentClaims).Select(p => new System.Security.Claims.Claim("permission", p)).ToList();
        var claimsToRemove = (await _userManager.GetClaimsAsync(user)).Where(c => c.Type == "permission" && !model.AssignedPermissions.Contains(c.Value)).ToList();
        if (claimsToRemove.Any())
        {
            await _userManager.RemoveClaimsAsync(user, claimsToRemove);
        }
        if (claimsToAdd.Any())
        {
            await _userManager.AddClaimsAsync(user, claimsToAdd);
        }

        if (!string.IsNullOrWhiteSpace(model.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var reset = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!reset.Succeeded)
            {
                foreach (var err in reset.Errors)
                {
                    ModelState.AddModelError("", err.Description);
                }
                model.AllRoles = await _roleManager.Roles.Select(r => r.Name!).ToListAsync();
                model.AllPermissions = new List<string> { "manage_users", "manage_docs", "view_stats", "manage_roles" };
                return View(model);
            }
        }

        return RedirectToAction(nameof(Users));
    }

    [HttpPost("Users/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return BadRequest();
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError("", "Unable to delete user.");
        }
        return RedirectToAction(nameof(Users));
    }

    // Documents endpoints
    [HttpGet("Documents")]
    [Authorize(Policy = "ManageDocuments")]
    public async Task<IActionResult> Documents(string? query, int page = 1, int pageSize = 20, string sortBy = "updated", string sortDir = "desc")
    {
        var q = _dbContext.Documents.Include(d => d.Owner).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query)) q = q.Where(d => d.Title.Contains(query));

        // sorting
        q = (sortBy, sortDir?.ToLowerInvariant()) switch
        {
            ("title", "asc") => q.OrderBy(d => d.Title),
            ("title", "desc") => q.OrderByDescending(d => d.Title),
            ("owner", "asc") => q.OrderBy(d => d.Owner!.Email),
            ("owner", "desc") => q.OrderByDescending(d => d.Owner!.Email),
            ("updated", "asc") => q.OrderBy(d => d.UpdatedAtUtc),
            _ => q.OrderByDescending(d => d.UpdatedAtUtc),
        };

        var total = await q.CountAsync();
        var docs = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var rows = docs.Select(d => new AdminDocumentRowModel
        {
            Id = d.Id,
            Title = d.Title,
            OwnerEmail = d.Owner?.Email ?? string.Empty,
            UpdatedAtUtc = d.UpdatedAtUtc
        }).ToList();

        var model = new AdminDocumentListViewModel
        {
            Documents = rows,
            TotalItems = total,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = total == 0 ? 1 : (int)Math.Ceiling((double)total / pageSize),
            SortBy = sortBy,
            SortDir = sortDir ?? "desc",
            Query = query ?? string.Empty
        };

        return View(model);
    }

    [HttpGet("Documents/Edit/{id}")]
    [Authorize(Policy = "ManageDocuments")]
    public async Task<IActionResult> EditDocument(int id)
    {
        var doc = await _dbContext.Documents.FindAsync(id);
        if (doc == null) return NotFound();
        var model = new AdminDocumentEditModel { Id = doc.Id, Title = doc.Title, RawHtml = doc.Content };
        // create a readable plain-text preview from HTML
        model.PlainTextPreview = ExtractPlainTextFromHtml(doc.Content);
        return View(model);
    }

    [HttpPost("Documents/Edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDocument(AdminDocumentEditModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var doc = await _dbContext.Documents.FindAsync(model.Id);
        if (doc == null) return NotFound();
        doc.Title = model.Title;
        // Only update HTML content if RawHtml present (admin edited raw HTML). Otherwise keep existing HTML.
        if (!string.IsNullOrWhiteSpace(model.RawHtml))
        {
            doc.Content = model.RawHtml;
        }
        doc.UpdatedAtUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Documents));
    }

    [HttpPost("Documents/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDocument(int id)
    {
        var doc = await _dbContext.Documents.FindAsync(id);
        if (doc == null) return NotFound();
        _dbContext.Documents.Remove(doc);
        await _dbContext.SaveChangesAsync();
        return RedirectToAction(nameof(Documents));
    }

    [HttpGet("Stats")]
    [Authorize(Policy = "ViewStats")]
    public async Task<IActionResult> Stats()
    {
        return View();
    }

    [HttpGet("Stats/Data")]
    [Authorize(Policy = "ViewStats")]
    public async Task<IActionResult> StatsData()
    {
        var days = 7;
        var today = DateTime.UtcNow.Date;
        var labels = new List<string>();
        var docCounts = new List<int>();
        var templateCounts = new List<int>();
        for (int i = days - 1; i >= 0; i--)
        {
            var d = today.AddDays(-i);
            labels.Add(d.ToString("yyyy-MM-dd"));
            var next = d.AddDays(1);
            var dc = await _dbContext.Documents.CountAsync(x => x.CreatedAtUtc >= d && x.CreatedAtUtc < next);
            var tc = await _dbContext.FileTemplates.CountAsync(x => x.CreatedAtUtc >= d && x.CreatedAtUtc < next);
            docCounts.Add(dc);
            templateCounts.Add(tc);
        }

        return Json(new { labels, docCounts, templateCounts });
    }

    private static string ExtractPlainTextFromHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var normalized = html;
        normalized = Regex.Replace(normalized, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"</(p|div|li|tr|h1|h2|h3|h4|h5|h6)>", "\n", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"<[^>]+>", " ", RegexOptions.Singleline);
        normalized = WebUtility.HtmlDecode(normalized);

        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => Regex.Replace(x, @"\s+", " ").Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        return string.Join("\n\n", lines);
    }
}
