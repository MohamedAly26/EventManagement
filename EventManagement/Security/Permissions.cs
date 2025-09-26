namespace EventManagement.Security;

public static class Permissions
{
    public const string ClaimType = "permission";

    public static class Names
    {
        public const string ManageEvents = "events.manage";
        public const string PermissionsConfigure = "permissions.configure";
        public const string ManageRoles = "roles.manage";
        public const string ViewSubscribers = "subscribers.view";
        public const string ManageUsers = "users.manage";
    }

    public static readonly string[] All =
    {
        Names.ManageEvents, Names.PermissionsConfigure, Names.ManageRoles,
        Names.ViewSubscribers, Names.ManageUsers
    };
}

