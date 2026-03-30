using System.Collections.Generic;

namespace Controller.Models;

public class AdminUserEditViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> AllRoles { get; set; } = new List<string>();
    public List<string> AssignedRoles { get; set; } = new List<string>();
    public List<string> AllPermissions { get; set; } = new List<string>();
    public List<string> AssignedPermissions { get; set; } = new List<string>();
    public string? NewPassword { get; set; }
}
