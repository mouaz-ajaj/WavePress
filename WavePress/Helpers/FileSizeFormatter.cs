namespace WavePress.Helpers
{
    /// <summary>
    /// أداة تنسيق أحجام الملفات — تحوّل البايتات إلى صيغة مقروءة.
    /// Formats byte counts into human-readable strings (KB, MB, GB).
    /// </summary>
    public static class FileSizeFormatter
    {
        private static readonly string[] Units = { "B", "KB", "MB", "GB", "TB" };

        /// <summary>
        /// يحوّل عدد البايتات إلى نص مقروء (مثل "1.5 MB").
        /// </summary>
        public static string Format(long bytes)
        {
            if (bytes <= 0) return "0 B";

            int unitIndex = 0;
            double size = bytes;

            while (size >= 1024 && unitIndex < Units.Length - 1)
            {
                size /= 1024.0;
                unitIndex++;
            }

            return $"{size:F2} {Units[unitIndex]}";
        }
    }
}
