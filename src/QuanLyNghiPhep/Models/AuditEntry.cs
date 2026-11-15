namespace QuanLyNghiPhep.Models;

public sealed class AuditEntry
{
    public int Id
    {
        get; set;
    }
    public DateTime At { get; set; } = DateTime.Now;
    public string User { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
}
