---
id: BRIDGE-000004-FIX-001
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T22:20:00+03:00
updated: 2026-11-24T22:20:00+03:00
base: source
branch: task/BRIDGE-000004-exchange-package
commit: 798dfb1c135f3ee5972b4ec8fdacbcba1aec7164
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/6
related: [BRIDGE-000004, BRIDGE-000004-FIX-001]
---

# BRIDGE-000004-FIX-001 — recursive package file integrity check

## 1. Итог

**FIX-001 выполнен.**

`ExchangePackageReader` теперь проверяет не только файлы верхнего уровня, а весь
регулярный состав пакета рекурсивно:

- nested regular files сравниваются с `manifest.Files` и `checksums.sha256`;
- незаявленный файл даёт `EMB-PKG-019 ExtraFile`;
- отсутствующий заявленный файл даёт `EMB-PKG-012 MissingFile`;
- заявленный nested file проверяется по размеру и SHA-256;
- symlink/reparse point не обходится и диагностируется как `EMB-PKG-021 UnsupportedLink`.

Формат manifest/checksums не менялся; Writer, `BoardSnapshotJson` и `Attachment`/ZIP
не затрагивались.

## 2. Алгоритм рекурсивной проверки

Добавлен внутренний обход `CollectRegularFiles`:

1. Начинается с package root.
2. Для каждого каталога вызывается нерекурсивный `Directory.GetFileSystemEntries`.
3. Для каждой записи читается `FileAttributes`.
4. `FileAttributes.ReparsePoint`:
   - запись не обходится;
   - добавляется `PackageDiagnosticCodes.UnsupportedLink`;
   - для directory link рекурсия не выполняется, для file link файл не читается.
5. Directory добавляется в стек обхода.
6. Regular file преобразуется в relative POSIX path (`/`) и добавляется в фактический
   набор.
7. `visitedDirectories` защищает от повторного обхода и циклов.
8. При ошибке доступа к каталогу/записи выдаётся `InvalidPath`.

Проверяемый набор:

```text
expected = { manifest.json } ∪ manifest.Files
actual   = recursive regular files − checksums.sha256
```

Проверки:

- `actual` содержит всё из `expected` иначе `MissingFile`;
- `actual` не содержит ничего сверх `expected` иначе `ExtraFile`;
- checksums покрывают ровно `expected`;
- каждый `manifest.Files` entry проверяется по размеру и SHA-256;
- каждый checksum entry проверяется по SHA-256;
- `checksums.sha256` не покрывает сам себя.

## 3. Вложенные файлы

Текущая MVP-политика не запрещает nested paths: `ExchangePackageValidator` и Reader
допускают дополнительные регулярные файлы, если они:

- безопасны как relative POSIX path;
- присутствуют в `manifest.Files`;
- присутствуют в `checksums.sha256`;
- совпадают по размеру и SHA-256.

`Reader_VerifiesDeclaredNestedFile` проверяет `extra/data.bin`: файл добавляется
в manifest и checksums, reader успешно читает пакет. Это не attachment API и не
генерация attachments — только корректная проверка уже заявленного файла.

## 4. Symlink / reparse point

Directory/file reparse points не обходятся. Это исключает выход за package root и
бесконечную рекурсию. Неожиданная ссылка внутри пакета делает пакет невалидным.

Тест `Reader_RejectsNestedDirectoryLink` добавлен, но на текущем Windows-хосте
создание directory symlink завершилось `UnauthorizedAccessException` (нет
соответствующей привилегии). Тест в этом окружении завершается без выполнения
symlink-сценария; это ограничение зафиксировано здесь, а не выдаётся за проверенный
результат. Код обхода reparse points реализован независимо от возможности создать
ссылку в тесте.

## 5. Новые тесты

| Тест | Результат |
|---|---|
| `Reader_RejectsUnlistedNestedFile` | пройден: `ExtraFile`, snapshot не возвращён |
| `Reader_VerifiesDeclaredNestedFile` | пройден: nested file с manifest/checksum |
| `Reader_RejectsNestedDirectoryLink` | не выполнен: symlink недоступен на хосте |

Существующие тесты Writer/Reader, tamper, security, determinism и snapshot validation
не изменялись и проходят.

## 6. Build и тесты

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release — Core.Tests 69/69, Altium.Tests 65/65, всего 134/134
```

Ранее было 66 Core-тестов; добавлены 3 сценария.

## 7. Изменённые файлы

| Файл | Изменение |
|---|---|
| `src/EcadMcadBridge.Core/Packaging/ExchangePackageReader.cs` | Рекурсивный inventory, reparse/extra/missing проверки |
| `src/EcadMcadBridge.Core/Packaging/PackageDiagnostic.cs` | `EMB-PKG-021 UnsupportedLink` |
| `tests/EcadMcadBridge.Core.Tests/ExchangePackageTests.cs` | nested unlisted/declared/link tests |
| `.agent/ARCHITECTURE.md`, `.agent/PROJECT_CONTEXT.md` | Описание recursive integrity и reparse policy |

## 8. Оставшиеся ограничения

- Symlink/reparse runtime test не выполнен на текущем Windows-хосте из-за отсутствия
  привилегии создания symlink; требуется хост с соответствующей возможностью.
- Nested files поддерживаются как проверяемые regular files, но attachments API,
  ZIP и генерация вложений не реализованы.
- Reader не следует symlink/reparse point and does not read outside the package root.

## 9. Commit / PR

- Commit реализации:
  `798dfb1c135f3ee5972b4ec8fdacbcba1aec7164`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/6
  (открыт, не слит).
- Ветка: `task/BRIDGE-000004-exchange-package`, база `source`.
- Force-push, rebase и merge не выполнялись.
