using Microsoft.EntityFrameworkCore;
using MV.ApplicationLayer.Interfaces;
using MV.DomainLayer.Constants;
using MV.DomainLayer.Entities;

namespace MV.ApplicationLayer.Helpers;

public static class ClassSessionLockHelper
{
    /// <summary>
    /// Locks the class-session row (FOR UPDATE) against a real relational database in production.
    /// EF Core's InMemory provider (used by unit tests) can't translate raw SQL and has no real
    /// concurrent writers to protect against anyway, so it falls back to a plain filtered query.
    /// </summary>
    public static IQueryable<ClassSession> LockById(IAppDbContext db, int classSessionId)
        => db.Database.IsRelational()
            ? db.ClassSessions.FromSqlRaw(SqlQueries.LockClassSessionById, classSessionId)
            : db.ClassSessions.Where(cs => cs.Classsessionid == classSessionId);
}
