# ArqamBase desktop app

WinForms on .NET 10, Krypton ribbon, EF Core. Replaces the Access front end module by module.

## Run

```bash
dotnet run --project ArqamBase.App
```

Optional: `ArqamBase.exe --open setuplists` opens a screen at start (keys are in `Shell/ScreenCatalog.cs`).

## Database connection

- `ArqamBase.App/appsettings.json` holds the connection string. It uses Windows sign-in (`Trusted_Connection=True`) to `ArqamBase_dev` on DAS-SVR.
- The environment variable `ARQAMBASE_CONNECTION` replaces it, for example to test with a SQL login. Never put passwords in `appsettings.json`.
- Student pictures and documents are saved in `StudentAttachmentsFolder` (default `\\das-svr\arqambase\StudentsAttachments`), named `[CC]_[Name]_[Type]`, and listed in `tblStudentDocuments` (run `sql/student_documents_install.sql`). `ARQAMBASE_ATTACHMENTS` replaces the folder for testing. Each user's DAS-SVR Windows account needs Modify rights on that folder.
- Before first use on a database, run `sql/app_users_install.sql` (creates `AppUsers`).

## Sign-in (two levels)

1. **ArqamBase account**: user name and password from `AppUsers`. Passwords are stored as salted PBKDF2 hashes. 5 wrong passwords lock the account for 15 minutes. When `AppUsers` is empty, the first sign-in creates that user as Admin.
2. **SQL Server**: the user's Windows account on the server (for example `DAS-SVR\feeclerk1`) and its password. The app connects with these credentials (like `runas /netonly`), so it works in a workgroup. The login can be remembered on that PC only, encrypted by Windows for that PC user; it is never stored in the database. Create the server accounts with `sql/windows_logins_template.sql`.

Admins add users in Setup lists > Users & roles and give each a temporary password ("Set password"); the user picks their own at first sign-in. Audit rows record the SQL login and the app user.

## Layout

| Folder | What |
|---|---|
| `ArqamBase.Data` | Entities and `ArqamDbContext` (maps the existing tables; soft delete via `isDeleted`; sends the Windows user to the audit triggers) |
| `ArqamBase.App/Shell` | Main window: ribbon, navigation pane, tabs, status bar; `ScreenCatalog` lists every screen |
| `ArqamBase.App/Screens` | One class per screen (`ScreenBase` gives New/Save/Delete/Undo/Sort/Filter to the ribbon) |
| `ArqamBase.App/Controls` | `AccessGrid` (sort by heading, filter by heading arrow) and `FilterBindingList` |
| `ArqamBase.App/Core` | Settings, user roles, theme (Light/Dark/Follow Windows), icons |

User preferences (theme, navigation pane) are saved per Windows user in `%LocalAppData%\ArqamBase\settings.json`.
