namespace QuanLyNghiPhep.Services;

public sealed class BusinessRuleException(string message) : InvalidOperationException(message);
