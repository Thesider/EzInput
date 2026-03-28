using Microsoft.AspNetCore.Identity;

namespace BussinessObject.Entities;

public class User : IdentityUser
{
    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public ICollection<FileTemplate> FileTemplates { get; set; } = new List<FileTemplate>();
}
