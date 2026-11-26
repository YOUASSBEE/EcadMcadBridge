---
id: BRIDGE-000038
type: report
status: done
from: local-agent
to: operator
created: 2026-11-27T00:57:13+03:00
updated: 2026-11-27T00:57:13+03:00
base: source
branch: agent/BRIDGE-000038-report
commit: 6dcff9bdf8dcfc57e9c637c4a0ce91b296bf34f2
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/28
related: [BRIDGE-000037]
---

# BRIDGE-000038 — GitHub MCP permission recovery и merge PR #28

## 1. Причина первоначальной ошибки 403

Предыдущий вызов `merge_pull_request` вернул `403 Forbidden: Resource not accessible by
integration`. Это стандартный ответ GitHub, когда используемая GitHub App integration
не имеет права `Pull requests: write` на репозиторий (уровень installation / токена),
хотя чтение репозитория через ту же интеграцию доступно.

В каталоге GitHub MCP нет action для просмотра прав текущей интеграции; диагностика
выполнялась по доступным action (`get_me`, `pull_request_read`, `merge_pull_request`)
и публичным read-only endpoint GitHub. Обход через shell, неизвестные API, чужие токены
или альтернативные credentials не применялся. Повторный вызов того же разрешённого MCP action
после предоставления прав завершился успешно — значит требуемое разрешение
`Pull requests: write` теперь действует.

## 2. Проверка PR перед merge (Stage B)

| Параметр | Значение |
|---|---|
| PR | #28 — https://github.com/YOUASSBEE/EcadMcadBridge/pull/28 |
| State / draft / merged | `open` / `false` / `false` |
| Base | `source @ ea817c9b1e52b6a86e5eb541cf814a578887a7d5` |
| Head | `330e13ae1c7b991acd24c5c9623ba67b846ff0a9` (совпадает с BRIDGE-000037, новых коммитов нет) |
| `mergeable` / `mergeable_state` | `true` / `clean` |
| Новые review threads / комментарии | 0 / 0 |
| Текущий `refs/heads/source` до merge | `ea817c9b1e52b6a86e5eb541cf814a578887a7d5` |

type=approval не требовался: BRIDGE-000037 дал verdict `ready_for_merge_review`,
пользователь явно разрешил merge.

## 3. Merge (Stage C)

- Вызван GitHub MCP action `merge_pull_request` с `merge_method: "merge"`
  (не squash, не rebase) и `expectedHeadSha: 330e13ae1c7b991acd24c5c9623ba67b846ff0a9`.
- Ответ GitHub: `merged: true`, `message: "Pull Request successfully merged"`.
- **Merge commit SHA: `5da53446866f12f227953a03321c7af48489920c`**.
- Родители merge commit: `ea817c9b1e52b6a86e5eb541cf814a578887a7d5` и
  `330e13ae1c7b991acd24c5c9623ba67b846ff0a9` — обычный merge commit, не переписывание истории.
- Повторных попыток merge не потребовалось; branch protection и settings не изменялись.

## 4. Post-merge verification (Stage D)

| Проверка | Результат |
|---|---|
| PR state | `closed` |
| PR merged | `true` |
| `merged_at` | `2026-11-26T21:56:39Z` |
| `merged_by` | `YOUASSBEE` |
| `merge_commit_sha` (PR metadata) | `5da53446866f12f227953a03321c7af48489920c` |
| `refs/heads/source` после merge | `5da53446866f12f227953a03321c7af48489920c` |
| PR #28 больше не открыт | подтверждено |

Кратковременно публичный неавторизованный API отдавал кэшированные значения
`state=open` / старый `source` SHA; повторный запрос с обходом кэша подтвердил
`closed` / `merged=true` и новый `source` HEAD, равный merge commit.
Source branch `task/BRIDGE-000035-top-only-smoke` не удалялась.

## 5. Подтверждение границ изменений

- Production-код, тесты, SDK, ADR, D-024 и fail-closed gate не изменялись.
- Production Bottom не активирован; D-024 остаётся contract-only.
- V-16 остаётся `unverified`; library footprint/body identity не доказана.
- PR #28 содержал только Markdown-отчёты; его содержимое после BRIDGE-000037 не менялось.
- Altium и SOLIDWORKS не запускались; пользовательский PCB не открывался.
- Настоящий отчёт размещён в отдельной ветке `agent/BRIDGE-000038-report`, созданной от
  обновлённого `source`; в head branch PR #28 новые коммиты не создавались.

## 6. Итог

**Успех подтверждён GitHub:** PR #28 merged в `source` обычным merge commit
`5da53446866f12f227953a03321c7af48489920c`; `source` указывает на этот commit.
Оставшихся obstacles нет.

Отчёт создан коммитом `6dcff9bdf8dcfc57e9c637c4a0ce91b296bf34f2`; финальная правка front matter выполняется отдельным metadata-коммитом.
