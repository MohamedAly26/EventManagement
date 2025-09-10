namespace EventManagement.Security
{
    using System.Collections.Generic;

    public static class Permissions
    {
        public const string ClaimType = "permission";

        public static class Names
        {
            public const string ManageEvents = "events.manage";
            public const string ViewSubscribers = "subscribers.view";
            public const string ManageUsers = "users.manage";
            public const string ManageRoles = "roles.manage";
            public const string ConfigurePermissions = "permissions.configure"; // può modificare la matrice
        }

        public static readonly string[] All =
        {
            Names.ManageEvents,
            Names.ViewSubscribers,
            Names.ManageUsers,
            Names.ManageRoles,
            Names.ConfigurePermissions
        };
    }
}
