using System.Threading.Tasks;

namespace auth.Services
{
    public interface IJwtKeyRotationService
    {
        Task RotateKeysIfNeededAsync();
    }
}