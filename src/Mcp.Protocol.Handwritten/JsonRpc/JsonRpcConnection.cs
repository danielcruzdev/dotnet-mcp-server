using System.Buffers;
using System.IO.Pipelines;
using System.Text.Json;
using Mcp.Protocol.Handwritten.Json;

namespace Mcp.Protocol.Handwritten.JsonRpc;

/// <summary>
/// A newline-delimited JSON-RPC channel over a pair of streams, as the MCP stdio transport
/// requires.
/// </summary>
/// <remarks>
/// <para>
/// The MCP specification is explicit: <i>"Messages are delimited by newlines, and MUST NOT
/// contain embedded newlines."</i> An earlier version of this type framed messages with an
/// LSP-style <c>Content-Length</c> header, which no MCP client understands — the defect this
/// artifact exists to document.
/// </para>
/// <para>
/// Reading goes through <see cref="PipeReader"/>, so a message is found by scanning buffered
/// spans for <c>\n</c>. The previous implementation issued one <c>await</c> and one
/// single-byte array allocation per byte read.
/// </para>
/// <para>
/// Writing is safe because <see cref="JsonSerializer"/> escapes newlines inside string values
/// as <c>\n</c>, so a serialized message is always one line. <see cref="WriteMessageAsync"/>
/// asserts that rather than assuming it.
/// </para>
/// </remarks>
public sealed class JsonRpcConnection : IAsyncDisposable
{
    private const byte NewLine = (byte)'\n';

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly PipeReader _reader;
    private readonly bool _ownsStreams;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public JsonRpcConnection(Stream input, Stream output, bool ownsStreams = false)
    {
        _input = input;
        _output = output;
        _ownsStreams = ownsStreams;
        _reader = PipeReader.Create(input, new StreamPipeReaderOptions(leaveOpen: true));
    }

    /// <summary>
    /// Reads the next message, or returns <c>null</c> once the peer closes the stream.
    /// </summary>
    public async Task<JsonRpcMessage?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var read = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = read.Buffer;

            if (TryReadLine(ref buffer, out var line))
            {
                var message = line.Length == 0 ? null : Deserialize(line);

                // Consumed and examined both stop where this line ended. Marking the rest of
                // the buffer examined would tell the reader we are waiting for new bytes, and
                // a second message already sitting there would never be read.
                _reader.AdvanceTo(buffer.Start);

                // A blank line between messages is not a message; skip it rather than
                // failing the session.
                if (message is null)
                {
                    continue;
                }

                return message;
            }

            if (read.IsCompleted)
            {
                // A trailing message with no closing newline is still a message. It is
                // deserialized before AdvanceTo, not after: the buffer goes back to the
                // reader the moment that call returns.
                var trailing = buffer.Length > 0 ? Deserialize(buffer) : null;
                _reader.AdvanceTo(buffer.End);

                return trailing;
            }

            // Nothing consumed, everything examined: there is no complete line yet, so the
            // next read has to wait for more bytes.
            _reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    public async Task WriteMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonDefaults.SerializerOptions);

        if (Array.IndexOf(payload, NewLine) >= 0)
        {
            throw new InvalidOperationException(
                "A JSON-RPC message must not contain an embedded newline; the peer would read it as two messages.");
        }

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _output.WriteAsync(new[] { NewLine }.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Splits the first newline-terminated line off <paramref name="buffer"/>, leaving the
    /// remainder for the next read.
    /// </summary>
    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var position = buffer.PositionOf(NewLine);
        if (position is null)
        {
            line = default;
            return false;
        }

        line = buffer.Slice(0, position.Value);
        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));

        return true;
    }

    private static JsonRpcMessage Deserialize(in ReadOnlySequence<byte> line)
    {
        var jsonReader = new Utf8JsonReader(line);

        return JsonSerializer.Deserialize<JsonRpcMessage>(ref jsonReader, JsonDefaults.SerializerOptions)
            ?? throw new InvalidDataException("Could not deserialize the JSON-RPC message.");
    }

    public async ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        await _reader.CompleteAsync().ConfigureAwait(false);

        if (!_ownsStreams)
        {
            return;
        }

        await _input.DisposeAsync().ConfigureAwait(false);

        if (!ReferenceEquals(_input, _output))
        {
            await _output.DisposeAsync().ConfigureAwait(false);
        }
    }
}
