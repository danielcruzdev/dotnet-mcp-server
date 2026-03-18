using System.Text;
using System.Text.Json;
using DotNetMcpServer.Shared.Json;

namespace DotNetMcpServer.Shared.JsonRpc;

public sealed class JsonRpcStream : IAsyncDisposable
{
    private static readonly byte[] HeaderDelimiter = new byte[] { 13, 10, 13, 10 };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly bool _ownsStreams;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonRpcStream(Stream input, Stream output, bool ownsStreams = false)
    {
        _input = input;
        _output = output;
        _ownsStreams = ownsStreams;
    }

    public async Task<JsonRpcMessage?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var headers = await ReadHeadersAsync(cancellationToken);
        if (headers is null)
        {
            return null;
        }

        if (!headers.TryGetValue("content-length", out var rawContentLength) || !int.TryParse(rawContentLength, out var contentLength) || contentLength < 0)
        {
            throw new InvalidDataException("Cabeçalho Content-Length inválido no stream JSON-RPC.");
        }

        var payload = await ReadExactlyAsync(contentLength, cancellationToken);
        var message = JsonSerializer.Deserialize<JsonRpcMessage>(payload, JsonDefaults.SerializerOptions);

        return message ?? throw new InvalidDataException("Não foi possível desserializar a mensagem JSON-RPC.");
    }

    public async Task WriteMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonDefaults.SerializerOptions);
        var headerBytes = Encoding.ASCII.GetBytes($"Content-Length: {payload.Length}\r\n\r\n");

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _output.WriteAsync(headerBytes.AsMemory(), cancellationToken);
            await _output.WriteAsync(payload.AsMemory(), cancellationToken);
            await _output.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<Dictionary<string, string>?> ReadHeadersAsync(CancellationToken cancellationToken)
    {
        var headerBytes = new List<byte>(256);
        var matched = 0;

        while (true)
        {
            var next = await ReadSingleByteAsync(cancellationToken);
            if (next is null)
            {
                if (headerBytes.Count == 0)
                {
                    return null;
                }

                throw new EndOfStreamException("Stream finalizado no meio dos cabeçalhos JSON-RPC.");
            }

            var value = next.Value;
            headerBytes.Add(value);

            if (value == HeaderDelimiter[matched])
            {
                matched++;
                if (matched == HeaderDelimiter.Length)
                {
                    break;
                }
            }
            else
            {
                matched = value == HeaderDelimiter[0] ? 1 : 0;
            }
        }

        var headerWithoutDelimiter = headerBytes.Take(headerBytes.Count - HeaderDelimiter.Length).ToArray();
        var headerText = Encoding.ASCII.GetString(headerWithoutDelimiter);
        var parsedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            parsedHeaders[key] = value;
        }

        return parsedHeaders;
    }

    private async Task<byte?> ReadSingleByteAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var bytesRead = await _input.ReadAsync(buffer.AsMemory(0, 1), cancellationToken);
        if (bytesRead == 0)
        {
            return null;
        }

        return buffer[0];
    }

    private async Task<byte[]> ReadExactlyAsync(int length, CancellationToken cancellationToken)
    {
        var payload = new byte[length];
        var offset = 0;

        while (offset < length)
        {
            var read = await _input.ReadAsync(payload.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException("Stream finalizado no meio do payload JSON-RPC.");
            }

            offset += read;
        }

        return payload;
    }

    public async ValueTask DisposeAsync()
    {
        _writeLock.Dispose();

        if (_ownsStreams)
        {
            await _input.DisposeAsync();

            if (!ReferenceEquals(_input, _output))
            {
                await _output.DisposeAsync();
            }
        }
    }
}



