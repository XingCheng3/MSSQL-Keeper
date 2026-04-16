namespace DBKeeper.Core.Models;

/// <summary>
/// 通用操作结果，Service 层统一返回类型
/// </summary>
public class OperationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    public static OperationResult Ok() => new() { Success = true };
    public static OperationResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}

/// <summary>
/// 带数据的操作结果
/// </summary>
public class OperationResult<T> : OperationResult
{
    public T? Data { get; set; }

    public static OperationResult<T> Ok(T data) => new() { Success = true, Data = data };
    public new static OperationResult<T> Fail(string error) => new() { Success = false, ErrorMessage = error };
}
