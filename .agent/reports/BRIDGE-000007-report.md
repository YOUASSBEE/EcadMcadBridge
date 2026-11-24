---
id: BRIDGE-000007
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-24T22:35:00+03:00
updated: 2026-11-24T22:35:00+03:00
base: source
branch: agent/BRIDGE-000007-altium-package-export
commit: db904b80df2c2216d16f8b539ad5f47450792fd8
pr: https://github.com/YOUASSBEE/EcadMcadBridge/pull/7
related: [BRIDGE-000003, BRIDGE-000004, BRIDGE-000007, D-005, D-015, D-016]
---

# BRIDGE-000007 — Altium Exchange Package Export

## 1. Итог

**BRIDGE-000007 выполнен.**

Команда `EcadMcadBridge:ExportBoardSnapshot` теперь формирует Exchange Package
через существующий Core `ExchangePackageWriter`:

```text
<package-root>/
└── <package-guid>/
    ├── manifest.json
    ├── board.snapshot.json
    └── checksums.sha256
```

Схемы `BoardSnapshot` и Package не менялись. `ExportSmokeSnapshot` сохранён;
изменён только общий номер версии Add-in.

## 2. V-05 — source document state/path

Проверено в Altium Designer `26.12.2.9` на рабочей копии
`board-with-cutout.PcbDoc`.

| Сценарий | Фактический результат | Экспорт |
|---|---|---|
| Сохранённая плата без изменений | `GetFileName()` — absolute path; `GetModified() == false` | успешен |
| В память добавлен track, без сохранения | `GetModified() == true`; document tab содержит `*` | отклонён: `E_SOURCE_MODIFIED` |
| После `DoFileSave` | `DoFileSave` вернул `true`; `GetModified() == false` | успешен |
| Новый PCB без сохранения | `GetFileName()` вернул относительное `PCB1.PcbDoc` | отклонён: `E_SOURCE_STATE` |

API-подтверждение внесено в ledger как C-89; V-05 закрыт.

`documentSha256` вычисляется из файла `.PcbDoc` после проверки стабильности
размера и `LastWriteTimeUtc`; hash snapshot и hash сериализованного BoardSnapshot
для этой цели не используются.

## 3. Реализация экспорта

Последовательность `ExportBoardSnapshot`:

1. `AltiumDocumentStateReader` проверяет `GetKind/GetFileName/GetModified`.
2. Требуется PCB с absolute path и `modified=false`.
3. `AltiumBoardSnapshotAdapter.Read` извлекает `BoardSnapshot`.
4. Source state проверяется повторно: путь не изменился, `modified=false`.
5. `PackageSourceMetadataReader` вычисляет стабильный SHA-256 исходного `.PcbDoc`.
6. `PackageRootResolver` выбирает package root.
7. Формируются `PackageWriteOptions` с новым GUID, UTC timestamp, producer `0.3.0`,
   source `altium` + document SHA.
8. `ExchangePackageWriter.Write` атомарно публикует пакет.
9. Ошибки writer и диагностики `EMB-PKG-*` логируются, успех показывается только
   при `IsSuccess=true`.

Автоматическое сохранение и изменение `.PcbDoc` отсутствуют.

## 4. Package root

- `ECAD_MCAD_BRIDGE_PACKAGE_ROOT` — absolute path, если задан.
- Иначе `%LOCALAPPDATA%\EcadMcadBridge\Packages`.
- Относительный env path отклоняется.
- Fallback-каталог проверен smoke-тестом.
- Повторный экспорт создаёт новый GUID-каталог; старый пакет не перезаписывается.

## 5. Metadata

- `Producer.Component` = `EcadMcadBridge.Altium`;
- `Producer.Version` = `0.3.0`;
- `Source.Cad` = `altium`;
- `Source.DocumentSha256` = SHA-256 исходного `.PcbDoc`;
- capabilities: `board-outline`, `component-placement`, `layers`, `nets`,
  `pad-stacks`, `pads`.
- `PackageId` — новый `Guid.NewGuid()`;
- `CreatedAtUtc` — `DateTimeOffset.UtcNow`.

## 6. Smoke

