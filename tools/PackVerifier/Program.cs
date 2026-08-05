using System;
using System.IO;
using Runewake.Engine.Cards;

namespace Runewake.PackVerifier;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: PackVerifier <path-to-pack.json>");
            return 2;
        }

        var path = Path.GetFullPath(args[0]);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Pack file not found: {path}");
            return 2;
        }

        var json = File.ReadAllText(path);
        var verified = ContentManager.VerifyPackJson(json);

        Console.WriteLine($"VERIFY_RESULT={verified}");
        Console.WriteLine($"PACK={path}");

        // Also demonstrate the tamper case
        var tampered = json.Replace("Root Warden", "ROOT_TAMPERED");
        var tamperedRejected = !ContentManager.VerifyPackJson(tampered);
        Console.WriteLine($"TAMPER_REJECTED={tamperedRejected}");

        return verified ? 0 : 1;
    }
}