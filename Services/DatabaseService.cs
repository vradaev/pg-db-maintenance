using Npgsql;
using Microsoft.Extensions.Options;
using DatabaseMaintenance.Models;

namespace DatabaseMaintenance.Services;

public class TableBloatInfo
{
    public string TableName { get; set; } = string.Empty;
    public double BloatSizeMB { get; set; }
    public double TableSizeMB { get; set; }
    public double BloatPercentage { get; set; }
    public TimeSpan VacuumDuration { get; set; }
    public double SizeAfterVacuumMB { get; set; }
}

public class DatabaseService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseService> _logger;
    private readonly IOptions<MaintenanceSettings> _maintenanceSettings;

    public DatabaseService(
        IOptions<DatabaseSettings> settings,
        IOptions<MaintenanceSettings> maintenanceSettings,
        ILogger<DatabaseService> logger)
    {
        _connectionString = settings.Value.ConnectionString;
        _maintenanceSettings = maintenanceSettings;
        _logger = logger;
    }

    public async Task<List<TableBloatInfo>> GetBloatTablesInfo()
    {
        var tables = new List<TableBloatInfo>();
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var query = @"
            SELECT 
                tablename,
                mb_bloat,
                table_mb,
                pct_bloat
            FROM bloat_monitor";

        await using var cmd = new NpgsqlCommand(query, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        
        while (await reader.ReadAsync())
        {
            tables.Add(new TableBloatInfo
            {
                TableName = reader.GetString(0),
                BloatSizeMB = reader.GetDouble(1),
                TableSizeMB = reader.GetDouble(2),
                BloatPercentage = reader.GetDouble(3)
            });
        }

        _logger.LogInformation("Найдено {Count} раздутых таблиц", tables.Count);
        return tables;
    }

    public async Task<TableBloatInfo> VacuumFullTable(TableBloatInfo tableInfo)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var startTime = DateTime.UtcNow;
        
        var query = $"VACUUM FULL {tableInfo.TableName}";
        await using var cmd = new NpgsqlCommand(query, conn);
        await cmd.ExecuteNonQueryAsync();
        
        tableInfo.VacuumDuration = DateTime.UtcNow - startTime;

        // Получаем размер таблицы после VACUUM в мегабайтах
        query = $"SELECT pg_total_relation_size('{tableInfo.TableName}') / 1024.0 / 1024.0";
        cmd.CommandText = query;
        tableInfo.SizeAfterVacuumMB = (double)await cmd.ExecuteScalarAsync();
        
        _logger.LogInformation("VACUUM FULL completed for table {TableName}. Duration: {Duration}, Size after: {SizeAfter} MB",
            tableInfo.TableName, tableInfo.VacuumDuration, tableInfo.SizeAfterVacuumMB);

        return tableInfo;
    }

    public async Task ReindexTable(string tableName)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        var query = $"REINDEX TABLE {tableName}";
        await using var cmd = new NpgsqlCommand(query, conn);
        await cmd.ExecuteNonQueryAsync();
        
        _logger.LogInformation("REINDEX completed for table {TableName}", tableName);
    }

    public async Task<MaintenanceStats> VacuumTablesAsync()
    {
        var startTime = DateTime.UtcNow;
        var tablesProcessed = 0;
        var totalSpaceFreed = 0L;
        var tableStats = new List<TableStats>();

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        // Получаем список раздутых таблиц
        var tables = await GetBloatTablesAsync(conn);
        tablesProcessed = tables.Count;

        if (tablesProcessed == 0)
        {
            _logger.LogInformation("Нет раздутых таблиц для обслуживания");
            return new MaintenanceStats
            {
                TablesProcessed = 0,
                TotalTime = DateTime.UtcNow - startTime,
                TotalSpaceFreed = 0,
                TableStats = new List<TableStats>()
            };
        }

        foreach (var table in tables)
        {
            _logger.LogInformation("VACUUM FULL for table {Table}", table);
            
            // Получаем размер до VACUUM
            var sizeBefore = await GetTableSizeAsync(conn, table);
            
            // Выполняем VACUUM
            await using var cmd = new NpgsqlCommand($"VACUUM FULL \"{table}\"", conn);
            await cmd.ExecuteNonQueryAsync();
            
            // Получаем размер после VACUUM
            var sizeAfter = await GetTableSizeAsync(conn, table);
            var spaceFreed = sizeBefore - sizeAfter;
            totalSpaceFreed += spaceFreed;

            tableStats.Add(new TableStats
            {
                TableName = table,
                SizeBefore = sizeBefore,
                SizeAfter = sizeAfter,
                SpaceFreed = spaceFreed
            });
        }

        return new MaintenanceStats
        {
            TablesProcessed = tablesProcessed,
            TotalTime = DateTime.UtcNow - startTime,
            TotalSpaceFreed = totalSpaceFreed,
            TableStats = tableStats
        };
    }

    public async Task<MaintenanceStats> ReindexTablesAsync()
    {
        var startTime = DateTime.UtcNow;
        var tablesProcessed = 0;
        var tableStats = new List<TableStats>();

        var tables = _maintenanceSettings.Value.Reindex.Tables;
        if (tables.Length == 0)
        {
            _logger.LogInformation("Нет таблиц для реиндексации");
            return new MaintenanceStats
            {
                TablesProcessed = 0,
                TotalTime = DateTime.UtcNow - startTime,
                TableStats = new List<TableStats>()
            };
        }

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();

        tablesProcessed = tables.Length;

        foreach (var table in tables)
        {
            _logger.LogInformation("REINDEX for table {Table}", table);
            var tableStartTime = DateTime.UtcNow;
            
            await using var cmd = new NpgsqlCommand($"REINDEX TABLE \"{table}\"", conn);
            await cmd.ExecuteNonQueryAsync();
            
            var duration = DateTime.UtcNow - tableStartTime;
            tableStats.Add(new TableStats
            {
                TableName = table,
                Duration = duration
            });
        }

        return new MaintenanceStats
        {
            TablesProcessed = tablesProcessed,
            TotalTime = DateTime.UtcNow - startTime,
            TableStats = tableStats
        };
    }

    private async Task<List<string>> GetBloatTablesAsync(NpgsqlConnection conn)
    {
        var tables = new List<string>();
        await using var cmd = new NpgsqlCommand(
            "SELECT tablename FROM bloat_monitor",
            conn
        );
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        return tables;
    }

    private async Task<long> GetTableSizeAsync(NpgsqlConnection conn, string tableName)
    {
        await using var cmd = new NpgsqlCommand(
            $"SELECT pg_total_relation_size('\"{tableName}\"')",
            conn
        );
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }
}

public class MaintenanceStats
{
    public int TablesProcessed { get; set; }
    public TimeSpan TotalTime { get; set; }
    public long TotalSpaceFreed { get; set; }
    public List<TableStats> TableStats { get; set; } = new();
}

public class TableStats
{
    public string TableName { get; set; } = string.Empty;
    public long SizeBefore { get; set; }
    public long SizeAfter { get; set; }
    public long SpaceFreed { get; set; }
    public TimeSpan Duration { get; set; }
} 