using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace LmsPlatform;

public class Db
{
    static Db()
    {
        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreIfNullConvention(true)
        };

        ConventionRegistry.Register("LmsPlatform", pack, _ => true);
    }

    public Db(IConfiguration config)
    {
        var client = new MongoClient(config["MongoDb:ConnectionString"] ?? "mongodb://localhost:27017");
        Database = client.GetDatabase(config["MongoDb:DatabaseName"] ?? "LmsPlatform");
    }

    public IMongoDatabase Database { get; }

    public IMongoCollection<User> Users => Database.GetCollection<User>("users");

    public IMongoCollection<Course> Courses => Database.GetCollection<Course>("courses");

    public IMongoCollection<Enrollment> Enrollments => Database.GetCollection<Enrollment>("enrollments");

    public IMongoCollection<Submission> Submissions => Database.GetCollection<Submission>("submissions");

    public IMongoCollection<Notification> Notifications => Database.GetCollection<Notification>("notifications");

    public IMongoCollection<Role> Roles => Database.GetCollection<Role>("roles");

    public async Task InitAsync(IConfiguration config, ILogger logger)
    {
        await Users.Indexes.CreateOneAsync(new CreateIndexModel<User>(
            Builders<User>.IndexKeys.Ascending(u => u.Email),
            new CreateIndexOptions { Unique = true, Name = "ux_users_email" }));

        await Enrollments.Indexes.CreateOneAsync(new CreateIndexModel<Enrollment>(
            Builders<Enrollment>.IndexKeys.Ascending(e => e.UserId).Ascending(e => e.CourseId),
            new CreateIndexOptions { Unique = true, Name = "ux_enrollments_user_course" }));

        await Submissions.Indexes.CreateOneAsync(new CreateIndexModel<Submission>(
            Builders<Submission>.IndexKeys.Ascending(s => s.AssignmentId).Ascending(s => s.StudentId),
            new CreateIndexOptions { Unique = true, Name = "ux_submissions_assignment_student" }));

        await SeedAsync(config, logger);
    }

    private async Task SeedAsync(IConfiguration config, ILogger logger)
    {
        var roles = new (string Name, string Description, string[] Permissions)[]
        {
            ("Admin", "Управление пользователями, ролями и курсами.", new[] { "users.manage", "courses.moderate" }),
            ("Teacher", "Создание курсов и проверка решений.", new[] { "courses.manage", "submissions.review" }),
            ("Student", "Запись на курсы и выполнение заданий.", new[] { "courses.view", "assignments.submit" })
        };

        foreach (var role in roles)
        {
            if (!await Roles.Find(r => r.Name == role.Name).AnyAsync())
            {
                await Roles.InsertOneAsync(new Role
                {
                    Name = role.Name,
                    Description = role.Description,
                    Permissions = role.Permissions.ToList()
                });
            }
        }

        var adminEmail = config["Seed:AdminEmail"] ?? "admin@lms.local";
        var adminPassword = config["Seed:AdminPassword"] ?? "Admin123!";
        await EnsureUserAsync(adminEmail, adminPassword, "Администратор системы", UserRole.Admin, logger);

        var demo = !bool.TryParse(config["Seed:DemoData"], out var enabled) || enabled;
        if (!demo)
        {
            return;
        }

        var teacher = await EnsureUserAsync("teacher@lms.local", "Teacher123!", "Иванов Иван Иванович", UserRole.Teacher, logger);
        var student = await EnsureUserAsync("student@lms.local", "Student123!", "Петров Пётр Петрович", UserRole.Student, logger);

        if (await Courses.CountDocumentsAsync(_ => true) > 0)
        {
            return;
        }

        var course = new Course
        {
            Title = "Основы C# и ASP.NET Core",
            Description = "Базовый курс по языку C#, ООП и созданию веб-API на ASP.NET Core.",
            TheoryContent =
                "C# — современный объектно-ориентированный язык платформы .NET.\n\n" +
                "Темы курса:\n1. Синтаксис и типы данных\n2. ООП: классы и интерфейсы\n" +
                "3. Асинхронность (async/await)\n4. ASP.NET Core Web API\n5. Работа с базами данных",
            VideoUrls = new List<string>
            {
                "https://learn.microsoft.com/ru-ru/dotnet/csharp/"
            },
            PresentationUrls = new List<string>
            {
                "https://learn.microsoft.com/ru-ru/dotnet/csharp/tour-of-csharp/"
            },
            Assignments = new List<Assignment>
            {
                new()
                {
                    Title = "Задание 1. Переменные и типы данных",
                    Description = "Напишите консольную программу, которая спрашивает имя и возраст и выводит приветствие.",
                    Deadline = DateTime.UtcNow.AddDays(14),
                    MaxPoints = 100
                },
                new()
                {
                    Title = "Задание 2. Мини-API на ASP.NET Core",
                    Description = "Создайте Web API с CRUD-операциями для сущности «Книга».",
                    Deadline = DateTime.UtcNow.AddDays(28),
                    MaxPoints = 100
                }
            },
            TeacherId = teacher.Id,
            Status = CourseStatus.Published
        };

        await Courses.InsertOneAsync(course);

        await Enrollments.InsertOneAsync(new Enrollment
        {
            UserId = student.Id,
            CourseId = course.Id,
            ProgressPercentage = 0
        });

        await Api.Notify(this, student.Id, $"Вы записаны на курс «{course.Title}».", "course", link: $"/course.html?id={course.Id}");

        logger.LogInformation("Demo course created.");
    }

    private async Task<User> EnsureUserAsync(string email, string password, string fullName, UserRole role, ILogger logger)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = await Users.Find(u => u.Email == normalized).FirstOrDefaultAsync();
        if (user is not null)
        {
            return user;
        }

        user = new User
        {
            Email = normalized,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FullName = fullName,
            Role = role
        };

        await Users.InsertOneAsync(user);
        await Api.Notify(this, user.Id, $"Добро пожаловать, {user.FullName}! Аккаунт создан.", "account");
        logger.LogInformation("Seeded user {Email}.", normalized);
        return user;
    }
}

