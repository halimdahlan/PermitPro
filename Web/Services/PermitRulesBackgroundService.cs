using PermitPro.Core.Interfaces;

namespace PermitPro.App.Services;

// Runs the companies' enabled permit rules (Overdue, Auto-archive) every PermitRules:IntervalMinutes
// (default 5). Hosted in the web app: IIS must keep the app pool running (AlwaysRunning + preload,
// idle time-out 0), otherwise the rules stop while nobody uses the site. PermitRuleService takes a
// SQL Server app lock, so overlapping instances during an app pool recycle don't both run them.
public class PermitRulesBackgroundService : BackgroundService
{
	private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IConfiguration _configuration;
	private readonly ILogger<PermitRulesBackgroundService> _logger;

	public PermitRulesBackgroundService(
		IServiceScopeFactory scopeFactory,
		IConfiguration configuration,
		ILogger<PermitRulesBackgroundService> logger)
	{
		_scopeFactory = scopeFactory;
		_configuration = configuration;
		_logger = logger;
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var minutes = Math.Clamp(_configuration.GetValue("PermitRules:IntervalMinutes", 5), 1, 1440);

		try
		{
			// Let the app finish starting before the first run.
			await Task.Delay(StartupDelay, stoppingToken);

			using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
			do
			{
				await RunOnceAsync(stoppingToken);
			}
			while (await timer.WaitForNextTickAsync(stoppingToken));
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
			// App shutting down.
		}
	}

	private async Task RunOnceAsync(CancellationToken ct)
	{
		try
		{
			await using var scope = _scopeFactory.CreateAsyncScope();
			await scope.ServiceProvider.GetRequiredService<IPermitRuleService>().RunAllAsync(ct);
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex)
		{
			// Never let one bad run stop the service.
			_logger.LogError(ex, "Permit rules run failed");
		}
	}
}
