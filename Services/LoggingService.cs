using System;
using System.IO;
using System.Collections.Generic;

namespace UndercutterFFXIV.Services
{
    /// <summary>
    /// Centralized logging service for all plugin operations
    /// </summary>
    public static class LoggingService
    {
        private static string LogFilePath { get; set; } = "";
        private static object lockObject = new object();

        public static void Initialize(string pluginConfigDirectory)
        {
            var logsDir = Path.Combine(pluginConfigDirectory, "logs");
            Directory.CreateDirectory(logsDir);
            LogFilePath = Path.Combine(logsDir, $"market_assistant_{DateTime.Now:yyyy-MM-dd}.log");
        }

        public static void LogInfo(string message)
        {
            LogToFile("INFO", message);
        }

        public static void LogWarning(string message)
        {
            LogToFile("WARN", message);
        }

        public static void LogError(string message)
        {
            LogToFile("ERROR", message);
        }

        public static void LogDebug(string message)
        {
#if DEBUG
            LogToFile("DEBUG", message);
#endif
        }

        private static void LogToFile(string level, string message)
        {
            try
            {
                lock (lockObject)
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var logLine = $"[{timestamp}] [{level}] {message}";
                    File.AppendAllText(LogFilePath, logLine + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                // Silent fail - can't log errors while logging
            }
        }

        public static List<string> GetRecentLogs(int lineCount = 50)
        {
            try
            {
                var lines = File.ReadAllLines(LogFilePath);
                var result = new List<string>();
                int startIndex = Math.Max(0, lines.Length - lineCount);
                for (int i = startIndex; i < lines.Length; i++)
                {
                    result.Add(lines[i]);
                }
                return result;
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}
