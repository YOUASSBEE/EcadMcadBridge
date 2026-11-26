---
id: BRIDGE-000019
type: report
status: ready_for_review
from: local-agent
to: operator
created: 2026-11-26T04:00:00+03:00
updated: 2026-11-26T05:00:00+03:00
base: source
branch: agent/BRIDGE-000019-board-substrate-assembly
commit: TBD
pr: TBD
related: [BRIDGE-000019, BRIDGE-000018]
---

# BRIDGE-000019 — Board Substrate Export and Assembly Integration

## 1. База и ветка

- Base `source`: `0ed4a56d9db41e4ce79a91317be49f2920188866`.
- Branch: `agent/BRIDGE-000019-board-substrate-assembly`.
- PR: TBD, base `source`, открыт, merge не выполнялся.
- Runtime: Altium Designer 26.12.2.9; SOLIDWORKS 2026 SP3.2, interop 34.3.2.3.

## 2. Подтверждённый board-only API

SDK 4.0.1 / AD26 `Altium.SDK.Interfaces.dll`:

- `IPCB_ServerInterface.Board3DModelExporter()`;
- `IPCB_Board3DModelExporter.ExportBoard3DModel(string, TBoard3DModelExportFormat, IPCB_Board, IProject)`
  (типизированный helper; формат `eBoard3DModelExportFormat_Step`);
- `SetState_ExportComponentOptions`, `SetState_ExportModelsOption`,
  `SetState_ExportHolesOption`, `SetState_ExportCopperOption`,
  `SetState_ExportFoldedBoard`, `SetState_ComponentSuffixType`,
  `SetState_IncludeMechanicalPadHoles`, `SetState_IncludeElectricalPadHoles`,
  `SetState_IncludeFreePadHoles`, `SetState_IncludeCoverLayer`,
  `SetState_ExportPadAndViaBarrelsOnly`;
- `IPCB_Board.SelectedObjectsCount()`,
  `SelectedObjects_BeginUpdate()/Clear()/EndUpdate()`.

В SDK нет переключателя «экспортировать без компонентов»: единственный
board-only режим — `SelectedComponents` при проверенном пустом selection.
`SelectedComponents` без очистки selection экспортировал бы выбранные тела.

## 3. Настройки экспорта

| Параметр | Значение |
|---|---|
| Format | STEP AP214 |
| Components | `SelectedComponents`, selection проверен как 0 |
| Models | `AsStep` |
| Holes | `AllHoles` |
| Pad/via/cavity holes | mechanical/electrical/free = `false` |
| Copper | `CopperNone` |
| Folded board | `false` |
| Component suffix | `None` |
| Cover layer | `false` |
| Pad/via barrels only | `false` |

Сохранённые пользовательские preferences не читаются. После экспорта
проверяются файл, AP214 header, SHA-256 и неизменность `GetModified`.
Если board STEP не создан или не AP214, capability `board-3d-model` не
объявляется, пакет остаётся без board asset.

## 4. Board asset

MiniPC:

- package `1804735d-7c31-47cb-98e3-e50950283c2e`;
- board STEP `13 085` bytes;
- SHA-256 `944d8ba0bf300d1bb717f025c83842534536cc18b7215d0af34adeaf84bb1949`;
- package path `assets/board/944d8ba0…1949.step`;
- board model descriptor `board.model.json`, capability `board-3d-model`;
- после экспорта selection остался пустым, `.PcbDoc` modified state не изменился.

Desktop `board with cutout.PcbDoc` (diagnostic export):

- package `ef26ea5d-8aee-4d7d-99d4-28aac362caab`;
- board STEP `232 025` bytes;
- SHA-256 `fff15249ce86fe87a58207206d3890c285bdfbbe576aecdc378a37e2610a4465`;
- 5 `Board Cutout` regions, 2 cutout contours экспортированы;
- исходный `.PcbDoc` не изменялся.
## 5. Package schema и backward compatibility

- Descriptor `board.model.json`, schema `ecad-mcad-bridge/board-model` 1.0.0.
- Frame: `outer-contour-minimum-xy-top-surface-z0`,
  `positive-altium-x`, `positive-altium-y`, `positive-outward-from-top`,
  `negative-z`; nominal thickness из `BoardProperties.Thickness` (reference only).
- Reader проверяет schema/version, units, frame, безопасный путь
  `assets/board/<lowercase-sha256>.step`, manifest/checksums, SHA-256, size,
  capability и отсутствие лишних board assets. Ошибки:
  `EMB-PKG-027`…`EMB-PKG-030`.
- Пакет без `board.model.json` читается с `BoardModel = null` и прежним
  поведением; manifest schema остаётся 1.0.0.
- Writer не публикует пакет при ошибке board asset; `board-3d-model` без
  `board.model.json` и `board.model.json` без capability отклоняются.

## 6. Измеренная геометрия MiniPC

