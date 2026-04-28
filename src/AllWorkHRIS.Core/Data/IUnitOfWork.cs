// AllWorkHRIS.Core/Data/IUnitOfWork.cs
using System.Data;

namespace AllWorkHRIS.Core.Data;

public interface IUnitOfWork : IDisposable
{
    IDbConnection Connection { get; }
    IDbTransaction Transaction { get; }
    void Commit();
    void Rollback();
}

public sealed class UnitOfWork : IUnitOfWork
{
    public IDbConnection Connection { get; }
    public IDbTransaction Transaction { get; }

    public UnitOfWork(IConnectionFactory connectionFactory)
    {
        Connection = connectionFactory.CreateConnection();
        Transaction = Connection.BeginTransaction();
    }

    public void Commit() => Transaction.Commit();
    public void Rollback() => Transaction.Rollback();

    public void Dispose()
    {
        Transaction.Dispose();
        Connection.Dispose();
    }
}
