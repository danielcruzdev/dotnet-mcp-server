# Snippets C# — Referencia Rapida
## Injecao de Dependencia (.NET 8+)
```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<IMeuServico, MeuServico>();
builder.Services.AddScoped<IRepositorio, Repositorio>();
builder.Services.AddHttpClient<IMeuClient, MeuClient>();
var app = builder.Build();
app.MapGet("/", () => "Hello World!");
app.Run();
```
## LINQ — Operacoes mais usadas
```csharp
var numeros = Enumerable.Range(1, 10);
var pares   = numeros.Where(n => n % 2 == 0);
var dobros  = numeros.Select(n => n * 2);
var soma    = numeros.Sum();
var agrupado = numeros.GroupBy(n => n % 3);
var ordenado = numeros.OrderByDescending(n => n).Take(3);
```
## Channel — produtor/consumidor
```csharp
var channel = Channel.CreateUnbounded<string>();
// Produtor
await channel.Writer.WriteAsync("mensagem 1");
channel.Writer.Complete();
// Consumidor
await foreach (var item in channel.Reader.ReadAllAsync())
    Console.WriteLine(item);
```
## CancellationToken pattern
```csharp
public async Task ProcessarAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await FazerAlgoAsync(ct);
        await Task.Delay(TimeSpan.FromSeconds(1), ct);
    }
}
```
## IAsyncDisposable
```csharp
public sealed class Recurso : IAsyncDisposable
{
    private readonly Stream _stream = File.OpenRead("dados.bin");
    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
    }
}
// uso
await using var recurso = new Recurso();
```
## System.Text.Json — serializacao customizada
```csharp
var options = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
var json = JsonSerializer.Serialize(meuObjeto, options);
var obj  = JsonSerializer.Deserialize<MeuTipo>(json, options);
```
