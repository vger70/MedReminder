namespace MedReminder.Application.Abstractions;

// Access level of a profile inside the multi-user layer
// (docs/ANALYSIS-MULTI-USER.md §1.1a).
//
// User: manages only its own profile data (medicines, stock,
// therapies, personal email recipient).
// Admin: additionally manages the global settings (SMTP, backup) and
// the profile registry (create / rename / delete / PIN).
//
// The role is soft security: someone with filesystem access can edit
// profiles.json by hand and become admin. The UI honors the role, the
// filesystem does not. The role is immutable after creation in this
// increment — no promote / demote flow (§14a G).
public enum ProfileRole
{
    // Default enum value is User by design: any unknown role read from
    // profiles.json falls back to the least-privileged level.
    User = 0,
    Admin = 1,
}
