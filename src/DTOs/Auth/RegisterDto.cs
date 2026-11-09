using System.ComponentModel.DataAnnotations;
using LmsPlatform.Models;

namespace LmsPlatform.DTOs.Auth
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "FIO (FullName) is required")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters long")]
        public string Password { get; set; } = string.Empty;

        public UserRole Role { get; set; } = UserRole.Student;
    }
}
