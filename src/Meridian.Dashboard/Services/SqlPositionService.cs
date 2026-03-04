using Microsoft.Data.SqlClient;
using Meridian.Common.Models;

namespace Meridian.Dashboard.Services;

public class SqlPositionService : IDisposable
{
    private readonly string _connectionString;

    public SqlPositionService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<List<Position>> GetOpenPositionsAsync()
    {
        var positions = new List<Position>();
        if (string.IsNullOrEmpty(_connectionString)) return positions;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT * FROM Positions WHERE IsOpen = 1 ORDER BY PositionId", conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            positions.Add(new Position(
                reader.GetString(reader.GetOrdinal("PositionId")),
                new Option(
                    reader.GetString(reader.GetOrdinal("Symbol")),
                    reader.GetString(reader.GetOrdinal("Underlier")),
                    Enum.Parse<OptionType>(reader.GetString(reader.GetOrdinal("OptionType")), true),
                    reader.GetDecimal(reader.GetOrdinal("Strike")),
                    reader.GetDateTime(reader.GetOrdinal("Expiry")),
                    ExerciseStyle.European),
                reader.GetInt32(reader.GetOrdinal("Quantity")),
                reader.GetDecimal(reader.GetOrdinal("EntryPrice")),
                reader.GetDateTime(reader.GetOrdinal("EntryTime"))));
        }
        return positions;
    }

    public async Task<Position?> GetPositionAsync(string positionId)
    {
        if (string.IsNullOrEmpty(_connectionString)) return null;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("SELECT * FROM Positions WHERE PositionId = @Id AND IsOpen = 1", conn);
        cmd.Parameters.AddWithValue("@Id", positionId);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new Position(
                reader.GetString(reader.GetOrdinal("PositionId")),
                new Option(
                    reader.GetString(reader.GetOrdinal("Symbol")),
                    reader.GetString(reader.GetOrdinal("Underlier")),
                    Enum.Parse<OptionType>(reader.GetString(reader.GetOrdinal("OptionType")), true),
                    reader.GetDecimal(reader.GetOrdinal("Strike")),
                    reader.GetDateTime(reader.GetOrdinal("Expiry")),
                    ExerciseStyle.European),
                reader.GetInt32(reader.GetOrdinal("Quantity")),
                reader.GetDecimal(reader.GetOrdinal("EntryPrice")),
                reader.GetDateTime(reader.GetOrdinal("EntryTime")));
        }
        return null;
    }

    public async Task SavePositionAsync(Position position)
    {
        if (string.IsNullOrEmpty(_connectionString)) return;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(@"
            MERGE Positions AS target
            USING (SELECT @PositionId) AS source(PositionId)
            ON target.PositionId = source.PositionId
            WHEN NOT MATCHED THEN INSERT (PositionId, Symbol, Underlier, OptionType, Strike, Expiry, Quantity, EntryPrice, EntryTime, IsOpen)
                VALUES (@PositionId, @Symbol, @Underlier, @OptionType, @Strike, @Expiry, @Quantity, @EntryPrice, @EntryTime, 1);", conn);

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

    public async Task ClosePositionAsync(string positionId)
    {
        if (string.IsNullOrEmpty(_connectionString)) return;

        using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("UPDATE Positions SET IsOpen = 0 WHERE PositionId = @Id", conn);
        cmd.Parameters.AddWithValue("@Id", positionId);
        await cmd.ExecuteNonQueryAsync();
    }

    public void Dispose() { }
}
