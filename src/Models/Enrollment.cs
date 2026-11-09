namespace LmsPlatform.Models
{
    public class Enrollment
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }
        public User? User { get; set; }

        public Guid CourseId { get; set; }
        public Course? Course { get; set; }

        public double ProgressPercentage { get; set; } = 0.0;

        public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
    }
}
