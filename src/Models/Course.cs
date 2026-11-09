using System.ComponentModel.DataAnnotations;

namespace LmsPlatform.Models
{
    public class Course
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string Title { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string TheoryContent { get; set; } = string.Empty;

        public string VideoUrls { get; set; } = string.Empty; // Store as JSON array or semicolon separated

        public string PresentationUrls { get; set; } = string.Empty;

        public Guid TeacherId { get; set; }
        public User? Teacher { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();
    }
}