Основная плата: `board-with-cutout.PcbDoc`, AD `26.12.2.9`.

### 6.1 Сохранённая неизменённая плата

- исходный SHA-256: `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753`;
- SHA-256 после экспорта: тот же;
- Package ID: `00a079eb-5856-48ff-89ce-84744b0fa76d`;
- manifest `documentSha256`: `ed624c68dc315a3e022e96979c5a7b2b32f5bbad3db722be0cca2eb9c8647753`;
- `ExchangePackageReader`: `success=True`, errors 0, warnings 28;
- snapshot: 3 contours (outer + 2 Board Cutout Hole), 17 layers, 73 components,
  224 pads, 57 nets;
- adapter warnings: 8, package warnings: 28.

### 6.2 Повторный экспорт

- второй Package ID: `8eebbe3f-17b2-4820-a415-d831823ade2a`;
- первый пакет остался на месте;
- `documentSha256` также `ed624c68…`.

### 6.3 Несохранённые изменения

В память добавлен track; tab получил `*`; `GetModified()==true`. Экспорт:
`E_SOURCE_MODIFIED`, пакет не создан, файл не сохранён автоматически.

### 6.4 Сохранённая изменённая плата

После `DoFileSave`: `save=true, modified=false`; рабочий файл SHA-256
`73F7E7A9D3F88B5114BF86F0B71AB2F051039BA15C9EA73C25C399306F583958` до и после
экспорта; Package ID `b0fb5650-a928-4fa1-b7f8-91abb2f5b5c1`; reader success.
Это подтверждает, что `documentSha256` соответствует текущему сохранённому файлу,
а не snapshot.

### 6.5 Новый PCB

Новый `PCB1.PcbDoc` без сохранения: `GetFileName` относительный, экспорт
отклонён с `E_SOURCE_STATE`; пакет не создан.

## 7. Build и тесты

```text
dotnet build -c Release — 0 предупреждений, 0 ошибок
dotnet test -c Release — Core.Tests 78/78, Altium.Tests 65/65, всего 143/143
```

Добавлены Core-тесты для root resolver, stable file hash и source metadata
(9 новых тестов).

## 8. Изменённые файлы

Новые:

```text
src/EcadMcadBridge.Altium/Application/AltiumDocumentState.cs
src/EcadMcadBridge.Core/Packaging/PackageFileHasher.cs
src/EcadMcadBridge.Core/Packaging/PackageRootResolver.cs
tests/EcadMcadBridge.Core.Tests/PackageSourceTests.cs
```

Изменены:

```text
src/EcadMcadBridge.Altium/Application/ExportBoardSnapshotHandler.cs
src/EcadMcadBridge.Altium/Adapter/AltiumBoardSnapshotAdapter.cs
src/EcadMcadBridge.Altium/Application/ExportSmokeSnapshotHandler.cs
src/EcadMcadBridge.Altium/Bootstrap/PluginFactory.cs
src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.ins
src/EcadMcadBridge.Altium/Installation/EcadMcadBridge.rcs
.agent/SDK_API_LEDGER.md
.agent/ARCHITECTURE.md
.agent/PROJECT_CONTEXT.md
README.md
```

## 9. Ограничения

- Экспорт возможен только для сохранённого PCB с `GetModified()==false`.
- Автоматическое сохранение и закрытие документа не реализованы.
- `ExportSmokeSnapshot` проверен отдельно: `SNAPSHOT_OK`, 73 components,
  140 outline points, warnings 0; назначение команды не менялось.
- ZIP, attachments, импорт, ChangeSet/ECO и SOLIDWORKS Add-in не входят в scope.
- `documentSha256` — хеш исходного `.PcbDoc`, но не криптографическая подпись.
- В лог не пишутся absolute user paths; пользователю путь пакета показывается.

## 10. Commit / PR

- Commit реализации и документации:
  `db904b80df2c2216d16f8b539ad5f47450792fd8`.
- PR: https://github.com/YOUASSBEE/EcadMcadBridge/pull/7
  (открыт, не слит).
- Ветка: `agent/BRIDGE-000007-altium-package-export`, база `source`.
- Force-push, rebase и merge не выполнялись.