| Параметр | Значение |
|---|---|
| Solid bodies | 1 |
| Min X/Y/Z | `0, 0, -2.00800208` mm |
| Max X/Y/Z | `175, 155, 0` mm |
| Width × Height × Thickness | `175 × 155 × 2.00800208` mm |
| Nominal thickness | `2.00800208` mm |
| Thickness delta | `4.44e-16` mm |
| Volume | `54 097.58403728` mm³ |
| Expected volume (Outer − Hole) × measured T | `54 097.58419655` mm³ |
| Volume residual | `-0.00015927` mm³ |
| XY bounds residual | `5.2e-7` mm |
| Z top residual | `0` mm |
| Board probe | insideSolid=true |
| Cutout probes | нет Hole contours в outline MiniPC |

Измеренные значения получены из SOLIDWORKS `BoundingBox`/`Volume` в метрах и
переведены в mm; nominal thickness не подменяет измеренную. R2 full-board STEP
имел толщину `2.02800204` mm; board-only export MiniPC даёт `2.00800208` mm.
Причина расхождения не объявляется установленной.

Desktop cutout board (diagnostic, rejected by the full geometry gate):

| Параметр | Значение |
|---|---|
| Solid bodies | 3 (STEP содержит 3 solids) |
| Aggregate bounds | `80 × 55 × 0.79649574` mm |
| Min X/Y/Z | `-34.4, -12, -0.79649574` mm |
| Max X/Y/Z | `45.6, 43, 0` mm |
| Volume | `848.19959918` mm³ |
| Expected volume по неполному outline | `1 083.14487678` mm³ |
| XY bounds residual | `34.4` mm |
| Z top residual | `0` mm |

Snapshot outline этого файла partial: `outlineFallback=1`, `outlinePartial=1`,
`outlineUnmatchedLoops=1`; из 5 Board Cutout regions экспортированы 2. Outer
контур замкнут, но описывает не всю фактическую геометрию STEP, поэтому
XY/volume gate не пройден. Production inspector корректно отклоняет геометрию
(`SW-BRD-008/010`), а `SW-BRD-005` (3 solid body) теперь warning: тела
трактуются как одна substrate part.

## 7. Cutouts

Desktop board, SOLIDWORKS 2026 SP3.2, package `ef26ea5d-8aee-4d7d-99d4-28aac362caab`:

| Probe | Contour | X/Y/Z, mm | Expected | Inside solid |
|---|---|---|---|---|
| material | outer | `8.000, 27.500, -0.398` | true | **true** |
| cutout 1 | `cutout:clip:1` | `29.986, 44.456, -0.398` | false | **false** |
| cutout 2 | `cutout:clip:2` | `46.998, 48.817, -0.398` | false | **false** |

Оба реальных сквозных выреза подтверждены отсутствием материала; контрольная
точка в плате подтвердила solid. Это **не** полная валидация платы: outer loop
snapshot неполный, поэтому XY bounds/volume gate не пройден и smoke `success=false`.
Cutout probes при этом численно воспроизводимы и не основаны на визуальном осмотре.
Алгоритм дополнительно покрыт unit-тестом `TriangleMeshTests` (замкнутый frame
с сквозным отверстием: материал true, отверстие false).

Попытка создать дополнительный контролируемый 100×60 mm fixture с одним
20×10 mm cutout через Altium SDK не сохранилась: документ открылся без
file ownership (`OwnsFile=false`, `SupportsOwnSave=false`), изменения outline и
region не записались на диск. Поэтому fixture не использован и не заявлен.

Проверка cutout выполняется по фактической tessellation импортированного тела
(замкнутый triangle mesh, ray parity), а не по наличию контура в snapshot.
Точка внутри платы должна подтвердить материал; точка внутри каждого
тестируемого сквозного выреза — отсутствие материала.

## 8. Сборка и transforms

MiniPC 20 компонентов (`C1,C16,C17,C18,C19,C93,C97,C102,C104,C105,C108,C109,
C110,C111,C112,C113,C114,C115,C116,C117`):

- 8 уникальных component SLDPRT, 20 component occurrences;
- 1 board occurrence (`board.SLDPRT`, native part);
- всего 21 occurrence;
- board transform: `Tx=0`, `Ty=0`, `Tz=1.00400104` mm;
- измеренная верхняя поверхность платы после transform: `+1.00400104` mm =
  `BoardSnapshot.Thickness/2`;
- board fixed=true, board transform verified, translation residual `0`;
- component transforms совпадают с BRIDGE-000018 в пределах `1e-5` mm.

Board transform строится из измеренных значений:

```text
Tx = -measuredMinX
Ty = -measuredMinY
Tz = BoardSnapshot.Thickness / 2 - measuredTopZ
```

Измеренная толщина не меняется; компоненты не сдвигаются.

Desktop cutout board (`smoke-b3`, `--max-components 1`):

- 1 component SLDPRT + board, 2 occurrences (1 component + 1 board);
- board transform: `Tx=34.4`, `Ty=12`, `Tz=0.39824787` mm, выведен из измеренных
  `MinX=-34.4`, `MinY=-12`, `MaxZ=0`;
- board fixed=true, board transform verified, translation residual `0`;
- после reopen 2/2 occurrences; сборка сохранена и открыта повторно;
- `success=false` только из-за geometry gate (partial outline/volume).

