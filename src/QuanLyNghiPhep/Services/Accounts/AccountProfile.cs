namespace QuanLyNghiPhep.Services.Accounts;

public sealed record AccountProfile(
    string FullName,
    DateTime? DateOfBirth,
    string Gender,
    string PhoneNumber,
    string MajorCode);
