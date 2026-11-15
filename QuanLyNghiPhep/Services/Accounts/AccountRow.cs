namespace QuanLyNghiPhep.Services.Accounts;

public sealed record AccountRow(string Id, string Name, string Role, bool Enabled, bool Locked, string FullName, DateTime? DateOfBirth, string Gender, string PhoneNumber, string MajorCode, string FacultyName = "", string FacultyId = "", string Password = "");
