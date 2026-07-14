using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WizardMechanicsLab;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            CliOptions options = CliOptions.Parse(args);
            SelfTests.RunAll();
            Console.WriteLine("Self-tests: PASS");
            if (options.SelfTestOnly) return 0;

            var settings = new ExperimentSettings(
                options.Matches,
                options.Seed,
                new SimulationSettings(TimeLimitSeconds: options.TimeLimit));
            ExperimentReport report = new ExperimentRunner(settings).Run();
            string markdown = ReportWriter.ToMarkdown(report);
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() },
            });

            if (options.OutputDirectory is null)
            {
                Console.WriteLine(markdown);
            }
            else
            {
                Directory.CreateDirectory(options.OutputDirectory);
                if (options.Format is OutputFormat.Both or OutputFormat.Markdown)
                    File.WriteAllText(Path.Combine(options.OutputDirectory, "results.md"), markdown);
                if (options.Format is OutputFormat.Both or OutputFormat.Json)
                    File.WriteAllText(Path.Combine(options.OutputDirectory, "results.json"), json);
                Console.WriteLine($"Wrote results to {Path.GetFullPath(options.OutputDirectory)}");
                Console.WriteLine(markdown);
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private enum OutputFormat { Both, Json, Markdown }

    private sealed record CliOptions(
        int Matches,
        ulong Seed,
        double TimeLimit,
        string? OutputDirectory,
        OutputFormat Format,
        bool SelfTestOnly)
    {
        public static CliOptions Parse(string[] args)
        {
            int matches = 40;
            ulong seed = 20260711;
            double timeLimit = 45;
            string? output = null;
            OutputFormat format = OutputFormat.Both;
            bool selfTestOnly = false;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Value() => i + 1 < args.Length
                    ? args[++i]
                    : throw new ArgumentException($"Missing value after {arg}");
                switch (arg)
                {
                    case "--matches": matches = int.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "--seed": seed = ulong.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "--time-limit": timeLimit = double.Parse(Value(), CultureInfo.InvariantCulture); break;
                    case "--out": output = Value(); break;
                    case "--format":
                        format = Value().ToLowerInvariant() switch
                        {
                            "both" => OutputFormat.Both,
                            "json" => OutputFormat.Json,
                            "markdown" or "md" => OutputFormat.Markdown,
                            _ => throw new ArgumentException("--format must be both, json, or markdown"),
                        };
                        break;
                    case "--self-test-only": selfTestOnly = true; break;
                    case "--help":
                        Console.WriteLine("WizardMechanicsLab [--matches N] [--seed N] [--time-limit S] [--out DIR] [--format both|json|markdown] [--self-test-only]");
                        Environment.Exit(0);
                        break;
                    default: throw new ArgumentException("Unknown argument: " + arg);
                }
            }
            if (matches < 1) throw new ArgumentOutOfRangeException(nameof(matches));
            if (timeLimit <= 0) throw new ArgumentOutOfRangeException(nameof(timeLimit));
            return new CliOptions(matches, seed, timeLimit, output, format, selfTestOnly);
        }
    }
}
