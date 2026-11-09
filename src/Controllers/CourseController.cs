using LmsPlatform.Data;
using LmsPlatform.Extensions;
using LmsPlatform.Middleware;
using LmsPlatform.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LmsPlatform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CourseController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CourseController(AppDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get all courses (Accessible by any authenticated user)
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetCourses()
        {
            var courses = await _context.Courses
                .Include(c => c.Teacher)
                .Select(c => new
                {
                    c.Id,
                    c.Title,
                    c.Description,
                    TeacherName = c.Teacher != null ? c.Teacher.FullName : "Unknown",
                    c.CreatedAt
                })
                .ToListAsync();

            return Ok(courses);
        }

        /// <summary>
        /// Create a course (Teacher or Admin)
        /// </summary>
        [HttpPost]
        [AuthorizeRole(UserRole.Teacher, UserRole.Admin)]
        public async Task<IActionResult> CreateCourse([FromBody] Course course)
        {
            var teacherId = User.GetUserId();
            course.TeacherId = teacherId;
            course.CreatedAt = DateTime.UtcNow;

            await _context.Courses.AddAsync(course);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCourses), new { id = course.Id }, course);
        }
    }
}
