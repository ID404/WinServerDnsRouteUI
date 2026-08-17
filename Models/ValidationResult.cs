namespace DnsRouteUI.Models;

/// <summary>
/// 校验结果（规格第 5.2、5.3 节）。
/// 用于 CIDR、IP、重复名称、网段重叠等校验反馈。
/// </summary>
public sealed class ValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public void AddError(string message) => Errors.Add(message);

    public void AddWarning(string message) => Warnings.Add(message);

    public static ValidationResult Ok() => new();

    public static ValidationResult Fail(string error)
    {
        var r = new ValidationResult();
        r.AddError(error);
        return r;
    }
}
