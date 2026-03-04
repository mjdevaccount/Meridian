using Microsoft.Data.SqlClient;
using Meridian.Common.Models;

namespace Meridian.Risk.State;

public class SqlPositionRepository : IDisposable
{
    private readonly string _connectionString;

    public SqlPositionRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeSchemaAsync()
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();

        var commands = new[]
        {
            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Positions' AND xtype='U')
              CREATE TABLE Positions (
                  PositionId VARCHAR(50) PRIMARY KEY,
                  Symbol VARCHAR(20) NOT NULL,
                  Underlier VARCHAR(20) NOT NULL,
                  OptionType VARCHAR(4) NOT NULL,
                  Strike DECIMAL(18,6) NOT NULL,
                  Expiry DATE NOT NULL,
                  Quantity INT NOT NULL,
                  EntryPrice DECIMAL(18,6) NOT NULL,
                  EntryTime DATETIME2 NOT NULL,
                  IsOpen BIT NOT NULL DEFAULT 1)",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='PnlSnapshots' AND xtype='U')
              CREATE TABLE PnlSnapshots (
                  Id BIGINT IDENTITY PRIMARY KEY,
                  PositionId VARCHAR(50) NOT NULL,
                  MarkToMarket DECIMAL(18,6),
                  UnrealizedPnl DECIMAL(18,6),
                  TotalPnl DECIMAL(18,6),
                  SnapshotTime DATETIME2 NOT NULL)",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='GreeksSnapshots' AND xtype='U')
              CREATE TABLE GreeksSnapshots (
                  Id BIGINT IDENTITY PRIMARY KEY,
                  PositionId VARCHAR(50) NOT NULL,
                  Delta DECIMAL(18,8),
                  Gamma DECIMAL(18,8),
                  Vega DECIMAL(18,8),
                  Theta DECIMAL(18,8),
                  Rho DECIMAL(18,8),
                  SnapshotTime DATETIME2 NOT NULL)",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='RiskAlerts' AND xtype='U')
              CREATE TABLE RiskAlerts (
                  Id BIGINT IDENTITY PRIMARY KEY,
                  AlertType VARCHAR(50) NOT NULL,
                  Severity VARCHAR(20) NOT NULL,
                  Threshold DECIMAL(18,6),
                  CurrentValue DECIMAL(18,6),
                  Description NVARCHAR(500),
                  AlertTime DATETIME2 NOT NULL,
                  AcknowledgedAt DATETIME2 NULL)",

            @"IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='TradeBlotter' AND xtype='U')
              CREATE TABLE TradeBlotter (
                  TradeId BIGINT IDENTITY PRIMARY KEY,
                  PositionId VARCHAR(50) NOT NULL,
                  Side VARCHAR(4) NOT NULL,
                  Quantity INT NOT NULL,
                  Price DECIMAL(18,6) NOT NULL,
                  TradeTime DATETIME2 NOT NULL)"
        };

        foreach (var sql in commands)
        {
            using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task SavePositionAsync(Position position)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(@"
            MERGE Positions AS target
            USING (SELECT @PositionId) AS source(PositionId)
            ON target.PositionId = source.PositionId
            WHEN NOT MATCHED THEN INSERT (PositionId, Symbol, Underlier, OptionType, Strike, Expiry, Quantity, EntryPrice, EntryTime)
                VALUES (@PositionId, @Symbol, @Underlier, @OptionType, @Strike, @Expiry, @Quantity, @EntryPrice, @EntryTime);", conn);

        cmd.Parameters.AddWithValue("@PositionId", position.PositionId);
        cmd.Parameters.AddWithValue("@Symbol", position.Instrument.Symbol);
        cmd.Parameters.AddWithValue("@Underlier", position.Instrument.Underlier);
        cmd.Parameters.AddWithValue("@OptionType", position.Instrument.Type.ToString().ToUpper());
        cmd.Parameters.AddWithValue("@Strike", position.Instrument.Strike);
        cmd.Parameters.AddWithValue("@Expiry", position.Instrument.Expiry.Date);
        cmd.Parameters.AddWithValue("@Quantity", position.Quantity);
        cmd.Parameters.AddWithValue("@EntryPrice", position.EntryPrice);
        cmd.Parameters.AddWithValue("@EntryTime", position.EntryTime);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SavePnlSnapshotAsync(PnlUpdate pnl)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(@"
            INSERT INTO PnlSnapshots (PositionId, MarkToMarket, UnrealizedPnl, TotalPnl, SnapshotTime)
            VALUES (@PositionId, @Mtm, @Unrealized, @Total, @Time)", conn);
        cmd.Parameters.AddWithValue("@PositionId", pnl.PositionId);
        cmd.Parameters.AddWithValue("@Mtm", pnl.MarkToMarket);
        cmd.Parameters.AddWithValue("@Unrealized", pnl.UnrealizedPnl);
        cmd.Parameters.AddWithValue("@Total", pnl.TotalPnl);
        cmd.Parameters.AddWithValue("@Time", pnl.Timestamp);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveGreeksSnapshotAsync(string positionId, GreeksResult greeks, DateTime timestamp)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(@"
            INSERT INTO GreeksSnapshots (PositionId, Delta, Gamma, Vega, Theta, Rho, SnapshotTime)
            VALUES (@PositionId, @Delta, @Gamma, @Vega, @Theta, @Rho, @Time)", conn);
        cmd.Parameters.AddWithValue("@PositionId", positionId);
        cmd.Parameters.AddWithValue("@Delta", greeks.Delta);
        cmd.Parameters.AddWithValue("@Gamma", greeks.Gamma);
        cmd.Parameters.AddWithValue("@Vega", greeks.Vega);
        cmd.Parameters.AddWithValue("@Theta", greeks.Theta);
        cmd.Parameters.AddWithValue("@Rho", greeks.Rho);
        cmd.Parameters.AddWithValue("@Time", timestamp);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveRiskAlertAsync(RiskAlert alert)
    {
        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(@"
            INSERT INTO RiskAlerts (AlertType, Severity, Threshold, CurrentValue, Description, AlertTime)
            VALUES (@AlertType, @Severity, @Threshold, @CurrentValue, @Description, @AlertTime)", conn);
        cmd.Parameters.AddWithValue("@AlertType", alert.Type.ToString());
        cmd.Parameters.AddWithValue("@Severity", alert.Severity.ToString());
        cmd.Parameters.AddWithValue("@Threshold", alert.Threshold);
        cmd.Parameters.AddWithValue("@CurrentValue", alert.CurrentValue);
        cmd.Parameters.AddWithValue("@Description", alert.Description);
        cmd.Parameters.AddWithValue("@AlertTime", alert.Timestamp);
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose() { }
}
