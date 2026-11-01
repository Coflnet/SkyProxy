using System;
using System.IO;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Coflnet.Sky.Proxy.Models;

/// <summary>
/// Builds the Npgsql connection string. Without any <c>DB:TLS</c> setting the configured
/// <c>DB_CONNECTION</c> is returned untouched (current behaviour). With <c>DB:TLS:CA_CERT_PATH</c>
/// the connection is forced to strict <c>VerifyFull</c> against that CA bundle, and an optional
/// <c>CERT_PATH</c>/<c>KEY_PATH</c> pair is presented as the SQL client certificate. Files are read
/// by Npgsql on every new physical connection, so a renewed projected Secret is picked up without a restart.
/// </summary>
public static class DbConnectionString
{
    public static string Build(IConfiguration configuration)
    {
        var connectionString = configuration["DB_CONNECTION"] ?? string.Empty;
        var ca = Value(configuration, "DB:TLS:CA_CERT_PATH");
        var cert = Value(configuration, "DB:TLS:CERT_PATH");
        var key = Value(configuration, "DB:TLS:KEY_PATH");
        if (ca == null && cert == null && key == null)
            return connectionString;
        if (ca == null)
            throw new InvalidOperationException("DB:TLS:CERT_PATH/KEY_PATH require DB:TLS:CA_CERT_PATH (strict server verification)");
        if ((cert == null) != (key == null))
            throw new InvalidOperationException("DB:TLS:CERT_PATH and DB:TLS:KEY_PATH must be configured together");
        RequireFile("DB:TLS:CA_CERT_PATH", ca);
        if (cert != null)
        {
            RequireFile("DB:TLS:CERT_PATH", cert);
            RequireFile("DB:TLS:KEY_PATH", key);
        }
        return StrictTls(connectionString, ca, cert, key);
    }

    /// <summary>Upper bound for the lifetime of a physical connection, so renewed client certificates are picked up.</summary>
    internal const int MaxConnectionLifetimeSeconds = 300;

    /// <summary>Upper bound for the connect timeout (same value as SkyChat) so abandoned attempts free their socket and pool slot quickly.</summary>
    internal const int MaxConnectTimeoutSeconds = 10;

    private static string StrictTls(string connectionString, string ca, string cert, string key)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            SslMode = SslMode.VerifyFull,
            RootCertificate = ca,
            TrustServerCertificate = false,
            // legacy client identity options must not survive: either the configured pair or none
            SslCertificate = cert,
            SslKey = key,
            SslPassword = null,
        };
        var lifetime = builder.ConnectionLifetime;
        builder.ConnectionLifetime = lifetime <= 0 ? MaxConnectionLifetimeSeconds : Math.Min(lifetime, MaxConnectionLifetimeSeconds);
        builder.Timeout = builder.Timeout <= 0 ? MaxConnectTimeoutSeconds : Math.Min(builder.Timeout, MaxConnectTimeoutSeconds);
        return builder.ConnectionString;
    }

    private static string Value(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static void RequireFile(string setting, string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"{setting} points to a missing file");
    }
}
