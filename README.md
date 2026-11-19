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
dotnet run --project src/Sipl.Cli -- run examples/power.sipl --set X=2 Y=10
```

## Приклади

```bash
sipl run   examples/power.sipl --set X=2 Y=10     # виконати
sipl trace examples/gcd.sipl --set A=12 B=8       # показати кожен крок
sipl check examples/errors/missing_else.sipl      # перевірити синтаксис
sipl parse examples/power.sipl                    # AST
sipl tree  examples/power.sipl -f svg -o tree.svg # дерево виводу
sipl tree  examples/power.sipl --compare -f html -o compare.html   # дерево виводу і AST поруч
```

```text
$ sipl run examples/power.sipl --set X=2 Y=10
Готово, кроків: 54. Кінцевий стан:
  X = 2
  Y = 0
  R = 1024
```

```text
$ sipl check examples/errors/missing_else.sipl
examples/errors/missing_else.sipl:4:27: error E002: неочікувана лексема ';'; очікувалось: '*', '/', '%', '+', '-', 'else'
     4 |   if x > 0 then x := x - 1;
       |                           ^
  підказка: у SIPL if обов'язково має else (якщо нічого не треба: else skip)
```

Усі команди й формат JSON описано в [docs/cli.md](docs/cli.md).
