namespace QuanLyNghiPhep.Services.Accounts;

public sealed class UserAccessException(string message) : UnauthorizedAccessException(message);
