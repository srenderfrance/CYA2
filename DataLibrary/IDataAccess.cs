using System.Threading;

namespace DataLibrary
{
    public interface IDataAccess
    {
        bool IsConnected { get; }
        string LastError { get; }
        Task<List<T>> LoadData<T, U>(string sql, U parameters, string connectionString);
        Task<int> SaveData<T>(string sql, T parameters, string connectionString, CancellationToken cancellationToken = default);
        Task<bool> CheckConnection(string connectionString);
        bool ValidateConnectionString(string connectionString);
    }
}