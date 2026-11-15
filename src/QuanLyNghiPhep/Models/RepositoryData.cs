namespace QuanLyNghiPhep.Models;

public sealed class RepositoryData
{
    public List<ClassGroup> Classes { get; set; } = new();
    public List<Student> Students { get; set; } = new();
    public List<LeaveRequest> Requests { get; set; } = new();
    public List<AuditEntry> Audit { get; set; } = new();
    public int NextId { get; set; } = 1;
    public int DataVersion { get; set; } = 2;
}
