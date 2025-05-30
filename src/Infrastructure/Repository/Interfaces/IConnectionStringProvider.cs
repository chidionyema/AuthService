using System.Threading.Tasks;

namespace auth.Infrastructure.Repository.Interfaces
{
    public interface IConnectionStringProvider
    {
        Task<int> GetLeaseDurationAsync();
        Task<string> GetConnectionStringAsync();
        Task UpdateConnectionStringAsync();
    }
}