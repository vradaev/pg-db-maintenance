namespace DatabaseMaintenance.Models;

public class MaintenanceSettings
{
    public VacuumSettings VacuumTables { get; set; } = new();
    public ReindexSettings Reindex { get; set; } = new();
    public WaypointCleanupSettings WaypointCleanup { get; set; } = new();
}

public class VacuumSettings
{
    public bool Enabled { get; set; } = true;
    public string Schedule { get; set; } = string.Empty;
}

public class ReindexSettings
{
    public bool Enabled { get; set; } = true;
    public string Schedule { get; set; } = string.Empty;
    public string[] Tables { get; set; } = Array.Empty<string>();
}

public class WaypointCleanupSettings
{
    public bool Enabled { get; set; } = true;
    public string Schedule { get; set; } = "0 0 1 * * ?"; // каждый день в 1:00
    public bool DeleteWithOrderId { get; set; } = false;
    public int RetentionMonths { get; set; } = 24; // по умолчанию 24 месяца (2 года)
    public int BatchSize { get; set; } = 100_000; // размер батча для удаления
    public string StartTime { get; set; } = "01:00"; // время начала очистки
    public string EndTime { get; set; } = "05:00";   // время окончания очистки
} 