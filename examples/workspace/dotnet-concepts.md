# Conceitos .NET — Anotações de Estudo

## async / await

`async` e `await` permitem escrever código assíncrono de forma síncrona.
O `await` libera a thread atual enquanto aguarda uma operação de I/O.

```csharp
public async Task<string> BuscarDadosAsync(string url)
{
    using var client = new HttpClient();
    return await client.GetStringAsync(url); // não bloqueia a thread
}
```

**Regras importantes:**
- Um método `async` deve retornar `Task`, `Task<T>` ou `ValueTask<T>`.
- Nunca use `.Result` ou `.Wait()` em código async — risco de deadlock.
- Prefira `ConfigureAwait(false)` em bibliotecas.

---

## Records (C# 9+)

Records são tipos por valor com igualdade estrutural e imutabilidade por padrão.

```csharp
public record Produto(string Nome, decimal Preco);

var p1 = new Produto("Caneta", 2.50m);
var p2 = new Produto("Caneta", 2.50m);

Console.WriteLine(p1 == p2); // True — compara por valor
Console.WriteLine(p1);       // Produto { Nome = Caneta, Preco = 2.50 }
```

**Quando usar:**
- DTOs e objetos de transferência imutáveis.
- Resultados de operações (em vez de tuplas).

---

## Nullable Reference Types (C# 8+)

Ativados com `<Nullable>enable</Nullable>` no `.csproj`.

```csharp
string nome = "João";   // nunca null — compilador avisa se tentar atribuir null
string? apelido = null; // explicitamente nullable

void Imprimir(string? texto)
{
    if (texto is not null)
        Console.WriteLine(texto.ToUpper()); // sem warning
}
```

---

## Span\<T\> e Memory\<T\>

Permitem trabalhar com fatias de arrays/strings sem alocações extras.

```csharp
ReadOnlySpan<char> texto = "Hello, World!";
var saudacao = texto[..5]; // "Hello" — sem nova string alocada
```

**Uso típico:** parsers, serialização de alta performance.

---

## Pattern Matching (C# 7–11)

```csharp
object obj = 42;

string descricao = obj switch
{
    int n when n > 0 => $"Positivo: {n}",
    int n            => $"Negativo ou zero: {n}",
    string s         => $"Texto: {s}",
    _                => "Outro tipo"
};
```

---

## Referências

- [Microsoft Docs — async/await](https://learn.microsoft.com/dotnet/csharp/asynchronous-programming/)
- [Records em C#](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record)
- [Nullable reference types](https://learn.microsoft.com/dotnet/csharp/nullable-references)

