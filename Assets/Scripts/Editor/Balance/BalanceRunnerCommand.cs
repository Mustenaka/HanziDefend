using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HanziDefend.Editor.Balance
{
    public static class BalanceRunnerCommand
    {
        private const int DefaultGamesPerCohort = 25;

        [MenuItem("HanziDefend/Balance/Probe (1 per Cohort)")]
        public static void ProbeFourFromMenu()
        {
            RunAndLog(1, DefaultOutputDirectory());
        }

        [MenuItem("HanziDefend/Balance/Probe (5 per Cohort)")]
        public static void ProbeTwentyFromMenu()
        {
            RunAndLog(5, DefaultOutputDirectory());
        }

        [MenuItem("HanziDefend/Balance/Run (25 per Cohort)")]
        public static void RunHundredTotalFromMenu()
        {
            RunAndLog(DefaultGamesPerCohort, DefaultOutputDirectory());
        }

        [MenuItem("HanziDefend/Balance/Run (100 per Cohort)")]
        public static void RunHundredPerCohortFromMenu()
        {
            RunAndLog(100, DefaultOutputDirectory());
        }

        /// <summary>
        /// Unity command-line entry point:
        /// -executeMethod HanziDefend.Editor.Balance.BalanceRunnerCommand.RunFromCommandLine
        /// [-balanceGamesPerCohort 25] [-balanceOutput path] [-balanceSeed 0xE1002026]
        /// </summary>
        public static void RunFromCommandLine()
        {
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                int games = ParseInt(args, "-balanceGamesPerCohort", DefaultGamesPerCohort);
                uint seed = ParseUInt(args, "-balanceSeed", 0xE1002026u);
                string output = ParseString(args, "-balanceOutput", DefaultOutputDirectory());
                RunAndLog(games, output, seed);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (Application.isBatchMode)
                {
                    EditorApplication.Exit(1);
                    return;
                }

                throw;
            }
        }

        private static void RunAndLog(
            int gamesPerCohort,
            string outputDirectory,
            uint seed = 0xE1002026u)
        {
            var runner = new BalanceRunner();
            BalanceRunReport report = runner.Run(BalanceRunRequest.CreateDefault(
                gamesPerCohort,
                outputDirectory,
                seed));
            Debug.Log(
                $"BalanceRunner finished {report.Battles.Count} games in "
                + $"{report.WallClockSeconds:F3}s. Results: {Path.GetFullPath(outputDirectory)}");
        }

        /// <summary>
        /// WO-E4 owns the current cohort set, so menu runs land there. WO-E1-Results stays frozen
        /// as the evidence the suspended acceptance probe points at.
        /// </summary>
        private static string DefaultOutputDirectory()
        {
            return Path.Combine(
                Directory.GetCurrentDirectory(),
                "Docs",
                "Plan",
                "REVIEW",
                "WO-E4-Results");
        }

        private static string ParseString(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        private static int ParseInt(string[] args, string name, int fallback)
        {
            string value = ParseString(args, name, null);
            if (value == null)
            {
                return fallback;
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                || parsed <= 0)
            {
                throw new ArgumentException($"{name} must be a positive integer; received '{value}'.");
            }

            return parsed;
        }

        private static uint ParseUInt(string[] args, string name, uint fallback)
        {
            string value = ParseString(args, name, null);
            if (value == null)
            {
                return fallback;
            }

            string digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(2)
                : value;
            NumberStyles style = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? NumberStyles.AllowHexSpecifier
                : NumberStyles.Integer;
            if (!uint.TryParse(digits, style, CultureInfo.InvariantCulture, out uint parsed))
            {
                throw new ArgumentException($"{name} must be uint/hex; received '{value}'.");
            }

            return parsed;
        }
    }
}
