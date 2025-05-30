using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using auth.Infrastructure.Repository.Interfaces;
using auth.Db;


namespace auth.Infrastructure.Repository
{
    public class UserRepository : IUserRepository
    {
        private readonly AuthContext _context;

        public UserRepository(AuthContext context)
        {
            _context = context;
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId);
        }
    }
}