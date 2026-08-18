using StreamOrchestra.Tools;

var exitCode = args.Length > 0
    ? args[0].ToLowerInvariant() switch
    {
        "sync-telemetry-overhead" =>
            SyncTelemetryOverheadCommand.Execute(args[1..], Console.Out, Console.Error),
        "sync-pilot" => SyncPilotCommand.Execute(args[1..], Console.Out, Console.Error),
        _ => FeasibilityStatusCommand.Execute(args, Console.Out, Console.Error)
    }
    : FeasibilityStatusCommand.Execute(args, Console.Out, Console.Error);
return exitCode;
