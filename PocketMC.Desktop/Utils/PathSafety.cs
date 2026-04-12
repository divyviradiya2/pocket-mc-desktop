using System;
using System.IO;

namespace PocketMC.Desktop.Utils
{
    /// <summary>
    /// Shared path-safety utilities for validating file paths against
    /// directory traversal attacks (zip-slip, untrusted JSON paths, etc.).
    /// </summary>
    public static class PathSafety
    {
        /// <summary>
        /// Checks if a relative file path contains traversal sequences (../, ..\)
        /// that could escape a root directory. Safe to call during parse-time
        /// before the actual destination root is known.
        /// </summary>
        public static bool ContainsTraversal(string relativePath)
        {
            const string syntheticRoot = @"C:\syntheticroot\";
            try
            {
                string resolved = Path.GetFullPath(Path.Combine(syntheticRoot, relativePath));
                return !resolved.StartsWith(syntheticRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return true; // If Path.GetFullPath throws, the path is suspicious
            }
        }

        /// <summary>
        /// Validates that a resolved destination path remains within the given root directory.
        /// Returns the sanitized full path, or null if the path escapes the root.
        /// </summary>
        public static string? ValidateContainedPath(string rootDirectory, string relativePath)
        {
            string root = Path.GetFullPath(rootDirectory);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;

            string resolved = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
            return resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? resolved : null;
        }
    }
}
