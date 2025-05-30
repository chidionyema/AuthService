using System.Threading.Tasks;
using auth.Db;

namespace auth.Infrastructure.Repository.Interfaces
{  
     public interface IUserRepository {
        Task<User?> GetUserByIdAsync(string userId);
    }
}