using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenDirectoryDownloader.Cli;
using System.Diagnostics;

namespace OpenDirectoryDownloader.Hosting;

/// <summary>Shows the logs of the FlareSolverr Docker container while scanning (--flaresolverr-docker-name).</summary>
public sealed class FlareSolverrLogStreamer(HttpOptions httpOptions, ILogger<FlareSolverrLogStreamer> logger) : IHostedService, IDisposable
{
	private Process _process;

	public Task StartAsync(CancellationToken cancellationToken)
	{
		string containerName = httpOptions.FlareSolverrDockerName;

		if (string.IsNullOrWhiteSpace(containerName))
		{
			return Task.CompletedTask;
		}

		try
		{
			ProcessStartInfo processStartInfo = new("docker")
			{
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			processStartInfo.ArgumentList.Add("logs");
			processStartInfo.ArgumentList.Add("--tail");
			processStartInfo.ArgumentList.Add("0");
			processStartInfo.ArgumentList.Add("--follow");
			processStartInfo.ArgumentList.Add(containerName);

			_process = new Process { StartInfo = processStartInfo };
			_process.OutputDataReceived += (_, e) => WriteLine(e.Data, isError: false);
			_process.ErrorDataReceived += (_, e) => WriteLine(e.Data, isError: true);

			if (!_process.Start())
			{
				logger.LogWarning("Failed to start FlareSolverr Docker log streaming for container '{containerName}'", containerName);
				_process.Dispose();
				_process = null;
				return Task.CompletedTask;
			}

			logger.LogWarning("Streaming FlareSolverr Docker logs from container '{containerName}'", containerName);
			_process.BeginOutputReadLine();
			_process.BeginErrorReadLine();
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to start FlareSolverr Docker log streaming for container '{containerName}'", containerName);
		}

		return Task.CompletedTask;
	}

	private void WriteLine(string logLine, bool isError)
	{
		if (string.IsNullOrWhiteSpace(logLine))
		{
			return;
		}

		string formattedLogLine = $"[FlareSolverr] {logLine}";

		if (isError)
		{
			logger.LogWarning("{logLine}", formattedLogLine);
			return;
		}

		Console.WriteLine(formattedLogLine);
		logger.LogInformation("{logLine}", formattedLogLine);
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		if (_process is null)
		{
			return;
		}

		try
		{
			if (!_process.HasExited)
			{
				_process.Kill(entireProcessTree: true);
				await _process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
			}
		}
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Failed to stop FlareSolverr Docker log streaming for container '{containerName}'", httpOptions.FlareSolverrDockerName);
		}
	}

	public void Dispose() => _process?.Dispose();
}
