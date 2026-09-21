using System.Data;
using Microsoft.Data.SqlClient;

namespace ApptechDashboard.Services;

public static class TravelEvaluationCleanup
{
    public const string DeleteRelatedSql = """
        DELETE FROM dbo.TblChamCongTravelEvaluation
        WHERE CurrentAttendanceId = @AttendanceId
           OR PreviousAttendanceId = @AttendanceId
        """;

    public static async Task DeleteRelatedAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int attendanceId,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeleteRelatedSql;
        command.Parameters.Add(new SqlParameter("@AttendanceId", SqlDbType.Int) { Value = attendanceId });
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<bool> ExecuteAttendanceDeleteAsync(
        Func<Task> cleanupEvaluation,
        Func<Task<int>> deleteAttendance,
        Func<Task> rollback)
    {
        await cleanupEvaluation();
        if (await deleteAttendance() > 0)
        {
            return true;
        }

        await rollback();
        return false;
    }
}
