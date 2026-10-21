// Developer CLI: drives the environment setup and the headless emulator without the UI.
// andro-dev check|setup|run [--data-root <dir>]
using Andro.Application.Abstractions;
using Andro.Application.Paths;
using Andro.Application.Setup;
using Andro.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

Console.OutputEncoding = System.Text.Encoding.UTF8;

var command = args.FirstOrDefault() ?? "";
var rootIndex = Array.IndexOf(args, "--data-root");
var paths = rootIndex >= 0 && rootIndex + 1 < args.Length ? new AndroPaths(Path.GetFullPath(args[rootIndex + 1])) : AndroPaths.Default();

using var services = new ServiceCollection().AddAndro(paths).BuildServiceProvider();
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

Console.WriteLine($"Données : {paths.Root}");
switch (command)
{
    case "check":
        var report = await services.GetRequiredService<IPrerequisiteChecker>().CheckAsync(paths, cts.Token);
        Console.WriteLine($"Accélération : {report.Acceleration}");
        Console.WriteLine($"  {report.AccelerationDetail.ReplaceLineEndings(" ")}");
        Console.WriteLine($"Disque libre : {report.FreeDiskBytes / 1e9:F1} Go ({(report.DiskOk ? "OK" : "insuffisant, 15 Go requis")})");
        Console.WriteLine($"RAM : {report.TotalRamBytes / 1e9:F1} Go ({(report.RamOk ? "OK" : "insuffisante, 8 Go requis")})");
        return 0;

    case "setup":
        var setup = services.GetRequiredService<SetupService>();
        var plan = await setup.PlanAsync(cts.Token);
        if (plan.Pending.Count == 0)
        {
            Console.WriteLine("Tout est déjà installé.");
        }
        Console.WriteLine($"À télécharger : {plan.DownloadBytes / 1e9:F2} Go ({string.Join(", ", plan.Pending.Select(c => c.PackagePath))})");
        foreach (var license in plan.LicensesToAccept)
        {
            Console.WriteLine($"\n===== Licence {license.Id} =====\n{license.Text}\n");
        }
        if (plan.LicensesToAccept.Count > 0)
        {
            Console.Write("Acceptez-vous ces licences ? Tapez « oui » pour continuer : ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "oui", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Licences refusées : rien n'a été téléchargé.");
                return 1;
            }
            await setup.AcceptLicensesAsync(plan.LicensesToAccept);
        }
        var lastLine = "";
        await setup.RunAsync(plan, new ConsoleProgress(p =>
        {
            var percent = p.Total > 0 ? p.Received * 100 / p.Total : 0;
            var line = $"{p.Phase,-8} {p.PackagePath} {percent,3}%";
            if (line != lastLine) Console.WriteLine(lastLine = line);
        }), cts.Token);
        Console.WriteLine("Environnement prêt.");
        return 0;

    case "run":
        var launcher = services.GetRequiredService<IEmulatorLauncher>();
        Console.WriteLine("Démarrage d'Android…");
        var session = await launcher.StartAsync("Andro", cts.Token);
        Console.WriteLine($"Prêt : {session.AdbSerial}, gRPC 127.0.0.1:{session.GrpcPort}, jeton {session.GrpcToken}");
        Console.WriteLine("Ctrl+C pour arrêter.");
        try
        {
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        await launcher.StopAsync(session, CancellationToken.None);
        Console.WriteLine("Arrêté.");
        return 0;

    default:
        Console.WriteLine("Usage : andro-dev check|setup|run [--data-root <dossier>]");
        return 2;
}

internal sealed class ConsoleProgress(Action<SetupProgress> report) : IProgress<SetupProgress>
{
    public void Report(SetupProgress value) => report(value);
}
