using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BCrypt.Net;
using LmsPlatform.Data;
using LmsPlatform.DTOs.Auth;
using LmsPlatform.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace LmsPlatform.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
        {
            var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

            // Check email uniqueness
            var existingUser = await _context.Users
                .AnyAsync(u => u.Email.ToLower() == normalizedEmail);

            if (existingUser)
            {
                throw new InvalidOperationException($"User with email '{dto.Email}' already exists.");
            }

            // Hash password
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = normalizedEmail,
                FullName = dto.FullName.Trim(),
                PasswordHash = passwordHash,
                Role = dto.Role,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Users.AddAsync(user);

            // Automatically send initial welcome/registration notification
            var notification = new Notification
            {
                UserId = user.Id,
                Message = $"Добро пожаловать, {user.FullName}! Ваша учетная запись успешно создана и добавлена в систему.",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            await _context.Notifications.AddAsync(notification);

            await _context.SaveChangesAsync();

            var token = GenerateJwtToken(user);
            var expiryDays = int.TryParse(_configuration["Jwt:ExpiryDays"], out var exp) ? exp : 7;

            return new AuthResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.ToString(),
                ExpiresAt = DateTime.UtcNow.AddDays(expiryDays)
            };
        }

        public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
        {
            var normalizedEmail = dto.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail);

            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                throw new UnauthorizedAccessException("Invalid email or password.");
            }

            var token = GenerateJwtToken(user);
            var expiryDays = int.TryParse(_configuration["Jwt:ExpiryDays"], out var exp) ? exp : 7;

            return new AuthResponseDto
            {
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role.ToString(),
                ExpiresAt = DateTime.UtcNow.AddDays(expiryDays)
            };
        }

        public async Task<User?> GetUserByIdAsync(Guid userId)
        {
            return await _context.Users.FindAsync(userId);
        }

        public string GenerateJwtToken(User user)
        {
            var secretKey = _configuration["Jwt:SigningKey"] 
                ?? _configuration["Jwt:SecretKey"] 
                ?? "SuperSecretKeyForLmsPlatformMustBeLongEnough123456!";
            
            var issuer = _configuration["Jwt:Issuer"] ?? "LmsPlatform";
            var audience = _configuration["Jwt:Audience"] ?? "LmsPlatformUsers";
            var expiryDays = int.TryParse(_configuration["Jwt:ExpiryDays"], out var exp) ? exp : 7;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.FullName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim("sub", user.Id.ToString()),
                new Claim("role", user.Role.ToString())
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddDays(expiryDays),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
