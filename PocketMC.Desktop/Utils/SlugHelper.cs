using System;
using System.Text.RegularExpressions;

namespace PocketMC.Desktop.Utils
{
    public static class SlugHelper
    {
        public static string GenerateSlug(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "unnamed-server";

            // Convert to lowercase
            string slug = input.ToLowerInvariant();

            // Replace spaces and invalid filename characters with hyphens
            slug = Regex.Replace(slug, @"[^a-z0-9\-_]", "-");

            // Remove multiple consecutive hyphens
            slug = Regex.Replace(slug, @"-+", "-");

            // Trim hyphens from start and end
            slug = slug.Trim('-');

            if (string.IsNullOrEmpty(slug))
                return "unnamed-server";

            return slug;
        }
    }
}
