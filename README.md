# SIPL на C#

Проєктна робота з «Теорії програмування»: парсер, інтерпретатор і візуалізація дерева виводу
для мови SIPL.

## Завдання

| ID | Завдання | Бали | Де |
|---|---|---:|---|
| CORE-01 | Граматика | 0.5 | `grammar/`, `docs/grammar.md`, `tests/grammar/` |
| CORE-02 | Парсер і AST з позиціями | 1.5 | `src/Sipl.Core/Syntax`, `src/Sipl.Core/Ast` |
| CORE-03 | Інтерпретатор | 2.0 | `src/Sipl.Core/Runtime` |
| CORE-04 | Події виконання і trace | 1.0 | `src/Sipl.Core/Runtime/Events.cs` |
| CORE-05 | CLI і JSON | 1.0 | `src/Sipl.Cli`, `docs/cli.md`, `docs/schemas` |
| CORE-06 | Conformance-тести | 1.0 | `conformance/`, `docs/conformance.md` |
| VIS-01 | Дерево виводу: text, JSON, DOT, SVG | 1.5 | `src/Sipl.Core/Visualization/TreeRenderer.cs` |
| VIS-02 | AST і порівняння з деревом виводу | 1.0 | `src/Sipl.Core/Visualization/TreeComparison.cs` |
| REL-01 | CI | 0.5 | `.github/workflows/ci.yml` |
| REL-02 | Релізи, SemVer, CHANGELOG | 0.5 | `.github/workflows/release.yml`, `CHANGELOG.md` |

## Запуск

Потрібен .NET 8 SDK.

```bash
dotnet build
dotnet test
```
