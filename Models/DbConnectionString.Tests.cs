using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NUnit.Framework;

namespace Coflnet.Sky.Proxy.Models;

public class DbConnectionStringTests
{
    private const string Base = "host=155.133.23.194,155.133.23.195;database=proxy;user id=proxy;password=x;port=26257";
    private string dir;
    private string ca, cert, key;

    [SetUp]
    public void Setup()
    {
        dir = Path.Combine(Path.GetTempPath(), "dbcs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        ca = Path.Combine(dir, "ca.crt");
        cert = Path.Combine(dir, "tls.crt");
        key = Path.Combine(dir, "tls.key");
        foreach (var f in new[] { ca, cert, key })
            File.WriteAllText(f, "x");
    }

    [TearDown]
    public void Cleanup() => Directory.Delete(dir, true);

    private static IConfiguration Config(params (string, string)[] values)
    {
        var data = new Dictionary<string, string> { ["DB_CONNECTION"] = Base };
        foreach (var (k, v) in values)
            data[k] = v;
        return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
    }

    [Test]
    public void WithoutTlsSettingsConnectionStringIsUnchanged()
    {
        Assert.That(DbConnectionString.Build(Config()), Is.EqualTo(Base));
        // empty values (unset env placeholders) behave like unset
        Assert.That(DbConnectionString.Build(Config(("DB:TLS:CA_CERT_PATH", ""), ("DB:TLS:CERT_PATH", " "))), Is.EqualTo(Base));
    }

    [Test]
    public void CaOnlyForcesVerifyFullWithoutClientCertificate()
    {
        var b = new NpgsqlConnectionStringBuilder(DbConnectionString.Build(Config(("DB:TLS:CA_CERT_PATH", ca))));
        Assert.That(b.SslMode, Is.EqualTo(SslMode.VerifyFull));
        Assert.That(b.RootCertificate, Is.EqualTo(ca));
        Assert.That(b.SslCertificate, Is.Null.Or.Empty);
        Assert.That(b.Host, Is.EqualTo("155.133.23.194,155.133.23.195"));
        Assert.That(b.Username, Is.EqualTo("proxy"));
    }

    [Test]
    public void ExistingWeakerSslModeIsOverriddenByStrictVerification()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["DB_CONNECTION"] = Base + ";SSL Mode=Prefer;Trust Server Certificate=true",
            ["DB:TLS:CA_CERT_PATH"] = ca,
        }).Build();
        var b = new NpgsqlConnectionStringBuilder(DbConnectionString.Build(config));
        Assert.That(b.SslMode, Is.EqualTo(SslMode.VerifyFull));
    }

    [Test]
    public void ClientCertificateAndKeyAreApplied()
    {
        var b = new NpgsqlConnectionStringBuilder(DbConnectionString.Build(Config(
            ("DB:TLS:CA_CERT_PATH", ca), ("DB:TLS:CERT_PATH", cert), ("DB:TLS:KEY_PATH", key))));
        Assert.That(b.SslCertificate, Is.EqualTo(cert));
        Assert.That(b.SslKey, Is.EqualTo(key));
    }

    [Test]
    public void FailsClosedOnIncompleteOrMissingFiles()
    {
        Assert.Throws<InvalidOperationException>(() => DbConnectionString.Build(Config(("DB:TLS:CERT_PATH", cert), ("DB:TLS:KEY_PATH", key))));
        Assert.Throws<InvalidOperationException>(() => DbConnectionString.Build(Config(("DB:TLS:CA_CERT_PATH", ca), ("DB:TLS:CERT_PATH", cert))));
        Assert.Throws<InvalidOperationException>(() => DbConnectionString.Build(Config(("DB:TLS:CA_CERT_PATH", Path.Combine(dir, "nope.crt")))));
        File.Delete(key);
        Assert.Throws<InvalidOperationException>(() => DbConnectionString.Build(Config(
            ("DB:TLS:CA_CERT_PATH", ca), ("DB:TLS:CERT_PATH", cert), ("DB:TLS:KEY_PATH", key))));
    }

    [Test]
    public void TrustServerCertificateIsDisabledWhenStrictTlsIsConfigured()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["DB_CONNECTION"] = Base + ";Trust Server Certificate=true",
            ["DB:TLS:CA_CERT_PATH"] = ca,
        }).Build();
        var b = new NpgsqlConnectionStringBuilder(DbConnectionString.Build(config));
        Assert.That(b.TrustServerCertificate, Is.False);
    }

    [Test]
    public void LegacyClientIdentityOptionsDoNotSurviveStrictTls()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["DB_CONNECTION"] = Base + ";SSL Certificate=/old/tls.crt;SSL Key=/old/tls.key;SSL Password=old",
            ["DB:TLS:CA_CERT_PATH"] = ca,
        }).Build();
        var b = new NpgsqlConnectionStringBuilder(DbConnectionString.Build(config));
        Assert.That(b.SslCertificate, Is.Null.Or.Empty);
        Assert.That(b.SslKey, Is.Null.Or.Empty);
        Assert.That(b.SslPassword, Is.Null.Or.Empty);
    }

    [TestCase(0, 300)]
    [TestCase(-1, 300)]
    [TestCase(40, 40)]
    [TestCase(3600, 300)]
    public void StrictTlsBoundsPhysicalConnectionLifetime(int configured, int expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["DB_CONNECTION"] = Base + ";Connection Lifetime=" + configured,
            ["DB:TLS:CA_CERT_PATH"] = ca,
        }).Build();
        var b = new NpgsqlConnectionStringBuilder(DbConnectionString.Build(config));
        Assert.That(b.ConnectionLifetime, Is.EqualTo(expected));
    }

    [Test]
    public void LifetimeIsLeftAloneWithoutTlsSettings()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["DB_CONNECTION"] = Base + ";Connection Lifetime=3600",
        }).Build();
        Assert.That(DbConnectionString.Build(config), Does.Contain("Connection Lifetime=3600"));
    }

    [Test]
    public void InvalidTlsSettingsFailWhenServicesAreConfiguredNotOnFirstRequest()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["DB_CONNECTION"] = Base,
            ["DB:TLS:CA_CERT_PATH"] = Path.Combine(dir, "missing.crt"),
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        Assert.Throws<InvalidOperationException>(() => new Coflnet.Sky.Proxy.Startup(config).ConfigureServices(services));
    }
}
