using LmsPlatform.DTOs.Auth;
using LmsPlatform.Models;

namespace LmsPlatform.Services
{
    public interface IAuthService
    {
        Task<AuthResponseDto> RegisterAsync(RegisterDto dto);
        Task<AuthResponseDto> LoginAsync(LoginDto dto);
        Task<User?> GetUserByIdAsync(Guid userId);
        string GenerateJwtToken(User user);
    }
}
