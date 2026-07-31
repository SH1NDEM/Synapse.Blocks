using System.Text.Json.Serialization;
using Synapse.Blocks.Models;

namespace Synapse.Blocks.Serialization;

// Генерация сериализатора во время сборки ускоряет чтение уровней и не ломается
// после удаления неиспользуемого reflection-кода в Release-версии WebAssembly.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<LevelDefinition>))]
[JsonSerializable(typeof(HashSet<Guid>))]
[JsonSerializable(typeof(BlockProgram))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
