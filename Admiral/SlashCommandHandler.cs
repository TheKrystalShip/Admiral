using System.Text.RegularExpressions;
using Discord.WebSocket;
using TheKrystalShip.Admiral.Domain;
using TheKrystalShip.Admiral.Services;
using TheKrystalShip.Admiral.Tools;
using TheKrystalShip.Logging;

namespace TheKrystalShip.Admiral
{
    public partial class SlashCommandHandler
    {
        private readonly Logger<SlashCommandHandler> _logger;
        private readonly CommandExecutioner _commandExecutioner;
        private readonly Random _random;

        private readonly string[] _waitVariants = [
            "give me a sec", "will take a sec", "hold on", "I'll get back to you",
            "hang on", "just a moment", "gimme a minute", "processing", "beep boop", "brb",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAHHHH", "beep-bee-bee-boop-bee-doo-weep"
        ];

        private string WaitMessage { get => _waitVariants[_random.Next(_waitVariants.Length)]; }

        public event Func<RunningStatusUpdatedArgs, Task>? RunningStatusUpdated;

        [GeneratedRegex("[^a-zA-Z0-9_.]+", RegexOptions.Compiled)]
        private static partial Regex ChannelNameRegex();

        public SlashCommandHandler()
        {
            _logger = new();
            _commandExecutioner = new();
            _random = new();
        }

        public async Task OnSlashCommandExecuted(SocketSlashCommand command)
        {
            // All commands require the user to specify a game.
            string serviceName = string.Empty;

            if ((command.Data.Options?.First()?.Value) is SocketTextChannel channel)
            {
                serviceName = ChannelNameRegex().Replace(channel.Name, string.Empty);
            }

            Game game = new(
                internalName: serviceName,
                displayName: AppSettings.Get($"games:{serviceName}:displayName"),
                channelId: AppSettings.Get($"games:{serviceName}:channelId")
            );

            // Received command
            _logger.LogInformation("SlashCommand", $">>> /{(command.Data.Name ?? string.Empty) + " " + serviceName}");

            Result result = command.Data.Name switch
            {
                Command.START => await OnGameStartCommandAsync(command, game),
                Command.STOP => await OnGameStopCommandAsync(command, game),
                Command.RESTART => await OnGameRestartCommandAsync(command, game),
                Command.CHECK_FOR_UPDATE => await OnGameCheckForUpdateCommandAsync(command, game),
                Command.STATUS => OnGameStatusCommand(game),
                Command.SET_AUTOSTART => OnGameAutostartCommand(command, game),
                Command.IS_ONLINE => OnGameIsActiveCommand(game),
                Command.IS_AUTOSTART_ENABLED => OnGameIsEnabledCommand(game),

                // Default
                _ => new Result(CommandStatus.Error, "Command not found")
            };

            if (result.IsError)
            {
                await command.RespondAsync($"Error: {result.Output}");
                return;
            }

            // Returned result
            _logger.LogInformation("Discord", $"<<< {result.Output}");

            // Force to not respond, assume it was responded somewhere else
            if (result.Status != CommandStatus.Ignore)
                await command.RespondAsync(result.Output);
        }

        private async Task<Result> OnGameCheckForUpdateCommandAsync(SocketSlashCommand command, Game game)
        {
            // Checking for update take a while, respond first then follow up once execution is finished
            await command.RespondAsync($"Checking for {game} updates, {WaitMessage}");

            Result result = _commandExecutioner.CheckForUpdate(game.internalName);

            // Default answer with no update before checking
            string followupText = $"No update found for {game}";

            // CheckForUpdate returns CommandStatus.Error when there's an update
            if (result.IsError)
            {
                // RunningStatusUpdated?.Invoke(new(game, RunningStatus.NeedsUpdate));
                followupText = $"New version found: {result.Output}";
            }

            await command.FollowupAsync(followupText);

            return new Result(CommandStatus.Ignore, followupText);
        }

        private Result OnGameAutostartCommand(SocketSlashCommand command, Game game)
        {
            int newState = Convert.ToInt32(command.Data.Options.ElementAt(1)?.Value);

            Result result = newState switch
            {
                1 => _commandExecutioner.Enable(game.internalName),
                0 => _commandExecutioner.Disable(game.internalName),

                // Default
                _ => new Result(CommandStatus.Error, "Unknown error")
            };

            if (result.IsSuccess)
            {
                string autostartValue = newState == 1 ? "enabled" : "disabled";
                return new Result($"Autostart {autostartValue} for {game}");
            }

            return new Result(CommandStatus.Error, $"Failed to set autostart for {game}");
        }

        private Result OnGameIsActiveCommand(Game game)
        {
            var isActiveResult = _commandExecutioner.IsActive(game.internalName);
            string result = $"{game} is {GetSynonym(isActiveResult.Output)}";

            static string GetSynonym(string input) =>
                input switch
                {
                    "active" => "online",
                    "inactive" => "offline",
                    _ => input
                };

            return new Result(result);
        }

        private Result OnGameIsEnabledCommand(Game game)
        {
            Result result = _commandExecutioner.IsEnabled(game.internalName);
            return new Result(CommandStatus.Success, $"Autostart is {result.Output} for {game}");
        }

        private async Task<Result> OnGameStartCommandAsync(SocketSlashCommand command, Game game)
        {
            await command.RespondAsync($"Starting {game}, {WaitMessage}");

            Result result = _commandExecutioner.Start(game.internalName);

            if (result.IsSuccess)
            {
                RunningStatusUpdated?.Invoke(new(game.internalName, RunningStatus.Online));
                result.Output = $"Started {game}";
            }

            return new Result(CommandStatus.Ignore, result.Output);
        }

        private async Task<Result> OnGameStopCommandAsync(SocketSlashCommand command, Game game)
        {
            await command.RespondAsync($"Stopping {game}, {WaitMessage}");

            Result result = _commandExecutioner.Stop(game.internalName);

            if (result.IsSuccess)
            {
                RunningStatusUpdated?.Invoke(new(game.internalName, RunningStatus.Offline));
                result.Output = $"Stopped {game}";
            }

            return new Result(CommandStatus.Ignore, result.Output);
        }

        private async Task<Result> OnGameRestartCommandAsync(SocketSlashCommand command, Game game)
        {
            await command.RespondAsync($"Restarting {game}, {WaitMessage}");

            Result result = _commandExecutioner.Restart(game.internalName);

            if (result.IsSuccess)
                result.Output = $"Restarted {game}";

            return new Result(CommandStatus.Ignore, result.Output);
        }

        private Result OnGameStatusCommand(Game game)
        {
            return _commandExecutioner.Status(game.internalName);
        }
    }
}
