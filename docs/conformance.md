# Conformance-тести

`conformance/cases/` — 56 програм з очікуваним результатом. Тести не залежать від цієї реалізації:
ними можна перевірити будь-яку програму, що виконує SIPL.

Кожен тест — це два файли з однаковою назвою: `NAME.sipl` (програма) і `NAME.json` (очікування):

```json
{
  "description": "ділення на нуль",
  "input": { "a": 5 },
  "expect": {
    "status": "runtime_error",
    "state": { "a": 5 },
    "error": { "code": "R002", "line": 2, "column": 6 }
  }
}
```

| Поле | Значення |
|---|---|
| `description` | опис |
| `input` | вхідні значення змінних (необов'язкове) |
| `max_steps` | ліміт кроків (необов'язкове, за замовчуванням 100000) |
| `expect.status` | `ok`, `syntax_error`, `runtime_error` або `step_limit` |
| `expect.state` | точний кінцевий стан |
| `expect.state_subset` | перевіряються лише перелічені змінні |
| `expect.error` | код помилки, рядок і колонка |
| `expect.ast` | очікуване AST без позицій (як у `sipl parse --json --no-spans`) |

Перевіряються лише ті поля, які записані в тесті.

## Запуск

```bash
sipl conformance                                   # ця реалізація
sipl conformance --impl "КОМАНДА"                  # будь-яка інша реалізація
sipl conformance conformance/cases -k syntax/      # лише частина тестів
```

Реалізація, яку перевіряють через `--impl`, запускається як окремий процес:

```
КОМАНДА run FILE --json --max-steps N --set X=1 Y=2
КОМАНДА parse FILE --json --no-spans
```

У stdout вона має надрукувати JSON з полями `result.status`, `result.state`, `result.ast` і
`diagnostics[0]` (`code`, `span.line`, `span.column`). Якщо `result.status` немає, статус
визначається за кодом завершення: 0 — ok, 1 — syntax_error, 3 — runtime_error, 4 — step_limit.

Очікувані значення в тестах пораховані вручну, а не скопійовані з виводу програми.
