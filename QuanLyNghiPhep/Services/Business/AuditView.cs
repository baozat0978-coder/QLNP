namespace QuanLyNghiPhep.Services.Business;

public sealed record AuditView(DateTime At, string User, string Action, string Detail);
