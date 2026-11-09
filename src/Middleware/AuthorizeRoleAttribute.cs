using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using LmsPlatform.Models;
using LmsPlatform.Extensions;

namespace LmsPlatform.Middleware
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class AuthorizeRoleAttribute : Attribute, IAuthorizationFilter
    {
        private readonly UserRole[] _roles;

        public AuthorizeRoleAttribute(params UserRole[] roles)
        {
            _roles = roles;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            if (user?.Identity == null || !user.Identity.IsAuthenticated)
            {
                context.Result = new JsonResult(new
                {
                    statusCode = 401,
                    message = "Unauthorized: Authentication token is missing or invalid."
                })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return;
            }

            if (_roles != null && _roles.Length > 0)
            {
                try
                {
                    var userRole = user.GetUserRole();
                    if (!_roles.Contains(userRole))
                    {
                        context.Result = new JsonResult(new
                        {
                            statusCode = 403,
                            message = $"Forbidden: Required role ({string.Join(", ", _roles)}). Your role is '{userRole}'."
                        })
                        {
                            StatusCode = StatusCodes.Status403Forbidden
                        };
                    }
                }
                catch
                {
                    context.Result = new JsonResult(new
                    {
                        statusCode = 403,
                        message = "Forbidden: User role missing or unauthorized."
                    })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                }
            }
        }
    }
}
