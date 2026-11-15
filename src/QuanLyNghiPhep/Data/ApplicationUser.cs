using Microsoft.AspNetCore.Identity;

namespace QuanLyNghiPhep.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string ProtectedPassword { get; set; } = "";
    public bool IsEnabled { get; set; } = true;

    public string FullName { get; set; } = "";
    public DateTime? DateOfBirth
    {
        get; set;
    }
    public string MajorCode { get; set; } = "";
    public string Gender { get; set; } = "";
    public string FacultyName { get; set; } = "";
    public string FacultyId { get; set; } = "";
}
