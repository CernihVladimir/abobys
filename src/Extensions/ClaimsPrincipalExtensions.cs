using System.Security.Claims;
using LmsPlatform.Models;

namespace LmsPlatform.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static Guid GetUserId(this ClaimsPrincipal user)
        {
            var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst("UserId")?.Value;

            if (Guid.TryParse(userIdClaim, out var userId))
            {
                return userId;
            }

            throw new InvalidOperationException("User ID is missing or invalid in user claims.");
        }

        public static string GetUserEmail(this ClaimsPrincipal user)
        {
            return user.FindFirst(ClaimTypes.Email)?.Value
                ?? user.FindFirst("email")?.Value
                ?? string.Empty;
        }

        public static UserRole GetUserRole(this ClaimsPrincipal user)
        {
            var roleClaim = user.FindFirst(ClaimTypes.Role)?.Value
                ?? user.FindFirst("role")?.Value;

            if (Enum.TryParse<UserRole>(roleClaim, true, out var role))
            {
                return role;
            }

            throw new InvalidOperationException("User role is missing or invalid in user claims.");
        }
    }
}
