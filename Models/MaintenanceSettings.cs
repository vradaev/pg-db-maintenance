namespace DatabaseMaintenance.Models;

public class MaintenanceSettings
{
    public VacuumSettings VacuumTables { get; set; } = new();
    public ReindexSettings Reindex { get; set; } = new();
}

public class VacuumSettings
{
    public string Schedule { get; set; } = string.Empty;
}

public class ReindexSettings
{
    public string Schedule { get; set; } = string.Empty;
    public string[] Tables { get; set; } = Array.Empty<string>();
} 