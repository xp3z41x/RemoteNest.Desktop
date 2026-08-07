using System.Reflection;
using FluentAssertions;
using RemoteNest.Models;

namespace RemoteNest.Tests;

/// <summary>
/// Reflection helpers so tests automatically cover every <see cref="ConnectionProfile"/>
/// property — new fields are exercised without touching the tests.
/// </summary>
internal static class ProfileReflection
{
    public static PropertyInfo[] SettableProperties => typeof(ConnectionProfile)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanRead && p.CanWrite)
        .ToArray();

    /// <summary>Builds a profile with every property set to a seed-dependent non-default value.</summary>
    public static ConnectionProfile CreateFullyPopulated(int seed)
    {
        var profile = new ConnectionProfile();
        var i = 0;
        foreach (var prop in SettableProperties)
        {
            if (prop.Name == nameof(ConnectionProfile.Id)) continue;
            i++;

            if (prop.PropertyType == typeof(bool))
                prop.SetValue(profile, !(bool)prop.GetValue(profile)!); // flip the default
            else if (prop.PropertyType == typeof(int))
                prop.SetValue(profile, (int)prop.GetValue(profile)! + seed + i);
            else if (prop.PropertyType == typeof(string))
                prop.SetValue(profile, $"v{seed}-{prop.Name}-çã"); // unicode on purpose
            else if (prop.PropertyType == typeof(DateTime))
                prop.SetValue(profile, new DateTime(2026, 1, 1, 8, 30, 0, DateTimeKind.Utc).AddDays(seed + i));
            else
                throw new InvalidOperationException($"Unhandled property type {prop.PropertyType} ({prop.Name})");
        }
        return profile;
    }

    public static void AssertAllPropertiesEqual(
        ConnectionProfile expected,
        ConnectionProfile actual,
        bool ignoreCreatedAt = false,
        string[]? ignore = null)
    {
        foreach (var prop in SettableProperties)
        {
            if (ignore is not null && ignore.Contains(prop.Name)) continue;
            if (ignoreCreatedAt && prop.Name == nameof(ConnectionProfile.CreatedAt)) continue;

            var expectedValue = prop.GetValue(expected);
            var actualValue = prop.GetValue(actual);
            actualValue.Should().Be(expectedValue, $"property {prop.Name} must survive the roundtrip");
        }
    }
}