## 9. Save/reopen и cleanup

- `.SLDASM` сохраняется, закрывается, открывается повторно.
- Smoke A: 21/21 occurrences после reopen, transforms verified, cleanup
  `Verified`, `success=true`.
- Smoke B (Desktop cutout board): 2/2 occurrences, board fixed/verified,
  cleanup `Verified`, assembly saved/reopened; `success=false` из-за geometry
  gate неполного outline.
- JSON/assembly SHA:
  - `smoke-a4` JSON `57E96E701F4019BB211EEA4173926A744C21EE75E1C5B771456BF310D7178B92`,
    assembly `965358459E73712DB3176C272D85C9DB2683A1CE94FEE59DACF7DAEF7362AAFA`;
  - `smoke-b3` JSON `F8F2DFDD9AD6A9744EC56E0786BDB95C79BAA28A1EA5813C138BA6029134D3EA`,
    assembly `98FA80D886FC4F7817EAB2CE02D7ED764C86C7030301C1EF12F2C80502850266`.
- Board fixed flag и transform проверяются после reopen отдельно.
- Пользовательские документы baseline не закрываются; no-overwrite guard
  сохранён.

## 10. Исходный PcbDoc

- `C:\Users\Anton\Desktop\board with cutout.PcbDoc` до и после операций:
  SHA-256 `ED624C68DC315A3E022E96979C5A7B2B32F5BBAD3DB722BE0CCA2EB9C8647753`.
- Временная копия `board-with-cutout.PcbDoc` до/после: тот же SHA.
- Все runtime-операции выполнялись на временной копии; исходник не изменялся.

Desktop board использован как первый cutout fixture. Его snapshot outline
оказался partial (`outlineFallback=1`, `outlinePartial=1`,
`outlineUnmatchedLoops=1`; из 5 Board Cutout regions экспортированы 2),
импорт STEP дал 3 solid body, aggregate volume `848.20` mm³ против
`1 083.14` mm³ по неполному outline, XY residual `34.4` mm. Поэтому этот файл
**не** заявлен как полностью валидированная плата; cutout probes подтвердили
отсутствие материала в двух реальных вырезах (раздел 7).

## 11. Сборка и тесты

- `dotnet build -c Release`: 0 warnings / 0 errors.
- `dotnet test -c Release --no-build`: Core `208/208`, Altium `156/156`,
  SolidWorks `75/75`, всего `439/439`.
- `git diff --check`: OK.
- Runtime smoke: MiniPC 20 + board (`smoke-a4`), Desktop cutout board
  (`smoke-b3`, diagnostic), см. разделы 6–9.
- Повторный MiniPC/R3 smoke BRIDGE-000018 не выполнялся: формулы компонентов не
  менялись; component transforms проверены в smoke-a4.

## 12. Бинарники и постоянные настройки

- В Git нет `.dll`, `.step`, `.x_t`, `.PcbDoc`, `.PcbLib`, `.SLDPRT`,
  `.SLDASM` и пользовательских данных.
- Registry, `.ins`, регистрация Add-in и постоянные Altium/SOLIDWORKS
  preferences не менялись. Assembly-as-part preference восстанавливается после
  каждого импорта (существующее поведение BRIDGE-000015).
- Временный Altium fixture handler удалён до коммита; установленный extension
  восстановлен из сохранённой чистой копии.

## 13. Ограничения

- Bottom placement, projection mismatch, Native/External, board reverse sync,
  Add-in registration, UI и installer — вне scope.
- Board-only STEP содержит субстрат без copper/pad/via drill holes; это
  соответствует контракту Outer/Hole contours, но не является полной
  производственной моделью платы.
- MiniPC outline не содержит Hole contours, поэтому MiniPC подтверждает только
  solid board probe. Реальные cutouts подтверждены на Desktop board (два
  отсутствия материала); полная геометрия этой платы не заявлена.
- Desktop board fixture отклонён из-за неполного snapshot outline; это
  ограничение существующего outline adapter, не BRIDGE-000019.
- Полная MiniPC assembly со всеми 1046 компонентами не собиралась.

## 14. Открытый вопрос для ревью

**Вопрос:** можно ли принять BRIDGE-000019, если MiniPC проходит полный
geometry gate, а реальная плата с вырезами подтверждает cutout probes, но её
snapshot outline неполный и поэтому production inspector её отклоняет?

Варианты:

1. **Принять текущий PR** (рекомендуется для этого этапа): board export,
   package contract, import, assembly и cutout point checks реализованы;
   ограничение outline adapter зафиксировать отдельной задачей.
2. **Расширить BRIDGE-000019**: исправить outline adapter для partial/unmatched
   loops, перегенерировать пакет и повторить Smoke B до `success=true`.
3. **Заменить fixture**: найти/создать другую реальную плату с полным outline
   и сквозным вырезом; текущая попытка SDK-fixture не сохраняется Altium.

Рекомендация: вариант 1, отдельная задача на outline adapter; не ослаблять
production geometry gate и не объявлять Desktop board полностью валидированной.
