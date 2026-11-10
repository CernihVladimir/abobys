using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace LmsPlatform;

public enum UserRole
{
    Admin,
    Teacher,
    Student
}

public enum CourseStatus
{
    Draft,
    PendingReview,
    Published,
    Rejected
}

public enum AssignmentStatus
{
    NotSolved,
    Solved
}

public class User
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public UserRole Role { get; set; } = UserRole.Student;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Course
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string TheoryContent { get; set; } = string.Empty;

    public List<string> VideoUrls { get; set; } = new();

    public List<string> PresentationUrls { get; set; } = new();

    public List<Assignment> Assignments { get; set; } = new();

    [BsonRepresentation(BsonType.String)]
    public Guid TeacherId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public CourseStatus Status { get; set; } = CourseStatus.Draft;

    public string? RejectionComment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Assignment
{
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime? Deadline { get; set; }

    public int MaxPoints { get; set; } = 100;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Submission
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [BsonRepresentation(BsonType.String)]
    public Guid AssignmentId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid CourseId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid StudentId { get; set; }

    public string Content { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public AssignmentStatus Status { get; set; } = AssignmentStatus.NotSolved;

    public string? Comment { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CheckedAt { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid? CheckedBy { get; set; }
}

public class Enrollment
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid CourseId { get; set; }

    public double ProgressPercentage { get; set; }

    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Notification
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    public string Message { get; set; } = string.Empty;

    public string Type { get; set; } = "info";

    public string? Link { get; set; }

    public DateTime? Deadline { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Role
{
    [BsonId]
    [BsonRepresentation(BsonType.String)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> Permissions { get; set; } = new();

    public bool IsSystem { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class LoginRequest
{
    public string? Email { get; set; }

    public string? Password { get; set; }
}

public class RegisterRequest
{
    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public UserRole Role { get; set; } = UserRole.Student;
}

public class CourseRequest
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? TheoryContent { get; set; }

    public List<string>? VideoUrls { get; set; }

    public List<string>? PresentationUrls { get; set; }

    public Guid? TeacherId { get; set; }
}

public class AssignmentRequest
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    public DateTime? Deadline { get; set; }

    public int? MaxPoints { get; set; }
}

public class SubmitRequest
{
    public string? Content { get; set; }
}

public class ReviewRequest
{
    public AssignmentStatus Status { get; set; } = AssignmentStatus.Solved;

    public string? Comment { get; set; }
}

public class RejectRequest
{
    public string? Comment { get; set; }
}

public class RoleRequest
{
    public UserRole Role { get; set; } = UserRole.Student;
}

public class EnrollRequest
{
    public Guid CourseId { get; set; }

    public double? ProgressPercentage { get; set; }
}

public class ProgressRequest
{
    public double ProgressPercentage { get; set; }
}

public class CreateUserRequest
{
    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public UserRole Role { get; set; } = UserRole.Student;
}

public class UpdateUserRequest
{
    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }
}
