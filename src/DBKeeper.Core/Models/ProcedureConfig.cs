namespace DBKeeper.Core.Models;

public class ProcedureConfig
{
    public string DatabaseName { get; set; } = string.Empty;
    public string ProcedureName { get; set; } = string.Empty;
    public List<SpParameter>? Parameters { get; set; }
    public int TimeoutSec { get; set; } = 300;
}

public class SpParameter
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
