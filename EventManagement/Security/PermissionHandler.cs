using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace EventManagement.Security;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionHandler(
    UserManager<IdentityUser> users,
    RoleManager<IdentityRole> roles) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User?.Identity?.IsAuthenticated != true) return;

        var user = await users.GetUserAsync(context.User);
        if (user is null) return;

        var userRoles = await users.GetRolesAsync(user);
        foreach (var roleName in userRoles)
        {
            var role = await roles.FindByNameAsync(roleName);
            if (role is null) continue;

            var roleClaims = await roles.GetClaimsAsync(role);
            if (roleClaims.Any(c => c.Type == Permissions.ClaimType &&
                                    c.Value == requirement.Permission))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}
