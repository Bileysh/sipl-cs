# Команди sipl

```
sipl <команда> FILE [опції]
```

`FILE` — файл з програмою або `-`, щоб прочитати програму зі стандартного вводу.

| Команда | Що робить | Опції |
|---|---|---|
| `parse` | розбирає програму й показує AST (JSON) | `--json`, `--no-spans` |
| `check` | перевіряє синтаксис | `--json` |
| `run` | виконує програму, друкує кінцевий стан | `--set X=1 Y=2`, `--max-steps N`, `--json` |
| `trace` | виконує програму й показує кожен крок | `--set`, `--max-steps`, `--json` |

Приклади:

```bash
sipl run examples/power.sipl --set X=2 Y=10
sipl trace examples/gcd.sipl --set A=12 B=8
sipl run examples/power.sipl --set X=2 Y=10 --json
```

## Коди завершення

| Код | Значення |
|---|---|
| 0 | успіх |
| 1 | синтаксична помилка |
| 2 | неправильні аргументи |
| 3 | помилка виконання: змінна без значення (R001) або ділення на нуль (R002) |
| 4 | перевищено ліміт кроків (R003) |
| 5 | не вдалося прочитати файл |

## JSON

З опцією `--json` команди `parse`, `check`, `run` і `trace` друкують один об'єкт однакової
структури (схема — [schemas/cli.schema.json](schemas/cli.schema.json)):

```json
{
  "schema": "sipl.cli/1",
  "version": "0.3.0",
  "command": "run",
  "file": "examples/power.sipl",
  "ok": true,
  "exit_code": 0,
  "diagnostics": [],
  "result": { "status": "ok", "state": { "X": 2, "Y": 0, "R": 1024 }, "steps": 54 }
}
```

* `exit_code` збігається з кодом завершення процесу, `ok` — те саме, що `exit_code == 0`.
* `diagnostics` — помилки: `code`, `message`, `span` (рядок і колонка рахуються з 1), `hint`,
  `expected`.
* `result`:
  * для `parse` — `{"ast": ...}`;
  * для `run` — `status`, `state`, `steps`;
  * для `trace` — `{"schema": "sipl.trace/1", "events": [...], "result": ...}`;
  * для `check` і при синтаксичній помилці — `null`.

Події trace ([schemas/trace-event.schema.json](schemas/trace-event.schema.json)):

| `type` | Додаткові поля |
|---|---|
| `ProgramStarted` | `state` |
| `BeforeStatement`, `AfterStatement` | `step`, `node_id`, `kind`, `span` |
| `ConditionEvaluated` | `statement_id`, `value`, `node_id`, `span` |
| `VariableChanged` | `name`, `old` (`null`, якщо значення ще не було), `new` |
| `ProgramFinished` | `status`, `state`, `steps`, `diagnostic` |

`node_id` — номер вузла AST з `sipl parse --json`, тож подію можна зіставити з вузлом дерева й
місцем у коді.

Формат вважається стабільним: поля й коди не змінюються в межах `sipl.cli/1`.
