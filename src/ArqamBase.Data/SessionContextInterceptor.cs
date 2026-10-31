using System.Data.Common;
using System.Security.Principal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Win32.SafeHandles;

namespace ArqamBase.Data;

/// <summary>
/// Opens connections as the user's Windows account on the server (when a token is given),
/// then stores the app user in SESSION_CONTEXT so audit rows show who made a change.
/// </summary>
public sealed class SessionContextInterceptor(string appUser, SafeAccessTokenHandle? windowsToken) : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        if (windowsToken is null)
            return result;
        WindowsIdentity.RunImpersonated(windowsToken, connection.Open);
        return InterceptionResult.Suppress();
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData,
        InterceptionResult result, CancellationToken cancellationToken = default)
    {
        if (windowsToken is null)
            return result;
        await WindowsIdentity.RunImpersonatedAsync(windowsToken, () => connection.OpenAsync(cancellationToken));
        return InterceptionResult.Suppress();
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = Build(connection);
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = Build(connection);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand Build(DbConnection connection)
    {
        var cmd = connection.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context @key = N'WindowsUser', @value = @u;";
        var p = cmd.CreateParameter();
        p.ParameterName = "@u";
        p.Value = appUser;
        cmd.Parameters.Add(p);
        return cmd;
    }
}
