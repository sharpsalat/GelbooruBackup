using System;

namespace GelbooruBackup
{
    public static class EnvHelper
    {
        public static string GetRequiredEnv(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"Environment variable '{name}' is required but was not set.");
            return value;
        }

        public static string? GetOptionalStringEnv(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        public static bool? GetOptionalBoolEnv(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value))
                return null;

            if (bool.TryParse(value, out var parsed))
                return parsed;

            if (value == "1")
                return true;
            if (value == "0")
                return false;

            return null;
        }

        public static int? GetOptionalIntEnv(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(value))
                return null;

            if (int.TryParse(value, out var parsed))
                return parsed;

            return null;
        }

        // Optional URL env with validation, returns string (trimmed) when caller prefers the original URL text.
        // Returns null if variable is not set or empty.
        // Throws InvalidOperationException if variable is set but not a valid absolute http/https URL.
        public static string? GetOptionalUrlEnv(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return trimmed;
            }

            throw new InvalidOperationException($"Environment variable '{name}' is set but is not a valid absolute HTTP/HTTPS URL: '{value}'.");
        }

        // Optional URL env with validation, returns the original string (trimmed).
        // Returns null if variable is not set or empty.
        // Throws InvalidOperationException if variable is set but not a valid absolute http/https URL.
        public static string? GetOptionalUrlEnvString(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return trimmed;
            }

            throw new InvalidOperationException($"Environment variable '{name}' is set but is not a valid absolute HTTP/HTTPS URL: '{value}'.");
        }
    }
}