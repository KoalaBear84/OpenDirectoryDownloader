using System.Text;

namespace OpenDirectoryDownloader;

public static class HttpMessageExtensions
{
	public static async Task<string> ToRawString(this HttpRequestMessage httpRequestMessage)
	{
		StringBuilder stringBuilder = new();

		string line1 = $"{httpRequestMessage.Method} {httpRequestMessage.RequestUri} HTTP/{httpRequestMessage.Version}";
		stringBuilder.AppendLine(line1);

		foreach ((string key, IEnumerable<string> value) in httpRequestMessage.Headers)
		{
			foreach (string val in value)
			{
				string header = $"{key}: {val}";
				stringBuilder.AppendLine(header);
			}
		}

		if (httpRequestMessage.Content?.Headers != null)
		{
			foreach ((string key, IEnumerable<string> value) in httpRequestMessage.Content.Headers)
			{
				foreach (string val in value)
				{
					string header = $"{key}: {val}";
					stringBuilder.AppendLine(header);
				}
			}
		}

		stringBuilder.AppendLine();

		string body = await (httpRequestMessage.Content?.ReadAsStringAsync() ?? Task.FromResult<string>(null));

		if (!string.IsNullOrWhiteSpace(body))
		{
			stringBuilder.AppendLine(body);
		}

		return stringBuilder.ToString();
	}

	public static async Task<string> ToRawString(this HttpResponseMessage response)
	{
		StringBuilder stringBuilder = new();

		int statusCode = (int)response.StatusCode;
		string line = $"HTTP/{response.Version} {statusCode} {response.ReasonPhrase}";
		stringBuilder.AppendLine(line);

		foreach ((string key, IEnumerable<string> value) in response.Headers)
		{
			foreach (string val in value)
			{
				string header = $"{key}: {val}";
				stringBuilder.AppendLine(header);
			}
		}

		foreach ((string key, IEnumerable<string> value) in response.Content.Headers)
		{
			foreach (string val in value)
			{
				string header = $"{key}: {val}";
				stringBuilder.AppendLine(header);
			}
		}

		stringBuilder.AppendLine();

		string body = await response.Content.ReadAsStringAsync();

		if (!string.IsNullOrWhiteSpace(body))
		{
			stringBuilder.AppendLine(body);
		}

		return stringBuilder.ToString();
	}
}
