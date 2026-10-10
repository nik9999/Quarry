namespace Quarry.Ingestion.Chunking;

using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Quarry.Core.Abstractions;
using Quarry.Core.Documents;

/// <summary>
/// Стратегия семантического разбиения исходного кода C# на основе синтаксического дерева Roslyn (AST).
/// Сохраняет целостность классов, методов, интерфейсов и записей с метаданными символов.
/// </summary>
public sealed class CodeFileChunker : IChunker
{
    private readonly int _maxChunkSize;

    public string Strategy => "code";
    public int MaxChunkSize => _maxChunkSize;

    public CodeFileChunker(int maxChunkSize = 1500)
    {
        if (maxChunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxChunkSize), "Max chunk size must be greater than zero.");
        }

        _maxChunkSize = maxChunkSize;
    }

    public async IAsyncEnumerable<DocumentChunk> ChunkAsync(
        Document document,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.Text))
        {
            yield break;
        }

        await Task.Yield();

        var text = document.Text;
        var tree = CSharpSyntaxTree.ParseText(text, cancellationToken: cancellationToken);
        var root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);

        var nodesToChunk = new List<(SyntaxNode Node, string Kind, string Name, string? ParentName)>();

        // Collect top-level type declarations
        var typeDeclarations = root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>()
            .ToList();

        if (typeDeclarations.Count == 0)
        {
            // Fallback for files without types (top-level statements, small scripts)
            var recursiveFallback = new RecursiveCharacterChunker(_maxChunkSize, _maxChunkSize / 10);
            await foreach (var chunk in recursiveFallback.ChunkAsync(document, cancellationToken).ConfigureAwait(false))
            {
                yield return chunk;
            }
            yield break;
        }

        var chunkIndex = 0;

        foreach (var typeDecl in typeDeclarations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var typeName = typeDecl.Identifier.Text;
            var typeKind = typeDecl.Kind().ToString();

            // If the whole type fits inside maxChunkSize, emit it as a single chunk
            if (typeDecl.Span.Length <= _maxChunkSize)
            {
                var start = typeDecl.Span.Start;
                var end = typeDecl.Span.End;
                var chunkText = text[start..end].Trim();

                var meta = new Dictionary<string, string>
                {
                    ["symbol_name"] = typeName,
                    ["symbol_kind"] = typeKind
                };

                yield return ChunkBuilder.Create(
                    document,
                    chunkIndex++,
                    chunkText,
                    start,
                    end,
                    meta);

                continue;
            }

            // Otherwise, chunk individual members (methods, properties, nested types)
            var members = typeDecl.ChildNodes().OfType<MemberDeclarationSyntax>().ToList();
            if (members.Count == 0)
            {
                // Large type with no members or comments
                var start = typeDecl.Span.Start;
                var end = typeDecl.Span.End;
                yield return ChunkBuilder.Create(
                    document,
                    chunkIndex++,
                    text[start..end].Trim(),
                    start,
                    end,
                    new Dictionary<string, string>
                    {
                        ["symbol_name"] = typeName,
                        ["symbol_kind"] = typeKind
                    });
                continue;
            }

            foreach (var member in members)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var memberName = GetMemberIdentifier(member);
                var memberKind = member.Kind().ToString();
                var start = member.Span.Start;
                var end = member.Span.End;

                if (member.Span.Length <= _maxChunkSize)
                {
                    var chunkText = text[start..end].Trim();
                    var meta = new Dictionary<string, string>
                    {
                        ["symbol_name"] = memberName,
                        ["symbol_kind"] = memberKind,
                        ["parent_symbol"] = typeName
                    };

                    yield return ChunkBuilder.Create(
                        document,
                        chunkIndex++,
                        chunkText,
                        start,
                        end,
                        meta);
                }
                else
                {
                    // Member is larger than maxChunkSize, slice it with overlap
                    var pos = start;
                    var step = _maxChunkSize - (_maxChunkSize / 10);

                    while (pos < end)
                    {
                        var sliceEnd = Math.Min(pos + _maxChunkSize, end);
                        var sliceText = text[pos..sliceEnd].Trim();

                        var meta = new Dictionary<string, string>
                        {
                            ["symbol_name"] = memberName,
                            ["symbol_kind"] = memberKind,
                            ["parent_symbol"] = typeName,
                            ["is_subpart"] = "true"
                        };

                        yield return ChunkBuilder.Create(
                            document,
                            chunkIndex++,
                            sliceText,
                            pos,
                            sliceEnd,
                            meta);

                        if (sliceEnd >= end)
                        {
                            break;
                        }

                        pos += step;
                    }
                }
            }
        }
    }

    private static string GetMemberIdentifier(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax m => m.Identifier.Text,
            PropertyDeclarationSyntax p => p.Identifier.Text,
            ConstructorDeclarationSyntax c => c.Identifier.Text,
            BaseTypeDeclarationSyntax t => t.Identifier.Text,
            FieldDeclarationSyntax f when f.Declaration.Variables.Count > 0 => f.Declaration.Variables[0].Identifier.Text,
            _ => member.Kind().ToString()
        };
    }
}