public static class Api
{
    public static void MapAuth(RouteGroupBuilder api)
    {
        api.MapPost("/auth/register", async (RegisterRequest req, Db db, IConfiguration config) =>
        {
            var error = ValidateUser(req.FullName, req.Email, req.Password);
            if (error is not null)
            {
                return Results.BadRequest(new { message = error });
            }

            if (req.Role == UserRole.Admin)
            {
                return Results.BadRequest(new { message = "Регистрация администратора недоступна." });
            }

            var email = req.Email!.Trim().ToLowerInvariant();
            if (await db.Users.Find(u => u.Email == email).AnyAsync())
            {
                return Results.BadRequest(new { message = "Пользователь с таким email уже существует." });
            }

            var user = new User
            {
                Email = email,
                FullName = req.FullName!.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password!),
                Role = req.Role
            };

            await db.Users.InsertOneAsync(user);
            await Notify(db, user.Id, $"Добро пожаловать, {user.FullName}! Аккаунт создан.", "account");

            return Results.Ok(AuthResponse(user, config));
        });

        api.MapPost("/auth/login", async (LoginRequest req, Db db, IConfiguration config) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
            {
                return Results.BadRequest(new { message = "Введите email и пароль." });
            }

            var email = req.Email.Trim().ToLowerInvariant();
            var user = await db.Users.Find(u => u.Email == email).FirstOrDefaultAsync();

            if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            {
                return Results.Json(new { message = "Неверный email или пароль." }, statusCode: 401);
            }

            return Results.Ok(AuthResponse(user, config));
        });

        api.MapGet("/auth/me", async (ClaimsPrincipal user, Db db) =>
        {
            var current = await db.Users.Find(u => u.Id == UserId(user)).FirstOrDefaultAsync();
            return current is null
                ? Results.NotFound(new { message = "Пользователь не найден." })
                : Results.Ok(UserView(current));
        }).RequireAuthorization();
    }

    public static void MapNotifications(RouteGroupBuilder api)
    {
        api.MapGet("/notifications", async (ClaimsPrincipal user, Db db) =>
        {
            var list = await db.Notifications
                .Find(n => n.UserId == UserId(user))
                .SortByDescending(n => n.CreatedAt)
                .ToListAsync();

            return Results.Ok(list);
        }).RequireAuthorization();

        api.MapGet("/notifications/unread-count", async (ClaimsPrincipal user, Db db) =>
        {
            var count = await db.Notifications.CountDocumentsAsync(n => n.UserId == UserId(user) && !n.IsRead);
            return Results.Ok(new { count });
        }).RequireAuthorization();

        api.MapPut("/notifications/{id:guid}/read", async (Guid id, ClaimsPrincipal user, Db db) =>
        {
            var result = await db.Notifications.UpdateOneAsync(
                n => n.Id == id && n.UserId == UserId(user),
                Builders<Notification>.Update.Set(n => n.IsRead, true));

            return result.MatchedCount == 0
                ? Results.NotFound(new { message = "Уведомление не найдено." })
                : Results.Ok(new { id, isRead = true });
        }).RequireAuthorization();

        api.MapPut("/notifications/read-all", async (ClaimsPrincipal user, Db db) =>
        {
            await db.Notifications.UpdateManyAsync(
                n => n.UserId == UserId(user) && !n.IsRead,
                Builders<Notification>.Update.Set(n => n.IsRead, true));

            return Results.NoContent();
        }).RequireAuthorization();
    }

    public static void MapCourses(RouteGroupBuilder api)
    {
        Action<AuthorizationPolicyBuilder> teacherOrAdmin = p => p.RequireRole("Teacher", "Admin");

        api.MapGet("/courses", async (CourseStatus? status, string? search, bool? mine, ClaimsPrincipal user, Db db) =>
        {
            var role = UserRoleOf(user);
            var userId = UserId(user);
            var filter = Builders<Course>.Filter.Empty;

            if (mine == true)
            {
                if (role == UserRole.Student)
                {
                    var ids = await db.Enrollments.Find(e => e.UserId == userId).Project(e => e.CourseId).ToListAsync();
                    filter &= Builders<Course>.Filter.In(c => c.Id, ids);
                    filter &= Builders<Course>.Filter.Eq(c => c.Status, CourseStatus.Published);
                }
                else
                {
                    filter &= Builders<Course>.Filter.Eq(c => c.TeacherId, userId);
                }
            }
            else if (role == UserRole.Student)
            {
                filter &= Builders<Course>.Filter.Eq(c => c.Status, CourseStatus.Published);
            }
            else if (role == UserRole.Teacher)
            {
                filter &= Builders<Course>.Filter.Or(
                    Builders<Course>.Filter.Eq(c => c.Status, CourseStatus.Published),
                    Builders<Course>.Filter.Eq(c => c.TeacherId, userId));
            }

            if (status.HasValue)
            {
                if (role == UserRole.Student && status.Value != CourseStatus.Published)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                if (role == UserRole.Teacher && status.Value != CourseStatus.Published)
                {
                    filter &= Builders<Course>.Filter.Eq(c => c.TeacherId, userId);
                }

                filter &= Builders<Course>.Filter.Eq(c => c.Status, status.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var regex = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
                filter &= Builders<Course>.Filter.Or(
                    Builders<Course>.Filter.Regex(c => c.Title, regex),
                    Builders<Course>.Filter.Regex(c => c.Description, regex));
            }

            var courses = await db.Courses.Find(filter).SortByDescending(c => c.CreatedAt).ToListAsync();
            return Results.Ok(await CourseSummaries(db, courses, role == UserRole.Student ? userId : null));
        }).RequireAuthorization();

        api.MapGet("/courses/{id:guid}", async (Guid id, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            var role = UserRoleOf(user);
            var userId = UserId(user);

            if (role == UserRole.Student && course.Status != CourseStatus.Published)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (role == UserRole.Teacher && course.Status != CourseStatus.Published && course.TeacherId != userId)
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            return Results.Ok(await CourseDetail(db, course, user));
        }).RequireAuthorization();

        api.MapPost("/courses", async (CourseRequest req, ClaimsPrincipal user, Db db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title))
            {
                return Results.BadRequest(new { message = "Введите название курса." });
            }

            var role = UserRoleOf(user);
            var teacherId = UserId(user);

            if (role == UserRole.Admin && req.TeacherId.HasValue)
            {
                var teacher = await db.Users.Find(u => u.Id == req.TeacherId.Value).FirstOrDefaultAsync();
                if (teacher is null || teacher.Role != UserRole.Teacher)
                {
                    return Results.BadRequest(new { message = "Преподаватель не найден." });
                }

                teacherId = teacher.Id;
            }

            var course = new Course
            {
                Title = req.Title.Trim(),
                Description = req.Description?.Trim() ?? string.Empty,
                TheoryContent = req.TheoryContent ?? string.Empty,
                VideoUrls = Links(req.VideoUrls),
                PresentationUrls = Links(req.PresentationUrls),
                TeacherId = teacherId
            };

            await db.Courses.InsertOneAsync(course);
            return Results.Created($"/api/courses/{course.Id}", new { course.Id });
        }).RequireAuthorization(teacherOrAdmin);

        api.MapPut("/courses/{id:guid}", async (Guid id, CourseRequest req, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (!CanManage(course, user))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (req.Title is not null)
            {
                if (string.IsNullOrWhiteSpace(req.Title))
                {
                    return Results.BadRequest(new { message = "Название курса не может быть пустым." });
                }

                course.Title = req.Title.Trim();
            }

            if (req.Description is not null) course.Description = req.Description.Trim();
            if (req.TheoryContent is not null) course.TheoryContent = req.TheoryContent;
            if (req.VideoUrls is not null) course.VideoUrls = Links(req.VideoUrls);
            if (req.PresentationUrls is not null) course.PresentationUrls = Links(req.PresentationUrls);

            course.UpdatedAt = DateTime.UtcNow;
            await db.Courses.ReplaceOneAsync(c => c.Id == id, course);

            return Results.Ok(new { course.Id });
        }).RequireAuthorization(teacherOrAdmin);

        api.MapDelete("/courses/{id:guid}", async (Guid id, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (!CanManage(course, user))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            await db.Courses.DeleteOneAsync(c => c.Id == id);
            await db.Enrollments.DeleteManyAsync(e => e.CourseId == id);
            await db.Submissions.DeleteManyAsync(s => s.CourseId == id);

            return Results.NoContent();
        }).RequireAuthorization(teacherOrAdmin);

        api.MapPost("/courses/{id:guid}/submit", async (Guid id, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (!CanManage(course, user))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (course.Status == CourseStatus.Published)
            {
                return Results.BadRequest(new { message = "Курс уже опубликован." });
            }

            course.Status = CourseStatus.PendingReview;
            course.RejectionComment = null;
            course.UpdatedAt = DateTime.UtcNow;
            await db.Courses.ReplaceOneAsync(c => c.Id == id, course);

            await NotifyRole(db, UserRole.Admin, $"Курс «{course.Title}» отправлен на проверку.", "course", "/admin.html");

            return Results.Ok(new { status = course.Status });
        }).RequireAuthorization(teacherOrAdmin);

        api.MapPost("/courses/{id:guid}/approve", async (Guid id, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            course.Status = CourseStatus.Published;
            course.RejectionComment = null;
            course.UpdatedAt = DateTime.UtcNow;
            await db.Courses.ReplaceOneAsync(c => c.Id == id, course);

            await Notify(db, course.TeacherId, $"Курс «{course.Title}» опубликован.", "course", link: $"/course.html?id={course.Id}");

            return Results.Ok(new { status = course.Status });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapPost("/courses/{id:guid}/reject", async (Guid id, RejectRequest req, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            course.Status = CourseStatus.Rejected;
            course.RejectionComment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
            course.UpdatedAt = DateTime.UtcNow;
            await db.Courses.ReplaceOneAsync(c => c.Id == id, course);

            var comment = course.RejectionComment is null ? string.Empty : $" Причина: {course.RejectionComment}";
            await Notify(db, course.TeacherId, $"Курс «{course.Title}» отклонён.{comment}", "course", link: "/courses.html");

            return Results.Ok(new { status = course.Status });
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapPost("/courses/{id:guid}/enroll", async (Guid id, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null || course.Status != CourseStatus.Published)
            {
                return Results.NotFound(new { message = "Опубликованный курс не найден." });
            }

            var userId = UserId(user);
            var existing = await db.Enrollments.Find(e => e.UserId == userId && e.CourseId == id).FirstOrDefaultAsync();
            if (existing is not null)
            {
                return Results.Ok(existing);
            }

            var enrollment = new Enrollment { UserId = userId, CourseId = id };
            await db.Enrollments.InsertOneAsync(enrollment);
            await Notify(db, userId, $"Вы записаны на курс «{course.Title}».", "course", link: $"/course.html?id={course.Id}");

            return Results.Ok(enrollment);
        }).RequireAuthorization(p => p.RequireRole("Student"));

        api.MapDelete("/courses/{id:guid}/enroll", async (Guid id, ClaimsPrincipal user, Db db) =>
        {
            var userId = UserId(user);
            await db.Enrollments.DeleteOneAsync(e => e.UserId == userId && e.CourseId == id);
            await db.Submissions.DeleteManyAsync(s => s.StudentId == userId && s.CourseId == id);

            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole("Student"));

        api.MapGet("/courses/{id:guid}/students", async (Guid id, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (!CanManage(course, user))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var enrollments = await db.Enrollments.Find(e => e.CourseId == id).ToListAsync();
            var userIds = enrollments.Select(e => e.UserId).ToList();
            var users = await db.Users.Find(u => userIds.Contains(u.Id)).ToListAsync();
            var userMap = users.ToDictionary(u => u.Id);

            var result = enrollments.Select(e =>
            {
                var student = userMap.GetValueOrDefault(e.UserId);
                return new
                {
                    e.UserId,
                    FullName = student?.FullName ?? "Неизвестный студент",
                    Email = student?.Email ?? string.Empty,
                    e.ProgressPercentage,
                    e.EnrolledAt
                };
            }).OrderByDescending(s => s.ProgressPercentage).ToList();

            return Results.Ok(result);
        }).RequireAuthorization(teacherOrAdmin);

        api.MapPost("/courses/{id:guid}/assignments", async (Guid id, AssignmentRequest req, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (!CanManage(course, user))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (string.IsNullOrWhiteSpace(req.Title))
            {
                return Results.BadRequest(new { message = "Введите название задания." });
            }

            var maxPoints = req.MaxPoints ?? 100;
            if (maxPoints < 1 || maxPoints > 1000)
            {
                return Results.BadRequest(new { message = "Баллы должны быть от 1 до 1000." });
            }

            var assignment = new Assignment
            {
                Title = req.Title.Trim(),
                Description = req.Description?.Trim() ?? string.Empty,
                Deadline = req.Deadline?.ToUniversalTime(),
                MaxPoints = maxPoints
            };

            course.Assignments.Add(assignment);
            course.UpdatedAt = DateTime.UtcNow;
            await db.Courses.ReplaceOneAsync(c => c.Id == id, course);

            var enrollments = await db.Enrollments.Find(e => e.CourseId == id).ToListAsync();
            foreach (var enrollment in enrollments)
            {
                var deadline = assignment.Deadline.HasValue
                    ? $" Дедлайн: {assignment.Deadline.Value:dd.MM.yyyy HH:mm} UTC."
                    : string.Empty;

                await Notify(db, enrollment.UserId,
                    $"Новое задание в курсе «{course.Title}»: «{assignment.Title}».{deadline}",
                    "assignment", assignment.Deadline, $"/course.html?id={id}");
            }

            await RecalcCourse(db, id);
            return Results.Ok(assignment);
        }).RequireAuthorization(teacherOrAdmin);

        api.MapPut("/courses/{id:guid}/assignments/{assignmentId:guid}", async (
            Guid id, Guid assignmentId, AssignmentRequest req, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (!CanManage(course, user))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var assignment = course.Assignments.FirstOrDefault(a => a.Id == assignmentId);
            if (assignment is null)
            {
                return Results.NotFound(new { message = "Задание не найдено." });
            }

            if (req.Title is not null)
            {
                if (string.IsNullOrWhiteSpace(req.Title))
                {
                    return Results.BadRequest(new { message = "Название задания не может быть пустым." });
                }

                assignment.Title = req.Title.Trim();
            }

            if (req.Description is not null) assignment.Description = req.Description.Trim();
            if (req.Deadline.HasValue) assignment.Deadline = req.Deadline.Value.ToUniversalTime();

            if (req.MaxPoints.HasValue)
            {
                if (req.MaxPoints < 1 || req.MaxPoints > 1000)
                {
                    return Results.BadRequest(new { message = "Баллы должны быть от 1 до 1000." });
                }

                assignment.MaxPoints = req.MaxPoints.Value;
            }

            course.UpdatedAt = DateTime.UtcNow;
            await db.Courses.ReplaceOneAsync(c => c.Id == id, course);

            return Results.Ok(assignment);
        }).RequireAuthorization(teacherOrAdmin);

        api.MapDelete("/courses/{id:guid}/assignments/{assignmentId:guid}", async (
            Guid id, Guid assignmentId, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (!CanManage(course, user))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var assignment = course.Assignments.FirstOrDefault(a => a.Id == assignmentId);
            if (assignment is null)
            {
                return Results.NotFound(new { message = "Задание не найдено." });
            }

            course.Assignments.Remove(assignment);
            course.UpdatedAt = DateTime.UtcNow;
            await db.Courses.ReplaceOneAsync(c => c.Id == id, course);
            await db.Submissions.DeleteManyAsync(s => s.AssignmentId == assignmentId);
            await RecalcCourse(db, id);

            return Results.NoContent();
        }).RequireAuthorization(teacherOrAdmin);

        api.MapPost("/courses/{id:guid}/assignments/{assignmentId:guid}/submit", async (
            Guid id, Guid assignmentId, SubmitRequest req, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null || course.Status != CourseStatus.Published)
            {
                return Results.NotFound(new { message = "Опубликованный курс не найден." });
            }

            var assignment = course.Assignments.FirstOrDefault(a => a.Id == assignmentId);
            if (assignment is null)
            {
                return Results.NotFound(new { message = "Задание не найдено." });
            }

            var studentId = UserId(user);
            var enrollment = await db.Enrollments.Find(e => e.UserId == studentId && e.CourseId == id).FirstOrDefaultAsync();
            if (enrollment is null)
            {
                return Results.BadRequest(new { message = "Сначала запишитесь на курс." });
            }

            if (string.IsNullOrWhiteSpace(req.Content))
            {
                return Results.BadRequest(new { message = "Решение не может быть пустым." });
            }

            var submission = await db.Submissions
                .Find(s => s.AssignmentId == assignmentId && s.StudentId == studentId)
                .FirstOrDefaultAsync();

            if (submission is null)
            {
                submission = new Submission
                {
                    AssignmentId = assignmentId,
                    CourseId = id,
                    StudentId = studentId,
                    Content = req.Content.Trim()
                };

                await db.Submissions.InsertOneAsync(submission);
            }
            else
            {
                submission.Content = req.Content.Trim();
                submission.Status = AssignmentStatus.NotSolved;
                submission.Comment = null;
                submission.SubmittedAt = DateTime.UtcNow;
                submission.CheckedAt = null;
                submission.CheckedBy = null;

                await db.Submissions.ReplaceOneAsync(s => s.Id == submission.Id, submission);
            }

            var student = await db.Users.Find(u => u.Id == studentId).FirstOrDefaultAsync();
            await Notify(db, course.TeacherId,
                $"Студент {student?.FullName ?? "студент"} отправил решение «{assignment.Title}».",
                "assignment", link: "/courses.html");

            await RecalcProgress(db, studentId, id);
            return Results.Ok(SubmissionView(submission, student?.FullName ?? string.Empty));
        }).RequireAuthorization(p => p.RequireRole("Student"));

        api.MapGet("/courses/{id:guid}/assignments/{assignmentId:guid}/submissions", async (
            Guid id, Guid assignmentId, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (!CanManage(course, user))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            if (course.Assignments.All(a => a.Id != assignmentId))
            {
                return Results.NotFound(new { message = "Задание не найдено." });
            }

            var submissions = await db.Submissions
                .Find(s => s.AssignmentId == assignmentId)
                .SortByDescending(s => s.SubmittedAt)
                .ToListAsync();

            var studentIds = submissions.Select(s => s.StudentId).Distinct().ToList();
            var students = await db.Users.Find(u => studentIds.Contains(u.Id)).ToListAsync();
            var studentMap = students.ToDictionary(u => u.Id);

            return Results.Ok(submissions.Select(s =>
                SubmissionView(s, studentMap.GetValueOrDefault(s.StudentId)?.FullName ?? "Неизвестный студент")).ToList());
        }).RequireAuthorization(teacherOrAdmin);

        api.MapPut("/courses/{id:guid}/assignments/{assignmentId:guid}/submissions/{submissionId:guid}", async (
            Guid id, Guid assignmentId, Guid submissionId, ReviewRequest req, ClaimsPrincipal user, Db db) =>
        {
            var course = await db.Courses.Find(c => c.Id == id).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            if (!CanManage(course, user))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var assignment = course.Assignments.FirstOrDefault(a => a.Id == assignmentId);
            if (assignment is null)
            {
                return Results.NotFound(new { message = "Задание не найдено." });
            }

            var submission = await db.Submissions
                .Find(s => s.Id == submissionId && s.AssignmentId == assignmentId)
                .FirstOrDefaultAsync();

            if (submission is null)
            {
                return Results.NotFound(new { message = "Решение не найдено." });
            }

            submission.Status = req.Status;
            submission.Comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
            submission.CheckedAt = DateTime.UtcNow;
            submission.CheckedBy = UserId(user);

            await db.Submissions.ReplaceOneAsync(s => s.Id == submission.Id, submission);
            await RecalcProgress(db, submission.StudentId, id);

            var statusText = req.Status == AssignmentStatus.Solved ? "решено" : "не решено";
            await Notify(db, submission.StudentId,
                $"Задание «{assignment.Title}» проверено: {statusText}.",
                "assignment", link: $"/course.html?id={id}");

            var student = await db.Users.Find(u => u.Id == submission.StudentId).FirstOrDefaultAsync();
            return Results.Ok(SubmissionView(submission, student?.FullName ?? string.Empty));
        }).RequireAuthorization(teacherOrAdmin);
    }

    public static void MapAdmin(RouteGroupBuilder api)
    {
        api.MapGet("/admin/users", async (UserRole? role, string? search, Db db) =>
        {
            var filter = Builders<User>.Filter.Empty;

            if (role.HasValue)
            {
                filter &= Builders<User>.Filter.Eq(u => u.Role, role.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var regex = new BsonRegularExpression(Regex.Escape(search.Trim()), "i");
                filter &= Builders<User>.Filter.Or(
                    Builders<User>.Filter.Regex(u => u.FullName, regex),
                    Builders<User>.Filter.Regex(u => u.Email, regex));
            }

            var users = await db.Users.Find(filter).SortByDescending(u => u.CreatedAt).ToListAsync();
            return Results.Ok(users.Select(UserView).ToList());
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapPost("/admin/users", async (CreateUserRequest req, Db db) =>
        {
            var error = ValidateUser(req.FullName, req.Email, req.Password);
            if (error is not null)
            {
                return Results.BadRequest(new { message = error });
            }

            var email = req.Email!.Trim().ToLowerInvariant();
            if (await db.Users.Find(u => u.Email == email).AnyAsync())
            {
                return Results.BadRequest(new { message = "Пользователь с таким email уже существует." });
            }

            var user = new User
            {
                Email = email,
                FullName = req.FullName!.Trim(),
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password!),
                Role = req.Role
            };

            await db.Users.InsertOneAsync(user);
            await Notify(db, user.Id, $"Добро пожаловать, {user.FullName}! Аккаунт создан.", "account");

            return Results.Created($"/api/admin/users/{user.Id}", UserView(user));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapPut("/admin/users/{id:guid}", async (Guid id, UpdateUserRequest req, Db db) =>
        {
            var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
            if (user is null)
            {
                return Results.NotFound(new { message = "Пользователь не найден." });
            }

            if (!string.IsNullOrWhiteSpace(req.FullName))
            {
                user.FullName = req.FullName.Trim();
            }

            if (!string.IsNullOrWhiteSpace(req.Email))
            {
                var email = req.Email.Trim().ToLowerInvariant();
                if (await db.Users.Find(u => u.Email == email && u.Id != id).AnyAsync())
                {
                    return Results.BadRequest(new { message = "Пользователь с таким email уже существует." });
                }

                user.Email = email;
            }

            if (!string.IsNullOrWhiteSpace(req.Password))
            {
                if (req.Password.Length < 6)
                {
                    return Results.BadRequest(new { message = "Пароль должен содержать минимум 6 символов." });
                }

                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            }

            await db.Users.ReplaceOneAsync(u => u.Id == id, user);
            return Results.Ok(UserView(user));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapPut("/admin/users/{id:guid}/role", async (Guid id, RoleRequest req, Db db) =>
        {
            var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
            if (user is null)
            {
                return Results.NotFound(new { message = "Пользователь не найден." });
            }

            if (user.Role == UserRole.Admin && req.Role != UserRole.Admin)
            {
                var admins = await db.Users.CountDocumentsAsync(u => u.Role == UserRole.Admin);
                if (admins <= 1)
                {
                    return Results.BadRequest(new { message = "Нельзя снять роль с последнего администратора." });
                }
            }

            var oldRole = user.Role;
            user.Role = req.Role;
            await db.Users.ReplaceOneAsync(u => u.Id == id, user);
            await Notify(db, user.Id, $"Ваша роль изменена: {oldRole} → {req.Role}.", "account");

            return Results.Ok(UserView(user));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapDelete("/admin/users/{id:guid}", async (Guid id, ClaimsPrincipal user, Db db) =>
        {
            if (id == UserId(user))
            {
                return Results.BadRequest(new { message = "Нельзя удалить самого себя." });
            }

            var target = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
            if (target is null)
            {
                return Results.NotFound(new { message = "Пользователь не найден." });
            }

            if (target.Role == UserRole.Admin)
            {
                var admins = await db.Users.CountDocumentsAsync(u => u.Role == UserRole.Admin);
                if (admins <= 1)
                {
                    return Results.BadRequest(new { message = "Нельзя удалить последнего администратора." });
                }
            }

            if (await db.Courses.Find(c => c.TeacherId == id).AnyAsync())
            {
                return Results.BadRequest(new { message = "У пользователя есть курсы. Сначала удалите их." });
            }

            await db.Users.DeleteOneAsync(u => u.Id == id);
            await db.Enrollments.DeleteManyAsync(e => e.UserId == id);
            await db.Submissions.DeleteManyAsync(s => s.StudentId == id);
            await db.Notifications.DeleteManyAsync(n => n.UserId == id);

            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapGet("/admin/users/{id:guid}/enrollments", async (Guid id, Db db) =>
        {
            var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
            if (user is null)
            {
                return Results.NotFound(new { message = "Пользователь не найден." });
            }

            var enrollments = await db.Enrollments.Find(e => e.UserId == id).ToListAsync();
            var courseIds = enrollments.Select(e => e.CourseId).ToList();
            var courses = await db.Courses.Find(c => courseIds.Contains(c.Id)).ToListAsync();
            var courseMap = courses.ToDictionary(c => c.Id);

            return Results.Ok(enrollments.Select(e =>
                EnrollmentView(e, user, courseMap.GetValueOrDefault(e.CourseId))).ToList());
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapPost("/admin/users/{id:guid}/enrollments", async (Guid id, EnrollRequest req, Db db) =>
        {
            var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
            if (user is null)
            {
                return Results.NotFound(new { message = "Пользователь не найден." });
            }

            var course = await db.Courses.Find(c => c.Id == req.CourseId).FirstOrDefaultAsync();
            if (course is null)
            {
                return Results.NotFound(new { message = "Курс не найден." });
            }

            var existing = await db.Enrollments.Find(e => e.UserId == id && e.CourseId == req.CourseId).FirstOrDefaultAsync();
            if (existing is not null)
            {
                if (req.ProgressPercentage.HasValue)
                {
                    existing.ProgressPercentage = Math.Clamp(req.ProgressPercentage.Value, 0, 100);
                    existing.UpdatedAt = DateTime.UtcNow;
                    await db.Enrollments.ReplaceOneAsync(e => e.Id == existing.Id, existing);
                }

                return Results.Ok(EnrollmentView(existing, user, course));
            }

            var enrollment = new Enrollment
            {
                UserId = id,
                CourseId = req.CourseId,
                ProgressPercentage = Math.Clamp(req.ProgressPercentage ?? 0, 0, 100)
            };

            await db.Enrollments.InsertOneAsync(enrollment);
            await Notify(db, id, $"Вы записаны на курс «{course.Title}».", "course", link: $"/course.html?id={course.Id}");

            return Results.Ok(EnrollmentView(enrollment, user, course));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapPut("/admin/users/{id:guid}/enrollments/{courseId:guid}", async (
            Guid id, Guid courseId, ProgressRequest req, Db db) =>
        {
            var enrollment = await db.Enrollments.Find(e => e.UserId == id && e.CourseId == courseId).FirstOrDefaultAsync();
            if (enrollment is null)
            {
                return Results.NotFound(new { message = "Зачисление не найдено." });
            }

            enrollment.ProgressPercentage = Math.Clamp(req.ProgressPercentage, 0, 100);
            enrollment.UpdatedAt = DateTime.UtcNow;
            await db.Enrollments.ReplaceOneAsync(e => e.Id == enrollment.Id, enrollment);

            var user = await db.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
            var course = await db.Courses.Find(c => c.Id == courseId).FirstOrDefaultAsync();

            return Results.Ok(EnrollmentView(enrollment, user, course));
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapDelete("/admin/users/{id:guid}/enrollments/{courseId:guid}", async (Guid id, Guid courseId, Db db) =>
        {
            await db.Enrollments.DeleteOneAsync(e => e.UserId == id && e.CourseId == courseId);
            await db.Submissions.DeleteManyAsync(s => s.StudentId == id && s.CourseId == courseId);

            return Results.NoContent();
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapGet("/admin/roles", async (Db db) =>
        {
            var roles = await db.Roles.Find(_ => true).SortBy(r => r.Name).ToListAsync();
            return Results.Ok(roles);
        }).RequireAuthorization(p => p.RequireRole("Admin"));

        api.MapGet("/admin/stats", async (Db db) =>
        {
            return Results.Ok(new
            {
                totalUsers = await db.Users.CountDocumentsAsync(_ => true),
                students = await db.Users.CountDocumentsAsync(u => u.Role == UserRole.Student),
                teachers = await db.Users.CountDocumentsAsync(u => u.Role == UserRole.Teacher),
                admins = await db.Users.CountDocumentsAsync(u => u.Role == UserRole.Admin),
                courses = await db.Courses.CountDocumentsAsync(_ => true),
                publishedCourses = await db.Courses.CountDocumentsAsync(c => c.Status == CourseStatus.Published),
                pendingCourses = await db.Courses.CountDocumentsAsync(c => c.Status == CourseStatus.PendingReview),
                enrollments = await db.Enrollments.CountDocumentsAsync(_ => true),
                submissions = await db.Submissions.CountDocumentsAsync(_ => true)
            });
        }).RequireAuthorization(p => p.RequireRole("Admin"));
    }

    private static string? ValidateUser(string? fullName, string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return "Заполните ФИО, email и пароль.";
        }

        if (!email.Contains('@') || !email.Contains('.'))
        {
            return "Некорректный email.";
        }

        if (password.Length < 6)
        {
            return "Пароль должен содержать минимум 6 символов.";
        }

        return null;
    }

    private static object AuthResponse(User user, IConfiguration config)
    {
        var days = config.GetValue("Jwt:ExpiryDays", 7);

        return new
        {
            token = CreateToken(user, config),
            userId = user.Id,
            email = user.Email,
            fullName = user.FullName,
            role = user.Role.ToString(),
            expiresAt = DateTime.UtcNow.AddDays(days)
        };
    }

    private static string CreateToken(User user, IConfiguration config)
    {
        var key = config["Jwt:SigningKey"] ?? "SuperSecretJwtSigningKeyForLmsPlatformProject2026!";
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddDays(config.GetValue("Jwt:ExpiryDays", 7)),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Guid UserId(ClaimsPrincipal user)
    {
        return Guid.Parse(user.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    }

    private static UserRole UserRoleOf(ClaimsPrincipal user)
    {
        return Enum.Parse<UserRole>(user.FindFirst(ClaimTypes.Role)!.Value);
    }

    private static bool CanManage(Course course, ClaimsPrincipal user)
    {
        return UserRoleOf(user) == UserRole.Admin || course.TeacherId == UserId(user);
    }

    private static List<string> Links(IEnumerable<string>? values)
    {
        return values?
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct()
            .ToList() ?? new List<string>();
    }

    private static object UserView(User user)
    {
        return new
        {
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            user.CreatedAt
        };
    }

    private static object EnrollmentView(Enrollment enrollment, User? user, Course? course)
    {
        return new
        {
            enrollment.Id,
            enrollment.UserId,
            StudentName = user?.FullName ?? "Неизвестный пользователь",
            Email = user?.Email ?? string.Empty,
            enrollment.CourseId,
            CourseTitle = course?.Title ?? "Неизвестный курс",
            enrollment.ProgressPercentage,
            enrollment.EnrolledAt,
            enrollment.UpdatedAt
        };
    }

    private static object SubmissionView(Submission submission, string studentName)
    {
        return new
        {
            submission.Id,
            submission.AssignmentId,
            submission.CourseId,
            submission.StudentId,
            StudentName = studentName,
            submission.Content,
            submission.Status,
            submission.Comment,
            submission.SubmittedAt,
            submission.CheckedAt
        };
    }

    private static async Task<List<object>> CourseSummaries(Db db, List<Course> courses, Guid? studentId)
    {
        if (courses.Count == 0)
        {
            return new List<object>();
        }

        var teacherIds = courses.Select(c => c.TeacherId).Distinct().ToList();
        var teachers = await db.Users.Find(u => teacherIds.Contains(u.Id)).ToListAsync();
        var teacherMap = teachers.ToDictionary(u => u.Id, u => u.FullName);

        var courseIds = courses.Select(c => c.Id).ToList();
        var enrollments = await db.Enrollments.Find(e => courseIds.Contains(e.CourseId)).ToListAsync();
        var counts = enrollments.GroupBy(e => e.CourseId).ToDictionary(g => g.Key, g => g.Count());
        var progress = studentId.HasValue
            ? enrollments.Where(e => e.UserId == studentId).ToDictionary(e => e.CourseId, e => e.ProgressPercentage)
            : new Dictionary<Guid, double>();

        double? Progress(Guid courseId)
        {
            return studentId.HasValue && progress.TryGetValue(courseId, out var value) ? value : null;
        }

        return courses.Select(c => (object)new
        {
            c.Id,
            c.Title,
            c.Description,
            c.TeacherId,
            TeacherName = teacherMap.GetValueOrDefault(c.TeacherId, "Неизвестный преподаватель"),
            c.Status,
            AssignmentsCount = c.Assignments.Count,
            EnrolledCount = counts.GetValueOrDefault(c.Id),
            ProgressPercentage = Progress(c.Id),
            c.CreatedAt,
            c.UpdatedAt
        }).ToList();
    }

    private static async Task<object> CourseDetail(Db db, Course course, ClaimsPrincipal user)
    {
        var role = UserRoleOf(user);
        var userId = UserId(user);
        var teacher = await db.Users.Find(u => u.Id == course.TeacherId).FirstOrDefaultAsync();
        var enrolled = (int)await db.Enrollments.CountDocumentsAsync(e => e.CourseId == course.Id);

        var submissions = new List<Submission>();
        double? myProgress = null;
        var studentName = string.Empty;

        if (role == UserRole.Student)
        {
            var enrollment = await db.Enrollments.Find(e => e.UserId == userId && e.CourseId == course.Id).FirstOrDefaultAsync();
            myProgress = enrollment?.ProgressPercentage;

            submissions = await db.Submissions.Find(s => s.StudentId == userId && s.CourseId == course.Id).ToListAsync();
            var student = await db.Users.Find(u => u.Id == userId).FirstOrDefaultAsync();
            studentName = student?.FullName ?? string.Empty;
        }

        var submissionMap = submissions.ToDictionary(s => s.AssignmentId);

        return new
        {
            course.Id,
            course.Title,
            course.Description,
            course.TheoryContent,
            course.VideoUrls,
            course.PresentationUrls,
            course.TeacherId,
            TeacherName = teacher?.FullName ?? "Неизвестный преподаватель",
            course.Status,
            course.RejectionComment,
            AssignmentsCount = course.Assignments.Count,
            EnrolledCount = enrolled,
            Assignments = course.Assignments.Select(a => new
            {
                a.Id,
                a.Title,
                a.Description,
                a.Deadline,
                a.MaxPoints,
                a.CreatedAt,
                MySubmission = submissionMap.TryGetValue(a.Id, out var submission)
                    ? SubmissionView(submission, studentName)
                    : null
            }).ToList(),
            MySubmissions = submissions.Select(s => SubmissionView(s, studentName)).ToList(),
            MyProgress = myProgress,
            course.CreatedAt,
            course.UpdatedAt
        };
    }

    private static async Task<double> RecalcProgress(Db db, Guid studentId, Guid courseId)
    {
        var course = await db.Courses.Find(c => c.Id == courseId).FirstOrDefaultAsync();
        var enrollment = await db.Enrollments.Find(e => e.UserId == studentId && e.CourseId == courseId).FirstOrDefaultAsync();

        if (course is null || enrollment is null)
        {
            return 0;
        }

        var assignmentIds = course.Assignments.Select(a => a.Id).ToList();
        var solved = assignmentIds.Count == 0
            ? 0
            : (int)await db.Submissions.CountDocumentsAsync(s =>
                s.StudentId == studentId &&
                s.CourseId == courseId &&
                s.Status == AssignmentStatus.Solved &&
                assignmentIds.Contains(s.AssignmentId));

        var percentage = assignmentIds.Count == 0
            ? 0
            : Math.Round((double)solved / assignmentIds.Count * 100, 2);

        await db.Enrollments.UpdateOneAsync(
            e => e.Id == enrollment.Id,
            Builders<Enrollment>.Update
                .Set(e => e.ProgressPercentage, percentage)
                .Set(e => e.UpdatedAt, DateTime.UtcNow));

        return percentage;
    }

    private static async Task RecalcCourse(Db db, Guid courseId)
    {
        var enrollments = await db.Enrollments.Find(e => e.CourseId == courseId).ToListAsync();
        foreach (var enrollment in enrollments)
        {
            await RecalcProgress(db, enrollment.UserId, courseId);
        }
    }

    public static Task Notify(Db db, Guid userId, string message, string type = "info", DateTime? deadline = null, string? link = null)
    {
        return db.Notifications.InsertOneAsync(new Notification
        {
            UserId = userId,
            Message = message,
            Type = type,
            Deadline = deadline,
            Link = link
        });
    }

    private static async Task NotifyRole(Db db, UserRole role, string message, string type = "info", string? link = null)
    {
        var users = await db.Users.Find(u => u.Role == role).ToListAsync();
        if (users.Count == 0)
        {
            return;
        }

        await db.Notifications.InsertManyAsync(users.Select(u => new Notification
        {
            UserId = u.Id,
            Message = message,
            Type = type,
            Link = link
        }));
    }
}
