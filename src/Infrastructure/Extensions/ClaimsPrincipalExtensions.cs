using System.Security.Claims;
using System;
namespace auth.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static string GetUserId(this ClaimsPrincipal principal)
        {
            return principal.FindFirstValue(ClaimTypes.NameIdentifier) 
                ?? throw new InvalidOperationException("User ID not found");
        }
    }
}
