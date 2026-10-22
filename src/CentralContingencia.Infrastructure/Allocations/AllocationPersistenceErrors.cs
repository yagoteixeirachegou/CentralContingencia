using CentralContingencia.Application.Allocations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CentralContingencia.Infrastructure.Allocations;

internal static class AllocationPersistenceErrors
{
    public static AllocationPersistenceException Translate(Exception error)
    {
        var postgres = error as PostgresException ?? error.InnerException as PostgresException;
        if (postgres?.TableName == "OperationalEvents" || error is DbUpdateException update && postgres?.TableName is null &&
            update.Entries.Any(entry => entry.Entity is CentralContingencia.Domain.Audit.OperationalAuditEvent))
            return new(AllocationPersistenceFailure.AuditFailure);
        var failure = error is DbUpdateConcurrencyException ? AllocationPersistenceFailure.Conflict : postgres?.SqlState switch
        {
            PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure or
                PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.LockNotAvailable => AllocationPersistenceFailure.Conflict,
            PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.CheckViolation or
                PostgresErrorCodes.NotNullViolation or PostgresErrorCodes.StringDataRightTruncation => AllocationPersistenceFailure.Validation,
            _ => AllocationPersistenceFailure.Unavailable
        };
        // Never retain database detail, SQL, parameters or inner exceptions in the application error.
        return new(failure);
    }
    public static T Domain<T>(Func<T> action)
    {
        try { return action(); }
        catch (ArgumentException) { throw new AllocationPersistenceException(AllocationPersistenceFailure.Validation); }
        catch (InvalidOperationException) { throw new AllocationPersistenceException(AllocationPersistenceFailure.Conflict); }
    }
    public static void Domain(Action action) => Domain(() => { action(); return true; });
}
